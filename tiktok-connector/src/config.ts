import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { config as loadEnvFile } from 'dotenv';

export type BallType = 'Orange' | 'Yellow' | 'Green' | 'Pink' | 'Inferno';
export interface GiftMapping {
  name: string;
  coins: number;
  type: BallType | 'Galaxy';
  quantity: number;
  giftId?: string;
}
export interface AppConfig {
  tiktokUsername: string;
  tiktokRoomId?: string;
  eulerStreamApiKey?: string;
  bridgePort: number;
  gifts: GiftMapping[];
  followRewardEnabled: boolean;
  followRewardBallType: BallType;
  followRewardBallQuantity: number;
  likeRewardEnabled: boolean;
  likeRewardThreshold: number;
  likeRewardBallType: BallType;
  likeRewardBallQuantity: number;
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
  const roomId = env.TIKTOK_ROOM_ID?.trim();
  if (roomId && !/^\d+$/.test(roomId))
    throw new Error('TIKTOK_ROOM_ID must be a numeric ID of the current LIVE room when supplied');
  const eulerStreamApiKey = env.EULER_STREAM_API_KEY?.trim() || undefined;
  const bridgePort = Number(env.PIXELLIVEMOD_BRIDGE_PORT);
  if (!/^\d+$/.test(env.PIXELLIVEMOD_BRIDGE_PORT ?? '') || !Number.isInteger(bridgePort) || bridgePort < 1024 || bridgePort > 65535)
    throw new Error('Set PIXELLIVEMOD_BRIDGE_PORT in the project .env to an integer from 1024 to 65535');
  if (!Array.isArray(input.gifts) || input.gifts.length !== 10)
    throw new Error('gifts must contain the nine ball presents and Galaxy');

  const allowedNames = new Set(['Rose', 'Finger Heart', 'TikTok', 'Perfume', 'GG',
    'Rosa', 'Heart Me', 'Doughnut', 'Hand Heart', 'Galaxy']);
  const allowedTypes = new Set<GiftMapping['type']>(['Orange', 'Yellow', 'Green', 'Pink', 'Inferno', 'Galaxy']);
  const seen = new Set<string>();
  const ids = new Set<string>();
  const gifts = input.gifts.map((entry: unknown, index: number): GiftMapping => {
    if (!entry || typeof entry !== 'object') throw new Error(`gifts[${index}] must be an object`);
    const gift = entry as Record<string, unknown>;
    if (typeof gift.name !== 'string' || !allowedNames.has(gift.name))
      throw new Error(`gifts[${index}].name is not supported`);
    if (seen.has(gift.name)) throw new Error(`Duplicate gift name: ${gift.name}`);
    seen.add(gift.name);
    if (!Number.isInteger(gift.coins) || Number(gift.coins) < 1)
      throw new Error(`${gift.name}: coins must be a positive integer`);
    if (!allowedTypes.has(gift.type as GiftMapping['type'])) throw new Error(`${gift.name}: invalid gift action`);
    if (!Number.isInteger(gift.quantity) || Number(gift.quantity) < 1 || Number(gift.quantity) > 20)
      throw new Error(`${gift.name}: quantity per gift must be 1..20`);
    if (gift.name === 'Galaxy' && (gift.type !== 'Galaxy' || gift.quantity !== 1))
      throw new Error('Galaxy must map to exactly one Galaxy action');
    if (gift.name !== 'Galaxy' && gift.type === 'Galaxy')
      throw new Error('Only Galaxy may trigger the Galaxy action');
    if (gift.giftId !== undefined && (typeof gift.giftId !== 'string' || !/^\d+$/.test(gift.giftId)))
      throw new Error(`${gift.name}: giftId must be a numeric string when supplied`);
    if (typeof gift.giftId === 'string') {
      if (ids.has(gift.giftId)) throw new Error(`Duplicate giftId: ${gift.giftId}`);
      ids.add(gift.giftId);
    }
    return gift as unknown as GiftMapping;
  });
  for (const name of allowedNames) if (!seen.has(name)) throw new Error(`Missing gift: ${name}`);
  const bool = (key: string): boolean => {
    const value = input[key] ?? true;
    if (typeof value !== 'boolean') throw new Error(`${key} must be boolean`);
    return value;
  };
  const ball = (key: string, fallback: BallType): BallType => {
    const value = input[key] ?? fallback;
    if (!['Orange', 'Yellow', 'Green', 'Pink', 'Inferno'].includes(String(value)))
      throw new Error(`${key} must be a supported ball type`);
    return value as BallType;
  };
  const integer = (key: string, fallback: number, max: number): number => {
    const value = input[key] ?? fallback;
    if (!Number.isSafeInteger(value) || Number(value) < 1 || Number(value) > max)
      throw new Error(`${key} must be 1..${max}`);
    return Number(value);
  };
  return { tiktokUsername: username, tiktokRoomId: roomId || undefined, eulerStreamApiKey, bridgePort, gifts,
    followRewardEnabled: bool('followRewardEnabled'), followRewardBallType: ball('followRewardBallType', 'Pink'),
    followRewardBallQuantity: integer('followRewardBallQuantity', 3, 20),
    likeRewardEnabled: bool('likeRewardEnabled'), likeRewardThreshold: integer('likeRewardThreshold', 100, 1000000),
    likeRewardBallType: ball('likeRewardBallType', 'Orange'),
    likeRewardBallQuantity: integer('likeRewardBallQuantity', 1, 20) };
}

export function loadConfig(): AppConfig {
  loadEnvFile({ path: envPath, quiet: true });
  return validateConfig(JSON.parse(readFileSync(configPath, 'utf8')));
}
