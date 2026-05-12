#!/usr/bin/env node

const path = require('path');
const { createRequire } = require('module');

const requireFromSim = createRequire(path.join(__dirname, '..', 'simulator', 'package.json'));
const mqtt = requireFromSim('mqtt');
const { v4: uuidv4 } = requireFromSim('uuid');

const args = process.argv.slice(2);

// Parse arguments
const opts = {
  eventId: args[0] || uuidv4(),
  sectorId: args[1] || 'A',
  spotId: args[2] || 'A-01',
  type: args[3] || 'STUCK_OCCUPIED', // STUCK_OCCUPIED, STUCK_FREE, FLAPPING
  mqttHost: args[4] || 'localhost',
  mqttPort: args[5] || '1883'
};

// Validate required parameters
if (!args[0]) {
  console.log('Usage: node insert-incident-event.js <eventId> [sectorId] [spotId] [type] [mqttHost] [mqttPort]');
  console.log('');
  console.log('Examples:');
  console.log('  node insert-incident-event.js my-event-123');
  console.log('  node insert-incident-event.js my-event-456 B B-15 STUCK_FREE');
  console.log('  node insert-incident-event.js my-event-789 C C-30 FLAPPING localhost 1883');
  console.log('');
  console.log('Types: STUCK_OCCUPIED, STUCK_FREE, FLAPPING');
  console.log('Default sector: A, Default spot: A-01, Default type: STUCK_OCCUPIED');
  process.exit(1);
}

const client = mqtt.connect(`mqtt://${opts.mqttHost}:${opts.mqttPort}`);

function buildIncidentEvent() {
  return {
    eventId: opts.eventId,
    ts: new Date().toISOString(),
    sectorId: opts.sectorId,
    spotId: opts.spotId,
    type: opts.type,
    severity: 1,
    source: 'incident-monitor',
    status: 'open'
  };
}

client.on('connect', () => {
  const topic = `campus/parking/sectors/${opts.sectorId}/incidents`;
  const payload = JSON.stringify(buildIncidentEvent());

  client.publish(topic, payload, { qos: 1 }, (err) => {
    if (err) {
      console.error(`Publish failed: ${err.message}`);
      client.end(true, () => process.exit(1));
      return;
    }
    console.log(`✓ Incident event published successfully`);
    console.log(`  Topic: ${topic}`);
    console.log(`  EventID: ${opts.eventId}`);
    console.log(`  Spot: ${opts.spotId}`);
    console.log(`  Type: ${opts.type}`);
    client.end(true, () => process.exit(0));
  });
});

client.on('error', (err) => {
  console.error(`MQTT connection error: ${err.message}`);
  client.end(true, () => process.exit(1));
});
