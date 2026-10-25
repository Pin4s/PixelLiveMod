import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { test } from 'node:test';
import { validateConfig } from '../config.js';
import { EngagementCounter } from '../engagement.js';

const raw = JSON.parse(readFileSync(new URL('../../config.example.json', import.meta.url), 'utf8'));
const config = validateConfig(raw, false, { PIXELLIVEMOD_BRIDGE_PORT: '19081' });
const follow = (userId: string) => ({ common: { displayText: { key: 'pm_mt_guidance_follow' } },
  user: { id: userId, uniqueId: `viewer_${userId}` } });
const like = (userId: string, amount: number, roomTotal: number, msgId: string) => ({
  user: { id: userId, uniqueId: `viewer_${userId}` }, count: amount,
  total: String(roomTotal), common: { msgId },
});

test('follow remains once per stable viewer ID per room', () => {
  const state = new EngagementCounter(config, () => {});
  state.connect('100');
  assert.equal(state.follow({ user: { id: '7', uniqueId: 'viewer' } }), null);
  const first = state.follow(follow('7'))!;
  assert.deepEqual([first.source, first.type, first.quantity, first.viewerId], ['follow', 'Pink', 3, '7']);
  assert.equal(state.follow(follow('7')), null);
  state.connect('100');
  assert.equal(state.follow(follow('7')), null);
  state.connect('101');
  assert.equal(state.follow(follow('7'))?.quantity, 3);
});

test('viewers earn independent milestones, multiple rewards, and remainders', () => {
  const state = new EngagementCounter(config, () => {});
  state.connect('200');
  assert.equal(state.like(like('7', 70, 70, '1')).length, 0);
  assert.equal(state.like(like('8', 80, 150, '2')).length, 0);
  assert.equal(state.progressFor('7'), 70);
  assert.equal(state.progressFor('8'), 80);
  const joao = state.like(like('7', 30, 180, '3'));
  assert.deepEqual(joao.map(c => [c.source, c.type, c.quantity, c.viewerId, c.viewerName, c.milestone]),
    [['likes', 'Orange', 1, '7', 'viewer_7', 100]]);
  const maria = state.like(like('8', 20, 200, '4'));
  assert.deepEqual(maria.map(c => [c.viewerId, c.milestone]), [['8', 100]]);
  const extra = state.like(like('7', 250, 450, '5'));
  assert.deepEqual(extra.map(c => [c.viewerId, c.milestone]), [['7', 200], ['7', 300]]);
  assert.equal(new Set(extra.map(c => c.requestId)).size, 2);
  assert.equal(state.progressFor('7'), 50);
  assert.equal(state.countedLikesFor('7'), 350);
  assert.equal(state.countedLikesFor('8'), 100);
});

test('room total is never treated as a viewer total; replay IDs survive reconnection', () => {
  const state = new EngagementCounter(config, () => {});
  state.connect('300');
  assert.equal(state.like(like('7', 60, 10060, '1')).length, 0);
  assert.equal(state.like(like('8', 40, 10100, '2')).length, 0);
  state.connect('300'); // Same LIVE after a game round reset or connector reconnection.
  assert.equal(state.like(like('7', 60, 10060, '1')).length, 0);
  assert.equal(state.like(like('7', 40, 11000, '3'))[0].viewerId, '7');
  assert.equal(state.countedLikesFor('7'), 100);
  assert.equal(state.countedLikesFor('8'), 40);
  state.connect('301');
  assert.equal(state.countedLikesFor('7'), 0);
  assert.equal(state.like(like('7', 100, 12000, '1'))[0].milestone, 100);
});

test('legacy likeCount is incremental; stable user and message IDs are required', () => {
  const state = new EngagementCounter(config, () => {});
  state.connect('400');
  assert.equal(state.like({ user: { id: '7' }, likeCount: 100, totalLikeCount: 3000 }).length, 0);
  assert.equal(state.like({ likeCount: 100, common: { msgId: '1' } }).length, 0);
  const legacy = { user: { id: { toString: () => '9007199254740993123' }, uniqueId: 'viewer' },
    likeCount: 100, totalLikeCount: 3000, common: { msgId: '2' } };
  assert.equal(state.like(legacy)[0].viewerId, '9007199254740993123');
  assert.equal(state.like(legacy).length, 0);
});
