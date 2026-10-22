import { ControlEvent, TikTokLiveConnection, WebcastEvent } from 'tiktok-live-connector';
import { BridgeClient, splitCommands } from './bridge.js';
import { loadConfig } from './config.js';
import { GiftResolver, parseCatalog, parseGiftEvent } from './gifts.js';
import { SequenceCounter } from './sequence.js';

const log = (message: string): void => console.log(`${new Date().toISOString()} ${message}`);

async function main(): Promise<void> {
  const config = loadConfig();
  const bridge = new BridgeClient(config.bridgePort, log);
  bridge.start();
  const counter = new SequenceCounter(log);
  let stopped = false;
  let currentConnection: TikTokLiveConnection | null = null;
  const stop = (): void => {
    stopped = true;
    bridge.stop();
    void currentConnection?.disconnect().catch(error => log(`[TikTok] Shutdown error: ${String(error)}`));
  };
  process.once('SIGINT', stop);
  process.once('SIGTERM', stop);

  while (!stopped) {
    const resolver = new GiftResolver(config.gifts, log);
    const connection = new TikTokLiveConnection(config.tiktokUsername, {
      processInitialData: false,
      enableExtendedGiftInfo: false,
    });
    currentConnection = connection;
    // The 2.4.0 declaration's emitter base omits .on under strict TS 5.9,
    // although the installed runtime and README expose the method.
    const events = connection as unknown as { on(name: string, handler: (value: any) => void): void };
    let catalogReady = false;
    let disconnect!: () => void;
    const disconnected = new Promise<void>(resolve => { disconnect = resolve; });
    events.on(ControlEvent.ERROR, (error: unknown) => log(`[TikTok] Error: ${String(error)}`));
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
      const total = units * mapping.quantity;
      log(`[TikTok] Mapped: ${mapping.type} x${total}`);
      let commands;
      try { commands = splitCommands(mapping.type, total); }
      catch (error) { log(`[TikTok] Gift rejected: ${String(error)}`); return; }
      void (async () => {
        for (const command of commands) {
          const accepted = await bridge.submit(command);
          if (!accepted) log(`[TikTok] Spawn command not confirmed: ${command.type} x${command.quantity}, requestId=${command.requestId}`);
        }
      })().catch(error => log(`[TikTok] Gift dispatch error: ${String(error)}`));
    });

    try {
      log(`[TikTok] Connecting to @${config.tiktokUsername}`);
      const state = await connection.connect();
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
      log(`[TikTok] Connection failed: ${String(error)}`);
    } finally {
      catalogReady = false;
      try { await connection.disconnect(); } catch (error) { log(`[TikTok] Disconnect cleanup error: ${String(error)}`); }
      currentConnection = null;
    }
    if (!stopped) {
      log('[TikTok] Reconnecting in 5 seconds');
      await new Promise(resolve => setTimeout(resolve, 5000));
    }
  }
}

main().catch(error => { console.error(`[TikTok] Configuration/startup error: ${String(error)}`); process.exitCode = 1; });
