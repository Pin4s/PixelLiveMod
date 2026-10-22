import type { GiftEvent } from './gifts.js';

interface Streak { counted: number; ended: boolean; touched: number; }

export class SequenceCounter {
  private readonly streaks = new Map<string, Streak>();
  private readonly messages = new Map<string, number>();
  private readonly ttlMs = 10 * 60 * 1000;
  private events = 0;
  constructor(private readonly log: (message: string) => void) {}

  count(event: GiftEvent, now = Date.now()): number {
    if (++this.events % 200 === 0) this.prune(now);
    // Unknown gift type is treated conservatively as streakable, so a missing
    // type cannot turn cumulative 1,2,3... updates into 1+2+3... spawns.
    const isStreak = event.giftType !== 0;
    if (isStreak && event.groupId && event.userId) {
      const key = `${event.userId}:${event.giftId}:${event.groupId}`;
      const state = this.streaks.get(key) ?? { counted: 0, ended: false, touched: now };
      state.touched = now;
      if (state.ended || event.repeatCount <= state.counted) return 0;
      const delta = event.repeatCount - state.counted;
      state.counted = event.repeatCount;
      if (event.repeatEnd) state.ended = true;
      this.streaks.set(key, state);
      return delta;
    }
    if (isStreak && !event.repeatEnd) {
      this.log('[TikTok] Streak update lacks stable group/user ID; awaiting final count');
      return 0;
    }
    if (!event.msgId) {
      this.log('[TikTok] Gift lacks group/message ID; safe deduplication impossible, event skipped');
      return 0;
    }
    const key = `${event.userId ?? '?'}:${event.giftId}:${event.msgId}`;
    if (this.messages.has(key)) return 0;
    this.messages.set(key, now);
    return event.repeatCount;
  }

  private prune(now: number): void {
    for (const [key, state] of this.streaks)
      if (now - state.touched > this.ttlMs) this.streaks.delete(key);
    for (const [key, touched] of this.messages)
      if (now - touched > this.ttlMs) this.messages.delete(key);
  }
}
