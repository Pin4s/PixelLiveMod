import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { createServer } from 'node:net';
import { test } from 'node:test';
import { SignatureRateLimitError } from 'tiktok-live-connector';
import { BridgeClient, splitCommands } from '../bridge.js';
import { normalizeUsername, validateConfig } from '../config.js';
import { formatTikTokError, retryDelayForTikTokError } from '../errors.js';
import { GiftResolver, parseCatalog, parseGiftEvent, type GiftEvent } from '../gifts.js';
import { SequenceCounter } from '../sequence.js';

const exampleConfigPath = new URL('../../config.example.json', import.meta.url);
const exampleConfig = JSON.parse(readFileSync(exampleConfigPath, 'utf8'));
const testEnv = { TIKTOK_USERNAME: '', PIXELLIVEMOD_BRIDGE_PORT: '19081' };
const config = validateConfig(exampleConfig, false, testEnv);
const catalog = config.gifts.map((g, index) => ({ id: String(index + 101), name: g.name, coins: g.coins }));
const resolver = new GiftResolver(config.gifts, () => {});
resolver.resolveCatalog(catalog);
const event = (giftId: string, extra: Partial<GiftEvent> = {}): GiftEvent => ({
  giftId, repeatCount: 1, repeatEnd: true, giftType: 0, userId: '42', msgId: '1000', ...extra,
});

test('nine ball mappings retain their types and Galaxy is a separate action', () => {
  const expected = [
    ['Orange', 1], ['Orange', 5], ['Yellow', 1], ['Yellow', 5],
    ['Green', 1], ['Green', 5], ['Pink', 1], ['Pink', 5], ['Inferno', 1],
  ];
  for (let i = 0; i < expected.length; i++) {
    const mapped = resolver.resolve(event(String(i + 101)));
    assert.equal(mapped?.type, expected[i][0]);
    assert.equal(mapped?.quantity, expected[i][1]);
  }
  assert.equal(config.gifts[0].name, 'Rose');
  assert.equal(config.gifts[5].name, 'Rosa');
  assert.equal(resolver.resolve(event('110'))?.type, 'Galaxy');
  assert.equal(resolver.resolve(event('999')), null);
});

test('one Rose and five Roses map to one and five Orange balls', () => {
  const rose = resolver.resolve(event('101'))!;
  if (rose.type === 'Galaxy') throw new Error('Rose unexpectedly became an action');
  assert.deepEqual(splitCommands(rose.type, rose.quantity).map(c => c.quantity), [1]);
  assert.deepEqual(splitCommands(rose.type, 5 * rose.quantity).map(c => c.quantity), [5]);
});

test('Galaxy and Galáxia resolve only with the verified catalog cost', () => {
  const galaxy = config.gifts[9];
  const local = new GiftResolver(config.gifts, () => {});
  local.resolveCatalog([{ id: '900', name: 'Galáxia', coins: 1000 }]);
  assert.equal(local.resolve(event('900'))?.type, 'Galaxy');
  local.resolveCatalog([{ id: '900', name: 'Galaxy', coins: 999 }]);
  assert.equal(local.resolve(event('900')), null);
  local.resolveCatalog(null);
  assert.equal(local.resolve(event('900', { name: 'Galaxy', coins: 1000 }))?.type, 'Galaxy');
  assert.equal(local.resolve(event('900', { name: 'Galáxia', coins: 1000 }))?.type, 'Galaxy');
  assert.equal(local.resolve(event('900', { name: 'Galaxy', coins: 999 })), null);
  assert.equal(galaxy.quantity, 1);
});

test('catalog cost mismatch and ambiguous names fail closed', () => {
  const local = new GiftResolver(config.gifts, () => {});
  local.resolveCatalog([{ id: '101', name: 'Rose', coins: 2 },
    { id: '106', name: 'Rosa', coins: 10 }, { id: '206', name: 'Rosa', coins: 10 }]);
  assert.equal(local.resolve(event('101')), null);
  assert.equal(local.resolve(event('106')), null);
});

