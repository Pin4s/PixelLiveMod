import { randomUUID } from 'node:crypto';
import type { AppConfig } from './config.js';
import type { SpawnCommand } from './bridge.js';

type RecordValue = Record<string, unknown>;
const record = (value: unknown): RecordValue | null => value && typeof value === 'object' ? value as RecordValue : null;
const id = (value: unknown): string | null => {
  const text = typeof value === 'number' && Number.isSafeInteger(value) ? String(value)
    : typeof value === 'string' || (value && typeof value === 'object') ? String(value) : '';
  return /^\d+$/.test(text) && text !== '0' ? text : null;
};
const count = (value: unknown): number | null => {
  const number = typeof value === 'string' && /^\d+$/.test(value) ? Number(value) : value;
  return typeof number === 'number' && Number.isSafeInteger(number) && number >= 0 ? number : null;
};

export class EngagementCounter {
  private roomId: string | null = null;
  private followed = new Set<string>();
  private likeMessages = new Set<string>();
  private likes = new Map<string, number>();

  constructor(private readonly config: AppConfig, private readonly log: (message: string) => void) {}
  progressFor(viewerId: string): number { return (this.likes.get(viewerId) ?? 0) % this.config.likeRewardThreshold; }
  countedLikesFor(viewerId: string): number { return this.likes.get(viewerId) ?? 0; }

  connect(roomId: string): void {
    if (!roomId || roomId === '0') throw new Error('A numeric room ID is required for engagement rewards');
    if (this.roomId !== roomId) {
      this.roomId = roomId;
      this.followed = new Set();
      this.likeMessages = new Set();
      this.likes = new Map();
      this.log(`[Engagement] New live room=${roomId}; follow and like state reset`);
    } else {
      this.log(`[Engagement] Reconnected to room=${roomId}; session state retained`);
    }
  }

  follow(raw: unknown): SpawnCommand | null {
    if (!this.config.followRewardEnabled || !this.roomId) return null;
    const event = record(raw), common = record(event?.common), display = record(common?.displayText);
    if (typeof display?.key !== 'string' || !display.key.includes('follow')) return null;
    const user = record(event?.user);
    const viewerId = id(user?.userId) ?? id(user?.id);
    if (!viewerId) { this.log('[Follow] Event lacks stable numeric viewer ID; skipped'); return null; }
    this.log(`[Follow] Valid event: viewerId=${viewerId}`);
    if (this.followed.has(viewerId)) { this.log(`[Follow] Duplicate discarded: viewerId=${viewerId}`); return null; }
    this.followed.add(viewerId);
    const viewerName = [user?.uniqueId, user?.nickname].find(v => typeof v === 'string' && v.trim()) as string | undefined;
    this.log(`[Follow] Reward recognized: viewerId=${viewerId}`);
    return { requestId: randomUUID(), source: 'follow', type: this.config.followRewardBallType,
      quantity: this.config.followRewardBallQuantity, batchId: randomUUID(), viewerId,
      viewerName: (viewerName ?? `Usuário ${viewerId}`).trim().slice(0, 80) };
  }

  like(raw: unknown): SpawnCommand[] {
    if (!this.config.likeRewardEnabled || !this.roomId) return [];
    const event = record(raw), common = record(event?.common);
    const messageId = id(common?.msgId) ?? id(event?.msgId);
    const user = record(event?.user);
    const viewerId = id(user?.userId) ?? id(user?.id);
    if (!viewerId || !messageId) {
      this.log('[Likes] Stable viewer or message ID unavailable; event skipped');
      return [];
    }
    // v3 count (legacy likeCount) is this user's increment in this message.
    // v3 total (legacy totalLikeCount) is room-wide and must never be applied
    // to a viewer's counter.
    const incremental = count(event?.count) ?? count(event?.likeCount);
    if (incremental === null || incremental <= 0 || this.likeMessages.has(messageId)) return [];
    this.likeMessages.add(messageId);
    const current = this.likes.get(viewerId) ?? 0;
    if (!Number.isSafeInteger(current + incremental)) {
      this.log(`[Likes] Counter overflow for viewerId=${viewerId}; event skipped`);
      return [];
    }
    const before = Math.floor(current / this.config.likeRewardThreshold);
    const updated = current + incremental;
    this.likes.set(viewerId, updated);
    const after = Math.floor(updated / this.config.likeRewardThreshold);
    this.log(`[Likes] ViewerId=${viewerId} validated +${incremental}; progress=${this.progressFor(viewerId)}/${this.config.likeRewardThreshold}`);
    const viewerName = [user?.uniqueId, user?.nickname].find(v => typeof v === 'string' && v.trim()) as string | undefined;
    const commands: SpawnCommand[] = [];
    for (let milestone = before + 1; milestone <= after; milestone++) {
      const target = milestone * this.config.likeRewardThreshold;
      this.log(`[Likes] ViewerId=${viewerId} milestone reached: ${target}`);
      commands.push({ requestId: randomUUID(), source: 'likes', type: this.config.likeRewardBallType,
        quantity: this.config.likeRewardBallQuantity, milestone: target, batchId: randomUUID(),
        viewerId, viewerName: (viewerName ?? `UsuÃ¡rio ${viewerId}`).trim().slice(0, 80) });
    }
    return commands;
  }
}
