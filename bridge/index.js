const mqtt = require('mqtt');
const axios = require('axios');

const MQTT_HOST = process.env.MQTT_HOST || 'localhost';
const MQTT_PORT = process.env.MQTT_PORT || 1883;
const BACKEND_URL = process.env.BACKEND_URL || 'http://localhost:5000';

const client = mqtt.connect(`mqtt://${MQTT_HOST}:${MQTT_PORT}`);

client.on('connect', ()=>{
  console.log('bridge connected to mqtt');
  client.subscribe('campus/parking/sectors/+/spots/+/events');
  client.subscribe('campus/parking/sectors/+/gateway/status');
});

client.on('message', async (topic, payload)=>{
  try{
    const body = payload.toString();
    // forward to backend ingest endpoint
    await axios.post(`${BACKEND_URL}/api/v1/internal/events`, body, { headers: { 'Content-Type':'application/json' } });
  }catch(err){
    console.error('forward error', err.message);
  }
});

console.log('bridge started');