test('conservative fallback requires exact name and cost', () => {
  const local = new GiftResolver(config.gifts, () => {});
  local.resolveCatalog(null);
  assert.equal(local.resolve(event('55', { name: 'Rose', coins: 1 }))?.type, 'Orange');
  assert.equal(local.resolve(event('55', { name: 'Rosa', coins: 1 })), null);
  assert.equal(local.resolve(event('55', { name: 'rose', coins: 1 })), null);
});

test('v3 gift event fields and room catalog fields are parsed', () => {
  const parsed = parseGiftEvent({
    giftId: '101', repeatCount: 3, repeatEnd: 0, groupId: '777',
    common: { msgId: '9001' }, user: { id: '42', uniqueId: 'tha_math' },
    gift: { name: 'Rose', diamondCount: 1, type: 1 },
  });
  assert.deepEqual(parsed, {
    giftId: '101', name: 'Rose', coins: 1, userId: '42', viewerName: 'tha_math', groupId: '777',
    msgId: '9001', giftType: 1, repeatCount: 3, repeatEnd: false,
  });
  assert.deepEqual(parseCatalog([{ id: '101', name: 'Rose', diamond_count: 1 }]),
    [{ id: '101', name: 'Rose', coins: 1 }]);
});

test('protobuf Long viewer IDs remain precise and do not fall back to display names', () => {
  const parsed = parseGiftEvent({
    giftId: '101', repeatCount: 1, repeatEnd: 1,
    user: { id: { toString: () => '9007199254740993123' }, uniqueId: 'viewer' },
    common: { msgId: '89' }, gift: { type: 0 },
  });
  assert.equal(parsed?.userId, '9007199254740993123');
  assert.equal(parsed?.viewerName, 'viewer');
});

test('verified explicit ID tolerates translated catalog name but not wrong cost', () => {
  const mappings = structuredClone(config.gifts);
  mappings[0].giftId = '101';
  const local = new GiftResolver(mappings, () => {});
  local.resolveCatalog([{ id: '101', name: 'Rosa traduzida', coins: 1 }]);
  assert.equal(local.resolve(event('101'))?.name, 'Rose');
  local.resolveCatalog([{ id: '101', name: 'Rosa traduzida', coins: 10 }]);
  assert.equal(local.resolve(event('101')), null);
});

test('cumulative streak 1..10 then final produces exactly ten units', () => {
  const counter = new SequenceCounter(() => {});
  let units = 0;
  for (let i = 1; i <= 10; i++)
    units += counter.count(event('101', { giftType: 1, groupId: '77', repeatCount: i, repeatEnd: false, msgId: String(1000 + i) }));
  units += counter.count(event('101', { giftType: 1, groupId: '77', repeatCount: 10, repeatEnd: true, msgId: '2000' }));
  assert.equal(units, 10);
  assert.equal(counter.count(event('101', { giftType: 1, groupId: '77', repeatCount: 10, repeatEnd: true })), 0);
  assert.equal(counter.count(event('101', { giftType: 1, groupId: '78', repeatCount: 1, repeatEnd: true })), 1);
  assert.equal(counter.count(event('101', { giftType: 1, userId: '43', groupId: '77', repeatCount: 1, repeatEnd: true })), 1);
});

test('three Finger Hearts produce 15 balls; splitting respects 20 limit', () => {
  const total = 3 * config.gifts[1].quantity;
  assert.equal(total, 15);
  assert.deepEqual(splitCommands('Orange', total).map(c => c.quantity), [15]);
  assert.deepEqual(splitCommands('Orange', 55).map(c => c.quantity), [20, 20, 15]);
  assert.throws(() => splitCommands('Orange', 0));
});

test('split commands retain one gift batch and stable viewer identity', () => {
  const gift = { batchId: 'batch-1', viewerId: '42', viewerName: 'tha_math',
    giftId: '101', giftName: 'Rose', giftUnits: 55 };
  const commands = splitCommands('Orange', 55, gift);
  assert.deepEqual(commands.map(c => c.quantity), [20, 20, 15]);
  assert.ok(commands.every(c => c.batchId === gift.batchId && c.viewerId === '42' && c.giftUnits === 55));
  assert.equal(new Set(commands.map(c => c.requestId)).size, 3);
});

