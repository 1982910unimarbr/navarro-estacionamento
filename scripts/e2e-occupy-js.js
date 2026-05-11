const path = require('path');
const { createRequire } = require('module');
const http = require('http');
const https = require('https');

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
  delayMs: parseInt(args[5] || '1000', 10),
  simulatorUrl: args[6] || 'http://localhost:3000',
  noFlip: (args[7] || '').toLowerCase() === 'true'
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

function postJson(urlText, body) {
  return new Promise((resolve) => {
    try {
      const url = new URL(urlText);
      const payload = JSON.stringify(body);
      const lib = url.protocol === 'https:' ? https : http;
      const req = lib.request({
        method: 'POST',
        hostname: url.hostname,
        port: url.port || (url.protocol === 'https:' ? 443 : 80),
        path: url.pathname,
        headers: {
          'Content-Type': 'application/json',
          'Content-Length': Buffer.byteLength(payload)
        }
      }, (res) => {
        res.resume();
        res.on('end', () => resolve(res.statusCode >= 200 && res.statusCode < 300));
      });

      req.on('error', () => resolve(false));
      req.write(payload);
      req.end();
    } catch (err) {
      resolve(false);
    }
  });
}

async function setNoFlip(spotId) {
  const ok = await postJson(`${opts.simulatorUrl}/simulator/failures`, {
    spotId,
    mode: 'no_flip'
  });

  if (!ok) {
    console.warn(`Failed to set no_flip for ${spotId}`);
  }
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
    if (opts.noFlip) {
      await setNoFlip(spotId);
    }
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
