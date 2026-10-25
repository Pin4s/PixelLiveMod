import { ControlEvent, TikTokLiveConnection, WebcastEvent } from 'tiktok-live-connector';
import { BridgeClient, splitCommands, type SpawnCommand } from './bridge.js';
import { loadConfig } from './config.js';
import { formatTikTokError, retryDelayForTikTokError } from './errors.js';
import { GiftResolver, parseCatalog, parseGiftEvent } from './gifts.js';
import { SequenceCounter } from './sequence.js';
import { EngagementCounter } from './engagement.js';
import { randomUUID } from 'node:crypto';
import { setTimeout as sleep } from 'node:timers/promises';

const log = (message: string): void => console.log(`${new Date().toISOString()} ${message}`);

async function main(): Promise<void> {
  const config = loadConfig();
  const bridge = new BridgeClient(config.bridgePort, log);
  bridge.start();
  const counter = new SequenceCounter(log);
  const engagement = new EngagementCounter(config, log);
  let rewardDispatch = Promise.resolve();
  const dispatchReward = (command: SpawnCommand): void => {
    rewardDispatch = rewardDispatch.then(async () => {
      const label = command.source === 'follow' ? '[Follow]' : '[Likes]';
      log(`${label} Reward requested: ${command.type} x${command.quantity}, requestId=${command.requestId}`);
      const accepted = await bridge.submit(command);
      log(`${label} Command ${accepted ? 'queued' : 'not confirmed'}: requestId=${command.requestId}${command.milestone ? `, milestone=${command.milestone}` : ''}`);
    }).catch(error => log(`[Engagement] Reward dispatch error: ${String(error)}`));
  };
  let stopped = false;
  const retryAbort = new AbortController();
  let currentConnection: TikTokLiveConnection | null = null;
  const stop = (): void => {
    stopped = true;
    retryAbort.abort();
    bridge.stop();
    void currentConnection?.disconnect().catch(error => log(`[TikTok] Shutdown error: ${String(error)}`));
  };
  process.once('SIGINT', stop);
  process.once('SIGTERM', stop);

  while (!stopped) {
    const resolver = new GiftResolver(config.gifts, log);
    const connection = new TikTokLiveConnection(config.tiktokUsername, {
      signApiKey: config.eulerStreamApiKey,
      processInitialData: false,
      enableExtendedGiftInfo: false,
    });
    currentConnection = connection;
    // The 2.4.0 declaration's emitter base omits .on under strict TS 5.9,
    // although the installed runtime and README expose the method.
    const events = connection as unknown as { on(name: string, handler: (value: any) => void): void };
    let catalogReady = false;
    let connecting = true;
    let retryDelayMs = 5000;
    let disconnect!: () => void;
    const disconnected = new Promise<void>(resolve => { disconnect = resolve; });
    events.on(ControlEvent.ERROR, (error: unknown) => {
      // connect() also rejects with this error; report it once in the catch block.
      if (!connecting) log(`[TikTok] Error: ${formatTikTokError(error)}`);
    });
    events.on(ControlEvent.DISCONNECTED, (event: { code: number; reason?: string }) => {
      log(`[TikTok] Disconnected: code=${event.code}, reason=${event.reason ?? 'unknown'}`);
      disconnect();
    });
    events.on(WebcastEvent.GIFT, (raw: unknown) => {
      if (!catalogReady) { log('[TikTok] Gift ignored while resolving room catalog'); return; }
      const event = parseGiftEvent(raw);
      if (!event) { log('[TikTok] Gift with invalid metadata ignored'); return; }
      const mapping = resolver.resolve(event);
      if (!mapping) {
        log(`[TikTok] Unmapped/unidentified gift ignored: id=${event.giftId}, name=${event.name ?? '<unknown>'}, coins=${event.coins ?? '<unknown>'}`);
        return;
      }
      const units = counter.count(event);
      if (units === 0) return;
      log(`[TikTok] Gift received: ${mapping.name}, units=${units}, group=${event.groupId ?? '<none>'}`);
      const gift = event.userId && event.viewerName ? {
        batchId: randomUUID(), viewerId: event.userId,
        viewerName: event.viewerName.trim().slice(0, 80),
        giftId: event.giftId, giftName: mapping.name,
        giftUnits: units,
      } : undefined;
      if (mapping.type === 'Galaxy') {
        const command = { requestId: randomUUID(), action: 'GalaxyReset' as const, ...gift };
        void bridge.submit(command).then(accepted => {
          if (!accepted) log(`[TikTok] Galaxy reset not accepted: requestId=${command.requestId}`);
        });
        return;
      }
      const total = units * mapping.quantity;
      log(`[TikTok] Mapped: ${mapping.type} x${total}`);
      if (!gift) log('[TikTok] Stable viewer ID or public name unavailable; spawning without feed metadata');
      let commands;
      try { commands = splitCommands(mapping.type, total, gift); }
      catch (error) { log(`[TikTok] Gift rejected: ${String(error)}`); return; }
      void (async () => {
        for (const command of commands) {
          const accepted = await bridge.submit(command);
          if (!accepted) log(`[TikTok] Spawn command not confirmed: ${command.type} x${command.quantity}, requestId=${command.requestId}`);
        }
      })().catch(error => log(`[TikTok] Gift dispatch error: ${String(error)}`));
    });
    events.on(WebcastEvent.FOLLOW, (raw: unknown) => {
      const command = engagement.follow(raw);
      if (!command) return;
      dispatchReward(command);
    });
    events.on(WebcastEvent.LIKE, (raw: unknown) => {
      for (const command of engagement.like(raw)) {
        dispatchReward(command);
      }
    });

    try {
      log(`[TikTok] Connecting to @${config.tiktokUsername}${config.tiktokRoomId ? `, room=${config.tiktokRoomId}` : ''}`);
      const state = await connection.connect(config.tiktokRoomId);
      connecting = false;
      engagement.connect(String(state.roomId));
      log(`[TikTok] Connected to @${config.tiktokUsername}, room=${state.roomId || '<unknown>'}`);
      try {
        const catalog = parseCatalog(await connection.fetchAvailableGifts());
        resolver.resolveCatalog(catalog);
      } catch (error) {
        log(`[TikTok] Gift catalog request failed: ${String(error)}`);
        resolver.resolveCatalog(null);
      }
      catalogReady = true;
      await disconnected;
    } catch (error) {
      log(`[TikTok] Connection failed: ${formatTikTokError(error)}`);
      retryDelayMs = retryDelayForTikTokError(error);
    } finally {
      catalogReady = false;
      try { await connection.disconnect(); } catch (error) { log(`[TikTok] Disconnect cleanup error: ${String(error)}`); }
      currentConnection = null;
    }
    if (!stopped) {
      log(`[TikTok] Reconnecting in ${retryDelayMs / 1000} seconds`);
      await sleep(retryDelayMs, undefined, { signal: retryAbort.signal }).catch(() => {});
    }
  }
}

main().catch(error => { console.error(`[TikTok] Configuration/startup error: ${String(error)}`); process.exitCode = 1; });
