import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { config as loadEnvFile } from 'dotenv';

export type BallType = 'Orange' | 'Yellow' | 'Green' | 'Pink' | 'Inferno';
export interface GiftMapping {
  name: string;
  coins: number;
  type: BallType;
  quantity: number;
  giftId?: string;
}
export interface AppConfig {
  tiktokUsername: string;
  bridgePort: number;
  gifts: GiftMapping[];
}

export const configPath = fileURLToPath(new URL('../config.json', import.meta.url));
export const envPath = fileURLToPath(new URL('../../.env', import.meta.url));

export function normalizeUsername(value: string): string {
  return value.trim().replace(/^@/, '');
}

export function validateConfig(raw: unknown, requireUsername = true, env: NodeJS.ProcessEnv = process.env): AppConfig {
  if (!raw || typeof raw !== 'object') throw new Error('config.json must be an object');
  const input = raw as Record<string, unknown>;
  const username = normalizeUsername(env.TIKTOK_USERNAME ?? '');
  if (requireUsername && !/^[\w.]{2,30}$/.test(username))
    throw new Error('Set TIKTOK_USERNAME to a valid public-live @username in the project .env');
  const bridgePort = Number(env.PIXELLIVEMOD_BRIDGE_PORT);
  if (!/^\d+$/.test(env.PIXELLIVEMOD_BRIDGE_PORT ?? '') || !Number.isInteger(bridgePort) || bridgePort < 1024 || bridgePort > 65535)
    throw new Error('Set PIXELLIVEMOD_BRIDGE_PORT in the project .env to an integer from 1024 to 65535');
  if (!Array.isArray(input.gifts) || input.gifts.length !== 9)
    throw new Error('gifts must contain exactly the nine configured presents');

  const allowedNames = new Set(['Rose', 'Finger Heart', 'TikTok', 'Perfume', 'GG',
    'Rosa', 'Heart Me', 'Doughnut', 'Hand Heart']);
  const allowedTypes = new Set<BallType>(['Orange', 'Yellow', 'Green', 'Pink', 'Inferno']);
  const seen = new Set<string>();
  const ids = new Set<string>();
  const gifts = input.gifts.map((entry: unknown, index: number): GiftMapping => {
    if (!entry || typeof entry !== 'object') throw new Error(`gifts[${index}] must be an object`);
    const gift = entry as Record<string, unknown>;
    if (typeof gift.name !== 'string' || !allowedNames.has(gift.name))
      throw new Error(`gifts[${index}].name is not one of the nine supported presents`);
    if (seen.has(gift.name)) throw new Error(`Duplicate gift name: ${gift.name}`);
    seen.add(gift.name);
    if (!Number.isInteger(gift.coins) || Number(gift.coins) < 1)
      throw new Error(`${gift.name}: coins must be a positive integer`);
    if (!allowedTypes.has(gift.type as BallType)) throw new Error(`${gift.name}: invalid ball type`);
    if (!Number.isInteger(gift.quantity) || Number(gift.quantity) < 1 || Number(gift.quantity) > 20)
      throw new Error(`${gift.name}: quantity per gift must be 1..20`);
    if (gift.giftId !== undefined && (typeof gift.giftId !== 'string' || !/^\d+$/.test(gift.giftId)))
      throw new Error(`${gift.name}: giftId must be a numeric string when supplied`);
    if (typeof gift.giftId === 'string') {
      if (ids.has(gift.giftId)) throw new Error(`Duplicate giftId: ${gift.giftId}`);
      ids.add(gift.giftId);
    }
    return gift as unknown as GiftMapping;
  });
  for (const name of allowedNames) if (!seen.has(name)) throw new Error(`Missing gift: ${name}`);
  return { tiktokUsername: username, bridgePort, gifts };
}

export function loadConfig(): AppConfig {
  loadEnvFile({ path: envPath, quiet: true });
  return validateConfig(JSON.parse(readFileSync(configPath, 'utf8')));
}
