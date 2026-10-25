import { randomUUID } from 'node:crypto';
import { Socket } from 'node:net';
import type { BallType } from './config.js';

export interface GiftMetadata {
  batchId: string; viewerId: string; viewerName: string;
  giftId: string; giftName: string; giftUnits: number;
}
export interface RewardMetadata { source?: 'gift' | 'follow' | 'likes'; milestone?: number; }
export interface SpawnCommand extends Partial<GiftMetadata>, RewardMetadata { requestId: string; type: BallType; quantity: number; }
export interface GalaxyCommand extends Partial<GiftMetadata> { requestId: string; action: 'GalaxyReset'; }
export type BridgeCommand = SpawnCommand | GalaxyCommand;
interface Pending { command: BridgeCommand; resolve: (accepted: boolean) => void; }

export function splitCommands(type: BallType, total: number, gift?: GiftMetadata): SpawnCommand[] {
  if (!Number.isSafeInteger(total) || total < 1 || total > 10000)
    throw new Error(`Invalid mapped ball count ${total}; expected 1..10000`);
  const result: SpawnCommand[] = [];
  for (let remaining = total; remaining > 0; remaining -= 20)
    result.push({ requestId: randomUUID(), type, quantity: Math.min(20, remaining), ...gift });
  return result;
}

export class BridgeClient {
  private socket: Socket | null = null;
  private connecting = false;
  private buffer = '';
  private readonly queue: Pending[] = [];
  private active: Pending | null = null;
  private timeout: NodeJS.Timeout | null = null;
  private retryTimer: NodeJS.Timeout | null = null;
  private stopped = false;
  constructor(private readonly port: number, private readonly log: (message: string) => void) {}

  start(): void { this.connect(); }

  submit(command: BridgeCommand): Promise<boolean> {
    if (this.queue.length + (this.active ? 1 : 0) >= 256) {
      this.log(`[Bridge] Queue full: rejected ${'action' in command ? command.action : `${command.type} x${command.quantity}`} (${command.requestId})`);
      return Promise.resolve(false);
    }
    return new Promise(resolve => {
      this.queue.push({ command, resolve });
      this.pump();
    });
  }

  private connect(): void {
    if (this.stopped || this.connecting || this.socket) return;
    this.connecting = true;
    const socket = new Socket();
    socket.setEncoding('utf8');
    socket.connect(this.port, '127.0.0.1');
    socket.on('connect', () => {
      this.connecting = false;
      this.socket = socket;
      this.log(`[Bridge] Connected to 127.0.0.1:${this.port}`);
      this.pump();
    });
    socket.on('data', (chunk: string) => this.onData(chunk));
    socket.on('error', error => this.log(`[Bridge] Connection error: ${error.message}`));
    socket.on('close', () => {
      if (this.socket === socket) this.socket = null;
      this.connecting = false;
      if (this.timeout) clearTimeout(this.timeout);
      this.timeout = null;
      if (this.active) {
        this.log(`[Bridge] Outcome unknown after disconnect: ${this.active.command.requestId}; NOT retrying`);
        this.active.resolve(false);
        this.active = null;
      }
      this.buffer = '';
      if (!this.stopped) {
        this.log('[Bridge] Disconnected; retrying connection in 5 seconds');
        setTimeout(() => this.connect(), 5000);
      }
    });
  }

  private pump(): void {
    if (this.stopped || this.active || this.retryTimer || !this.socket || this.queue.length === 0) return;
    this.active = this.queue.shift()!;
    const command = this.active.command;
    this.socket.write(JSON.stringify(command) + '\n');
    this.log(`[Bridge] Command sent: ${'action' in command ? command.action : `${command.type} x${command.quantity}`}, requestId=${command.requestId}`);
    this.timeout = setTimeout(() => {
      this.log(`[Bridge] Acknowledgment timeout for ${command.requestId}; outcome unknown, NOT retrying`);
      this.socket?.destroy();
    }, 10000);
  }

  private onData(chunk: string): void {
    this.buffer += chunk;
    if (this.buffer.length > 8192) { this.socket?.destroy(new Error('Oversized bridge response')); return; }
    for (;;) {
      const end = this.buffer.indexOf('\n');
      if (end < 0) break;
      const line = this.buffer.slice(0, end);
      this.buffer = this.buffer.slice(end + 1);
      try {
        const reply = JSON.parse(line) as { requestId?: string; accepted?: boolean; reason?: string };
        if (!this.active || reply.requestId !== this.active.command.requestId) {
          this.log(`[Bridge] Unexpected acknowledgment ID ${reply.requestId ?? '<missing>'}`);
          continue;
        }
        if (this.timeout) clearTimeout(this.timeout);
        this.timeout = null;
        if (reply.accepted === false && (reply.reason === 'round restarting; retry' ||
          reply.reason === 'previous round; not spawned' || reply.reason === 'stale connection')) {
          const pending = this.active;
          this.active = null;
          this.queue.unshift(pending);
          this.retryTimer = setTimeout(() => {
            this.retryTimer = null;
            this.pump();
          }, 1000);
          continue;
        }
        const command = this.active.command;
        this.log(`[Unity] Command ${reply.accepted ? 'accepted' : 'rejected'}: ${'action' in command ? command.action : `${command.type} x${command.quantity}`}; ${reply.reason ?? ''}`);
        this.active.resolve(reply.accepted === true);
        this.active = null;
        this.pump();
      } catch (error) {
        this.log(`[Bridge] Invalid acknowledgment: ${String(error)}`);
      }
    }
  }

  stop(): void {
    this.stopped = true;
    this.socket?.destroy();
    this.socket = null;
    if (this.timeout) clearTimeout(this.timeout);
    if (this.retryTimer) clearTimeout(this.retryTimer);
    if (this.active) this.active.resolve(false);
    for (const item of this.queue) item.resolve(false);
    this.active = null;
    this.queue.length = 0;
  }
}