test('final-only fallback and duplicate individual message', () => {
  const counter = new SequenceCounter(() => {});
  assert.equal(counter.count(event('101', { giftType: 1, groupId: undefined, repeatCount: 2, repeatEnd: false })), 0);
  assert.equal(counter.count(event('101', { giftType: 1, groupId: undefined, repeatCount: 2, repeatEnd: true })), 2);
  assert.equal(counter.count(event('101', { giftType: 1, groupId: undefined, repeatCount: 2, repeatEnd: true })), 0);
  assert.equal(counter.count(event('101', { msgId: '222' })), 1);
  assert.equal(counter.count(event('101', { msgId: '222' })), 0);
});

test('missing gift type does not sum cumulative updates', () => {
  const counter = new SequenceCounter(() => {});
  assert.equal(counter.count(event('101', { giftType: undefined, groupId: undefined, repeatCount: 1, repeatEnd: false, msgId: '311' })), 0);
  assert.equal(counter.count(event('101', { giftType: undefined, groupId: undefined, repeatCount: 2, repeatEnd: false, msgId: '312' })), 0);
  assert.equal(counter.count(event('101', { giftType: undefined, groupId: undefined, repeatCount: 2, repeatEnd: true, msgId: '313' })), 2);
});

test('configuration rejects duplicate gifts and invalid quantity; @ normalized', () => {
  assert.equal(normalizeUsername('@viewer'), 'viewer');
  assert.equal(normalizeUsername('viewer'), 'viewer');
  const bad = structuredClone(config);
  bad.gifts[0].quantity = 0;
  assert.throws(() => validateConfig(bad, false, testEnv), /quantity/);
});

test('personal and local values come from environment, not gift config', () => {
  const withLegacyValues = { ...exampleConfig, tiktokUsername: 'old-user', bridgePort: 12345 };
  const loaded = validateConfig(withLegacyValues, true,
    { TIKTOK_USERNAME: '@live.user', EULER_STREAM_API_KEY: ' test-key ', PIXELLIVEMOD_BRIDGE_PORT: '19082' });
  assert.equal(loaded.tiktokUsername, 'live.user');
  assert.equal(loaded.tiktokRoomId, undefined);
  assert.equal(loaded.eulerStreamApiKey, 'test-key');
  assert.equal(loaded.bridgePort, 19082);
  assert.equal(validateConfig(exampleConfig, true,
    { TIKTOK_USERNAME: 'live.user', EULER_STREAM_API_KEY: ' ', PIXELLIVEMOD_BRIDGE_PORT: '19081' }).eulerStreamApiKey,
    undefined);
  assert.equal(validateConfig(exampleConfig, true,
    { TIKTOK_USERNAME: 'live.user', TIKTOK_ROOM_ID: '123456789', PIXELLIVEMOD_BRIDGE_PORT: '19081' }).tiktokRoomId,
    '123456789');
  assert.throws(() => validateConfig(exampleConfig, true,
    { TIKTOK_USERNAME: 'live.user', TIKTOK_ROOM_ID: 'old-room', PIXELLIVEMOD_BRIDGE_PORT: '19081' }), /TIKTOK_ROOM_ID/);
  assert.throws(() => validateConfig(exampleConfig, true,
    { PIXELLIVEMOD_BRIDGE_PORT: '19081' }), /TIKTOK_USERNAME/);
  assert.throws(() => validateConfig(exampleConfig, true,
    { TIKTOK_USERNAME: 'live.user', PIXELLIVEMOD_BRIDGE_PORT: 'invalid' }), /PIXELLIVEMOD_BRIDGE_PORT/);
});

test('connection failure logs each Room ID source instead of object placeholders', () => {
  const error = new Error('Failed to retrieve Room ID from all sources.');
  Object.assign(error, { config: { routeId: 'fetchRoomIdRoute', requestErrs: [
    new Error('HTML LiveRoom missing'), new Error('API user_not_found'), new Error('Euler permission denied'),
  ] } });
  const message = formatTikTokError({ info: 'Room ID lookup failed', exception: error });
  assert.match(message, /Room ID lookup failed/);
  assert.match(message, /source 1: Error: HTML LiveRoom missing/);
  assert.match(message, /source 2: Error: API user_not_found/);
  assert.match(message, /source 3: Error: Euler permission denied/);
  assert.doesNotMatch(message, /\[object Object\]/);
});

