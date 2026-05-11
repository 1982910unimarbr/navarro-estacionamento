const mqtt = require('mqtt');

const [topic, payload, host, port] = process.argv.slice(2);
if (!topic || !payload) {
  console.error('Usage: node mqtt-publish.js <topic> <payload> [host] [port]');
  process.exit(1);
}

const mqttHost = host || 'localhost';
const mqttPort = port || '1883';
const client = mqtt.connect(`mqtt://${mqttHost}:${mqttPort}`);

client.on('connect', () => {
  client.publish(topic, payload, { qos: 1 }, (err) => {
    if (err) {
      console.error(`Publish failed for ${topic}: ${err.message}`);
      client.end(true, () => process.exit(1));
      return;
    }
    console.log(`Published ${topic}`);
    client.end(true, () => process.exit(0));
  });
});

client.on('error', (err) => {
  console.error(`MQTT connection error: ${err.message}`);
  client.end(true, () => process.exit(1));
});
