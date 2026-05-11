const path = require('path');
const { createRequire } = require('module');

const requireFromSim = createRequire(path.join(__dirname, '..', 'simulator', 'package.json'));
const mqtt = requireFromSim('mqtt');
const { v4: uuidv4 } = requireFromSim('uuid');

const args = process.argv.slice(2);
const opts = {
  mqttHost: args[0] || 'localhost',
  mqttPort: args[1] || '1883',
  sector: args[2] || 'A',
  startIndex: parseInt(args[3] || '1', 10),
  endIndex: parseInt(args[4] || '28', 10),
  delayMs: parseInt(args[5] || '1000', 10)
};

if (Number.isNaN(opts.startIndex) || Number.isNaN(opts.endIndex) || opts.startIndex > opts.endIndex) {
  console.error('Invalid start/end index.');
  process.exit(1);
}

const client = mqtt.connect(`mqtt://${opts.mqttHost}:${opts.mqttPort}`);

function delay(ms) {
  return new Promise((resolve) => setTimeout(resolve, ms));
}

function buildPayload(spotId) {
  return {
    eventId: uuidv4(),
    ts: new Date().toISOString(),
    sectorId: opts.sector,
    spotId,
    state: 'OCCUPIED',
    source: 'sensor'
  };
}

async function publishSpot(spotId) {
  const topic = `campus/parking/sectors/${opts.sector}/spots/${spotId}/events`;
  const payload = JSON.stringify(buildPayload(spotId));

  return new Promise((resolve) => {
    client.publish(topic, payload, { qos: 0 }, (err) => {
      if (err) {
        console.error(`Publish failed for ${spotId}: ${err.message}`);
        resolve(false);
        return;
      }
      console.log(`Published ${spotId}`);
      resolve(true);
    });
  });
}

client.on('connect', async () => {
  for (let i = opts.startIndex; i <= opts.endIndex; i += 1) {
    const idx = String(i).padStart(2, '0');
    const spotId = `${opts.sector}-${idx}`;
    await publishSpot(spotId);
    if (opts.delayMs > 0 && i < opts.endIndex) {
      await delay(opts.delayMs);
    }
  }

  client.end(true, () => process.exit(0));
});

client.on('error', (err) => {
  console.error(`MQTT connection error: ${err.message}`);
  client.end(true, () => process.exit(1));
});
