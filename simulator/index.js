/* Minimal parking simulator
 - 3 sectors (A,B,C) x 30 spots each
 - publishes MQTT events to campus/parking/sectors/<sector>/spots/<spot>/events
 - HTTP API to inject faults: /faults
*/
const mqtt = require('mqtt');
const express = require('express');
const { v4: uuidv4 } = require('uuid');
const { Mutex } = require('async-mutex');

const MQTT_HOST = process.env.MQTT_HOST || 'localhost';
const MQTT_PORT = process.env.MQTT_PORT || 1883;
const SIM_TIME_RATIO = parseInt(process.env.SIM_TIME_RATIO || '60', 10); // seconds per real second

const client = mqtt.connect(`mqtt://${MQTT_HOST}:${MQTT_PORT}`);

// Spots state
const sectors = ['A','B','C'];
const spots = {}; // key -> {sector, id, state, lastChange, stuckMode, flapping}
const mutex = new Mutex();

for(const s of sectors){
  for(let i=1;i<=30;i++){
    const id = `${s}-${String(i).padStart(2,'0')}`;
    spots[id] = {
      sector: s,
      id,
      state: 'FREE',
      lastChange: Date.now(),
      stuckMode: null, // 'stuck_occupied'|'stuck_free'
      flapping: false
    };
  }
}

function publishSpotEvent(spot, state, source='sensor'){
  const payload = {
    eventId: uuidv4(),
    ts: new Date().toISOString(),
    sectorId: spot.sector,
    spotId: spot.id,
    state,
    source
  };
  const topic = `campus/parking/sectors/${spot.sector}/spots/${spot.id}/events`;
  client.publish(topic, JSON.stringify(payload), {qos:0});
}

// Simulation loop: each real second simulates SIM_TIME_RATIO seconds
setInterval(async ()=>{
  const release = await mutex.acquire();
  try{
    const now = Date.now();
    // iterate spots and randomly change states with realistic patterns
    for(const id of Object.keys(spots)){
      const spot = spots[id];
      // if stuck mode, maybe still occasionally publish (for stuck detection)
      if(spot.stuckMode === 'stuck_occupied'){
        if(spot.state !== 'OCCUPIED'){
          spot.state = 'OCCUPIED';
          spot.lastChange = now;
          publishSpotEvent(spot, 'OCCUPIED');
        }
        continue;
      }
      if(spot.stuckMode === 'stuck_free'){
        if(spot.state !== 'FREE'){
          spot.state = 'FREE';
          spot.lastChange = now;
          publishSpotEvent(spot, 'FREE');
        }
        continue;
      }

      // Basic probabilistic model: base chance to change state depends on time of day (simulated)
      const hour = (Math.floor((now/1000)*SIM_TIME_RATIO/3600) % 24);
      // peak morning 8-10 and afternoon 17-19
      let pChange = 0.001; // per tick
      if((hour>=7 && hour<=10) || (hour>=16 && hour<=19)) pChange = 0.02;
      // small random chance
      if(Math.random() < pChange){
        const newState = spot.state === 'FREE' ? 'OCCUPIED' : 'FREE';
        // flapping injection
        if(spot.flapping && Math.random()<0.5){
          // flip twice quickly
          spot.state = newState;
          spot.lastChange = now;
          publishSpotEvent(spot, newState);
          // immediate opposite
          spot.state = (newState==='FREE'?'OCCUPIED':'FREE');
          spot.lastChange = now+100;
          publishSpotEvent(spot, spot.state);
        } else {
          spot.state = newState;
          spot.lastChange = now;
          publishSpotEvent(spot, newState);
        }
      }
    }
  }finally{release();}
}, 1000);

// Periodically publish gateway status
setInterval(()=>{
  for(const s of sectors){
    const topic = `campus/parking/sectors/${s}/gateway/status`;
    const payload = JSON.stringify({ts:new Date().toISOString(), sectorId:s, status:'OK'});
    client.publish(topic, payload);
  }
}, 5000);

// HTTP API for control and fault injection
const app = express();
app.use(express.json());

app.get('/spots', (req,res)=>{
  res.json(Object.values(spots));
});

async function applyFaultMode(spotId, mode, res){
  if(!spots[spotId]) return res.status(404).json({error:'unknown spot'});
  const release = await mutex.acquire();
  try{
    if(mode === 'clear'){
      spots[spotId].stuckMode = null;
      spots[spotId].flapping = false;
      return res.json({ok:true});
    }
    if(mode === 'stuck_occupied' || mode === 'stuck_free'){
      spots[spotId].stuckMode = mode;
      return res.json({ok:true});
    }
    if(mode === 'flapping'){
      spots[spotId].flapping = true;
      return res.json({ok:true});
    }
    return res.status(400).json({error:'invalid mode'});
  }finally{release();}
}

// Backward-compatible endpoint
app.post('/faults', async (req,res)=>{
  // {spotId, mode: 'stuck_occupied'|'stuck_free'|'flapping'|'clear'}
  const {spotId, mode} = req.body;
  return applyFaultMode(spotId, mode, res);
});

// Spec-aligned endpoints
app.post('/simulator/failures', async (req,res)=>{
  // {spotId, mode: 'stuck_occupied'|'stuck_free'|'flapping'}
  const {spotId, mode} = req.body;
  return applyFaultMode(spotId, mode, res);
});

app.post('/simulator/reset', async (req,res)=>{
  // {spotId}
  const {spotId} = req.body;
  return applyFaultMode(spotId, 'clear', res);
});

const port = process.env.PORT || 3000;
app.listen(port, ()=>console.log('Simulator HTTP control listening on',port));