test('sign server rate limit preserves its cause and controls reconnection delay', () => {
  const error = new SignatureRateLimitError(undefined, 'Too many connections started.',
    { headers: { 'retry-after': '1116', 'x-ratelimit-reset': '1234567890' } });
  assert.match(formatTikTokError(error), /SignatureRateLimitError: \[Rate Limited\]/);
  assert.equal(error.retryAfter, 1116000);
  assert.equal(retryDelayForTikTokError(error), 1116000);
  assert.equal(retryDelayForTikTokError({ exception: error }), 1116000);
  assert.equal(retryDelayForTikTokError(new Error('offline')), 30000);
  assert.equal(retryDelayForTikTokError({ retryAfter: 1000 }), 30000);
});

test('JSON-line bridge receives a command and distinguishes queued acknowledgment', async () => {
  let received: Record<string, unknown> | null = null;
  const server = createServer(socket => {
    let buffer = '';
    socket.setEncoding('utf8');
    socket.on('data', (data: string) => {
      buffer += data;
      const end = buffer.indexOf('\n');
      if (end < 0) return;
      received = JSON.parse(buffer.slice(0, end)) as Record<string, unknown>;
      socket.write(JSON.stringify({ requestId: received.requestId, accepted: true, reason: 'queued, not yet created' }) + '\n');
    });
  });
  await new Promise<void>(resolve => server.listen(0, '127.0.0.1', resolve));
  const address = server.address();
  if (!address || typeof address === 'string') throw new Error('No local test port');
  const bridge = new BridgeClient(address.port, () => {});
  bridge.start();
  const accepted = await bridge.submit({ requestId: 'test-123', type: 'Pink', quantity: 5 });
  assert.equal(accepted, true);
  assert.deepEqual(received, { requestId: 'test-123', type: 'Pink', quantity: 5 });
  bridge.stop();
  await new Promise<void>(resolve => server.close(() => resolve()));
});

test('bridge keeps the same gift command pending while Unity restarts the round', async () => {
  const ids: string[] = [];
  const server = createServer(socket => {
    socket.setEncoding('utf8');
    socket.on('data', (data: string) => {
      for (const line of data.trim().split('\n')) {
        const command = JSON.parse(line) as { requestId: string };
        ids.push(command.requestId);
        socket.write(JSON.stringify({ requestId: command.requestId,
          accepted: ids.length > 1,
          reason: ids.length > 1 ? 'queued, not yet created' : 'round restarting; retry' }) + '\n');
      }
    });
  });
  await new Promise<void>(resolve => server.listen(0, '127.0.0.1', resolve));
  const address = server.address();
  if (!address || typeof address === 'string') throw new Error('No local test port');
  const bridge = new BridgeClient(address.port, () => {});
  try {
    bridge.start();
    assert.equal(await bridge.submit({ requestId: 'transition-123', type: 'Green', quantity: 2 }), true);
    assert.deepEqual(ids, ['transition-123', 'transition-123']);
  } finally {
    bridge.stop();
    await new Promise<void>(resolve => server.close(() => resolve()));
  }
});

test('Galaxy is sent as a special action without ball type or quantity', async () => {
  let received: Record<string, unknown> | null = null;
  const server = createServer(socket => {
    socket.setEncoding('utf8');
    socket.on('data', (data: string) => {
      received = JSON.parse(data.trim()) as Record<string, unknown>;
      socket.write(JSON.stringify({ requestId: received.requestId,
        accepted: true, reason: 'galaxy effect started' }) + '\n');
    });
  });
  await new Promise<void>(resolve => server.listen(0, '127.0.0.1', resolve));
  const address = server.address();
  if (!address || typeof address === 'string') throw new Error('No local test port');
  const bridge = new BridgeClient(address.port, () => {});
  try {
    bridge.start();
    assert.equal(await bridge.submit({ requestId: 'galaxy-1', action: 'GalaxyReset',
      viewerId: '42', viewerName: 'pixel_viewer' }), true);
    assert.deepEqual(received, { requestId: 'galaxy-1', action: 'GalaxyReset',
      viewerId: '42', viewerName: 'pixel_viewer' });
  } finally {
    bridge.stop();
    await new Promise<void>(resolve => server.close(() => resolve()));
  }
});
