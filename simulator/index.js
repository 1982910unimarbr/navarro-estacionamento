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
const SIM_TICK_MS = parseInt(process.env.SIM_TICK_MS || '10000', 10);
const OCCUPIED_HOLD_SEC = parseInt(process.env.OCCUPIED_HOLD_SEC || '10', 10);
const OCCUPIED_HOLD_MS = Math.max(OCCUPIED_HOLD_SEC, 0) * 1000;
const ARRIVAL_RATE_PER_MIN = parseFloat(process.env.ARRIVAL_RATE_PER_MIN || '30');
const DEPARTURE_RATE_PER_MIN = parseFloat(process.env.DEPARTURE_RATE_PER_MIN || '30');

const client = mqtt.connect(`mqtt://${MQTT_HOST}:${MQTT_PORT}`);

// Spots state
const sectors = ['A','B','C'];
const spots = {}; // key -> {sector, id, state, lastChange, stuckMode, flapping, occupiedUntil}
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
      flapping: false,
      occupiedUntil: null
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

function expectedCount(ratePerMin){
  const perTick = ratePerMin * (SIM_TICK_MS / 60000);
  const base = Math.floor(perTick);
  return base + (Math.random() < (perTick - base) ? 1 : 0);
}

function takeRandom(count, items){
  const copy = items.slice();
  const picked = [];
  const limit = Math.min(count, copy.length);
  for(let i = 0; i < limit; i++){
    const idx = Math.floor(Math.random() * copy.length);
    picked.push(copy.splice(idx, 1)[0]);
  }
  return picked;
}

// Simulation loop: each real second simulates SIM_TIME_RATIO seconds
setInterval(async ()=>{
  const release = await mutex.acquire();
  try{
    const now = Date.now();
    // iterate spots and apply entry/exit simulation
    for(const id of Object.keys(spots)){
      const spot = spots[id];
      if(spot.state === 'OCCUPIED' && spot.occupiedUntil && now >= spot.occupiedUntil){
        spot.state = 'FREE';
        spot.lastChange = now;
        spot.occupiedUntil = null;
        spot.stuckMode = null;
        publishSpotEvent(spot, 'FREE');
        continue;
      }
      // if stuck mode, maybe still occasionally publish (for stuck detection)
      if(spot.stuckMode === 'stuck_occupied'){
        if(spot.state !== 'OCCUPIED'){
          spot.state = 'OCCUPIED';
          spot.lastChange = now;
          spot.occupiedUntil = now + OCCUPIED_HOLD_MS;
          publishSpotEvent(spot, 'OCCUPIED');
        }
        continue;
      }
      if(spot.stuckMode === 'stuck_free'){
        if(spot.state !== 'FREE'){
          spot.state = 'FREE';
          spot.lastChange = now;
          spot.occupiedUntil = null;
          publishSpotEvent(spot, 'FREE');
        }
        continue;
      }
      if(spot.state === 'OCCUPIED' && spot.occupiedUntil && now < spot.occupiedUntil){
        continue;
      }
    }

    const freeSpots = Object.values(spots).filter(s => s.state === 'FREE');
    const occupiedSpots = Object.values(spots).filter(s => s.state === 'OCCUPIED' && (!s.occupiedUntil || now >= s.occupiedUntil));

    const arrivals = takeRandom(expectedCount(ARRIVAL_RATE_PER_MIN), freeSpots);
    const departures = takeRandom(expectedCount(DEPARTURE_RATE_PER_MIN), occupiedSpots);

    for(const spot of arrivals){
      spot.state = 'OCCUPIED';
      spot.lastChange = now;
      spot.occupiedUntil = now + OCCUPIED_HOLD_MS;
      publishSpotEvent(spot, 'OCCUPIED');
    }

    for(const spot of departures){
      spot.state = 'FREE';
      spot.lastChange = now;
      spot.occupiedUntil = null;
      publishSpotEvent(spot, 'FREE');
    }
  }finally{release();}
}, SIM_TICK_MS);

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
      spots[spotId].occupiedUntil = null;
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
