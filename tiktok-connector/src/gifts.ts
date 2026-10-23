import type { GiftMapping } from './config.js';

export interface CatalogGift { id: string; name: string; coins: number | null; }
export interface GiftEvent {
  giftId: string;
  name?: string;
  coins?: number;
  userId?: string;
  viewerName?: string;
  groupId?: string;
  msgId?: string;
  giftType?: number;
  repeatCount: number;
  repeatEnd: boolean;
}

function asRecord(value: unknown): Record<string, unknown> | undefined {
  return value && typeof value === 'object' ? value as Record<string, unknown> : undefined;
}
function stringId(value: unknown): string | undefined {
  if (typeof value === 'string' && /^\d+$/.test(value) && value !== '0') return value;
  if (typeof value === 'number' && Number.isSafeInteger(value) && value > 0) return String(value);
  // Protobuf Long objects retain the full decimal ID in toString().
  if (value && typeof value === 'object' &&
      typeof (value as { toString?: unknown }).toString === 'function') {
    const result = String(value);
    if (/^\d+$/.test(result) && result !== '0') return result;
  }
  return undefined;
}
function positiveInteger(value: unknown): number | undefined {
  return typeof value === 'number' && Number.isSafeInteger(value) && value > 0 ? value : undefined;
}

// v2.4.0 currently exposes both protobuf fields and optional extended metadata.
export function parseGiftEvent(value: unknown): GiftEvent | null {
  const event = asRecord(value);
  if (!event) return null;
  const details = asRecord(event.giftDetails);
  const gift = asRecord(event.gift);
  const extended = asRecord(event.extendedGiftInfo);
  const common = asRecord(event.common);
  const user = asRecord(event.user);
  const giftId = stringId(event.giftId);
  const repeatCount = positiveInteger(event.repeatCount);
  if (!giftId || !repeatCount) return null;
  const cost = positiveInteger(extended?.diamond_count) ?? positiveInteger(extended?.diamondCount)
    ?? positiveInteger(details?.diamondCount) ?? positiveInteger(gift?.diamondCount);
  return {
    giftId,
    name: [details?.giftName, extended?.name, gift?.name].find(v => typeof v === 'string') as string | undefined,
    coins: cost,
    userId: stringId(user?.userId) ?? stringId(user?.id),
    viewerName: [user?.uniqueId, event.uniqueId, user?.nickname, event.nickname]
      .find(v => typeof v === 'string' && v.trim().length > 0) as string | undefined,
    groupId: stringId(event.groupId),
    msgId: stringId(common?.msgId) ?? stringId(event.msgId),
    giftType: typeof details?.giftType === 'number' ? details.giftType
      : typeof gift?.type === 'number' ? gift.type : undefined,
    repeatCount,
    repeatEnd: event.repeatEnd === true || event.repeatEnd === 1,
  };
}

export function parseCatalog(value: unknown): CatalogGift[] | null {
  if (!Array.isArray(value)) return null;
  const result: CatalogGift[] = [];
  for (const entry of value) {
    const gift = asRecord(entry);
    if (!gift) continue;
    const id = stringId(gift.id);
    const name = gift.name;
    if (!id || typeof name !== 'string') continue;
    result.push({ id, name, coins: positiveInteger(gift.diamond_count) ?? positiveInteger(gift.diamondCount) ?? null });
  }
  return result;
}

export class GiftResolver {
  private byId = new Map<string, GiftMapping>();
  private ambiguousIds = new Set<string>();
  private catalogAvailable = false;
  constructor(private readonly mappings: GiftMapping[], private readonly log: (message: string) => void) {}

  resolveCatalog(catalog: CatalogGift[] | null): void {
    this.byId.clear();
    this.ambiguousIds.clear();
    this.catalogAvailable = catalog !== null;
    if (!catalog) { this.log('[TikTok] Gift catalog unavailable; conservative event-metadata fallback enabled'); return; }
    for (const mapping of this.mappings) {
      const candidates = catalog.filter(g => mapping.giftId ? g.id === mapping.giftId :
        (g.name === mapping.name || (mapping.name === 'Galaxy' && g.name === 'Galáxia')));
      if (candidates.length !== 1) {
        this.log(`[TikTok] Gift ${mapping.name}: ${candidates.length ? 'ambiguous' : 'not found'} in catalog; disabled`);
        continue;
      }
      const candidate = candidates[0];
      if ((!mapping.giftId && candidate.name !== mapping.name &&
          !(mapping.name === 'Galaxy' && candidate.name === 'Galáxia')) ||
          candidate.coins === null || candidate.coins !== mapping.coins) {
        this.log(`[TikTok] Gift ${mapping.name}: catalog name/cost mismatch (name=${candidate.name}, coins=${candidate.coins}); disabled`);
        continue;
      }
      if (this.byId.has(candidate.id) || this.ambiguousIds.has(candidate.id)) {
        this.log(`[TikTok] Gift ID ${candidate.id} ambiguous; disabled`);
        this.byId.delete(candidate.id);
        this.ambiguousIds.add(candidate.id);
        continue;
      }
      this.byId.set(candidate.id, mapping);
      this.log(`[TikTok] Resolved ${mapping.name}: id=${candidate.id}, coins=${candidate.coins}, ${mapping.type} x${mapping.quantity}`);
    }
  }

  resolve(event: GiftEvent): GiftMapping | null {
    if (this.catalogAvailable) return this.byId.get(event.giftId) ?? null;
    // No catalog: require exact name AND cost. Similar/translated names fail closed.
    const matches = this.mappings.filter(g =>
      (g.name === event.name || (g.name === 'Galaxy' && event.name === 'Galáxia')) && g.coins === event.coins &&
      (g.giftId === undefined || g.giftId === event.giftId));
    return matches.length === 1 ? matches[0] : null;
  }
}
