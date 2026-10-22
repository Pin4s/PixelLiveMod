// Explicit local smoke test. This file is never imported by the live connector.
import { randomUUID } from 'node:crypto';
import { BridgeClient, splitCommands, type GiftMetadata } from './bridge.js';

const port = Number(process.env.PIXELLIVEMOD_BRIDGE_PORT ?? 19081);
if (!Number.isInteger(port) || port < 1024 || port > 65535) throw new Error('Invalid bridge port');
const bridge = new BridgeClient(port, console.log);
bridge.start();

async function send(viewerId: string, viewerName: string, units: number): Promise<void> {
  const gift: GiftMetadata = {
    batchId: randomUUID(), viewerId, viewerName, giftId: 'local-test-rose',
    giftName: 'Rose', giftUnits: units,
  };
  for (const command of splitCommands('Orange', units, gift)) {
    if (!await bridge.submit(command)) console.log(`Command ${command.requestId} was not queued`);
  }
}

let deadline: NodeJS.Timeout | undefined;
try {
  await Promise.race([
    (async () => {
      await send('local-test-user-1', 'teste_a', 1);
      await new Promise(resolve => setTimeout(resolve, 2000));
      await send('local-test-user-1', 'teste_a', 3);
      await send('local-test-user-2', 'teste_b', 1);
    })(),
    new Promise<never>((_, reject) => {
      deadline = setTimeout(() => reject(new Error('Bridge unavailable after 15 seconds')), 15000);
    }),
  ]);
} finally {
  if (deadline) clearTimeout(deadline);
  bridge.stop();
}
