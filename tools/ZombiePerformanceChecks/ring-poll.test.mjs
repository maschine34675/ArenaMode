import test from "node:test";
import assert from "node:assert/strict";
import fs from "node:fs";
import { FrameRingReader, analyzeSegment, FRAME_RING_CAPACITY } from "./ring-analysis.mjs";
import { RingPoller, openRing } from "./ring-poll.mjs";
import { SimGame, fakeBridge, CAPACITY, RETENTION_MS } from "./frame-stats-sim.mjs";

// The poller and the reader against a port of UiBridge's FrameStats (frame-stats-sim.mjs): the reconstruction must equal
// the frames the simulated game really rendered, in order, whatever the age eviction and the capacity overwrite do.

async function collect(game, { seconds, pollMs = 700, callMs = 20, fail } = {}) {
  const bridge = fakeBridge(game, { callMs, fail });
  const ring = await openRing(bridge.call);
  const poller = new RingPoller({ call: bridge.call, ring, now: () => game.time });
  const records = [];
  const end = game.time + seconds * 1000;
  while (game.time < end) {
    try { records.push(await poller.poll()); } catch { records.push({ failed: true }); }
    game.advance(pollMs);
  }
  return records;
}

function reconstruct(records) {
  const reader = new FrameRingReader();
  for (const record of records) if (!record.failed) reader.push(record);
  return reader.result();
}

// The baseline poll's engine frame tells which frame the reconstruction starts after: frameCount = 1000 + frames added.
const baselineAdds = (records) => records.find((r) => !r.failed).f0 - 1000;

test("the simulator keeps FrameStats' contract: _head is the oldest frame and moves by age, the write slot moves once per frame", () => {
  const game = new SimGame(() => 10);
  game.advance(40000); // 4000 frames at 100 fps: 30 s old frames are evicted, so head has moved by about 1000
  const { head, count } = game.stats;
  assert.ok(head > 900 && head < 1100, `head ${head}`);
  assert.ok(count > 2900 && count <= 3002, `count ${count}`);
  assert.equal((head + count) % CAPACITY, game.stats.adds % CAPACITY);
  assert.notEqual(head, game.stats.adds % CAPACITY);
});

test("the collector reproduces the exact frames at a steady rate, 6 000 of them across a ring wrap", async () => {
  const game = new SimGame(() => 10);
  game.advance(5000);
  const records = await collect(game, { seconds: 60 });
  const result = reconstruct(records);
  assert.equal(result.segments.length, 1);
  assert.equal(result.discontinuities.length, 0);
  const segment = result.segments[0];
  const start = baselineAdds(records);
  assert.equal(segment.endSequence, records.at(-1).f0 - records[0].f0);
  assert.ok(segment.frames.length > 5900);
  assert.deepEqual(segment.frames.map((f) => f.ms), game.truth.slice(start, start + segment.frames.length));
  assert.equal(segment.integrityErrors.length, 0);
  assert.equal(analyzeSegment(segment).missingFrames, 0);
});

test("a rate that falls and a rate above the ring's capacity rate (136 fps) still read exactly", async () => {
  const game = new SimGame((t) => (t < 30000 ? 6 : t < 60000 ? 9 : 16)); // 166 fps overruns 4096 frames in 30 s: the capacity branch
  game.advance(2000);
  const records = await collect(game, { seconds: 100 });
  const result = reconstruct(records);
  assert.equal(result.discontinuities.length, 0);
  const segment = result.segments[0];
  const start = baselineAdds(records);
  assert.deepEqual(segment.frames.map((f) => f.ms), game.truth.slice(start, start + segment.frames.length));
  assert.equal(segment.integrityErrors.length, 0);
});

test("a hitch is recorded when it happens, once, with no hole in front of it", async () => {
  const game = new SimGame((t) => (t >= 20000 && t < 20010 ? 8000 : 10));
  game.advance(1000);
  const records = await collect(game, { seconds: 60 });
  const result = reconstruct(records);
  const segment = result.segments[0];
  assert.equal(segment.integrityErrors.length, 0);
  const big = segment.frames.filter((f) => f.ms >= 2000);
  assert.equal(big.length, 1);
  assert.equal(big[0].ms, 8000);
  // the poll that delivered it is the first one after the stall, 20 s of game time into the run, not 30 s later
  const delivered = records.find((r) => !r.failed && r.vals.includes(8000));
  assert.ok(delivered.wallMs0 > 20000 && delivered.wallMs0 < 30000, `delivered at ${delivered.wallMs0}`);
});

test("a stand-in that treats _head as the cursor sees old frames and a head that stands still after a hitch", async () => {
  // This is what the 2026-11-19 stand-in collector did (read the slots between two head values), run against the port.
  const game = new SimGame((t) => (t >= 20000 && t < 20010 ? 8000 : 10));
  game.advance(1000);
  const bridge = fakeBridge(game, { callMs: 20 });
  const ring = await openRing(bridge.call);
  const readHead = async () => {
    const r = await bridge.call('inspect', { action: 'read', reads: [{ handle: ring.stats, member: '_head' }, { type: 'UnityEngine.Time', member: 'frameCount' }] });
    return { head: r.values[0].value.value, frame: r.frame, t: game.time };
  };
  let last = await readHead();
  const polls = [];
  for (let i = 0; i < 100; i++) {
    game.advance(700);
    const cur = await readHead();
    const n = (cur.head - last.head + CAPACITY) % CAPACITY;
    const vals = [];
    for (let k = 0; k < n; k++) vals.push(game.stats.ms[(last.head + k) % CAPACITY]);
    polls.push({ t: cur.t, frames: cur.frame - last.frame, n, vals });
    last = cur;
  }
  const withBig = polls.find((p) => p.vals.includes(8000));
  // the 8 s frame is only evicted (hence only read) after RETENTION_MS of its own age
  assert.ok(withBig.t > 20000 + RETENTION_MS, `read at ${withBig.t}`);
  // before that the head stood still while the engine counter advanced: the stand-in's "ring holes"
  const holes = polls.filter((p) => p.n === 0 && p.frames > 50 && p.t > 20000 + RETENTION_MS - 9000 && p.t < withBig.t);
  assert.ok(holes.length >= 8, `${holes.length} polls with a still head`);
  // and in the first 30 s it reads nothing at all: no frame is old enough to be evicted yet
  assert.ok(polls.filter((p) => p.t < RETENTION_MS).every((p) => p.n === 0));
});

test("the recorded stand-in polls show the same shape: a still head with frames advancing, then one entry of 10 s", () => {
  const lines = fs.readFileSync(new URL("./fixtures/stand-in-ring-hole.jsonl", import.meta.url), "utf8").split(/\r?\n/).filter(Boolean).map((l) => JSON.parse(l));
  const polls = lines.filter((l) => l.kind === "ring");
  assert.equal(polls.length, 12);
  const stalled = polls.filter((p) => p.h0 === p.h1 && p.f1 - p.f0 > 50 && p.vals.length === 0);
  assert.ok(stalled.length >= 6);
  const big = polls.filter((p) => p.vals.some((v) => v > 2000));
  assert.equal(big.length, 2);
  // the unit of the real ring values is milliseconds: at 60 to 120 fps the median frame is 8 to 17 ms. A ring in seconds or
  // in ticks would pass every simulated test (the simulator is in ms by construction) and fail only in a native run.
  const all = polls.flatMap((p) => p.vals).sort((a, b) => a - b);
  assert.ok(all.length > 100, `${all.length} values`);
  const median = all[Math.floor(all.length / 2)];
  assert.ok(median > 5 && median < 40, `median ${median}`);
  assert.ok(Math.max(...big[0].vals) > 10000 && Math.max(...big[1].vals) > 2000);
  // each big entry sits right after a run of still-head polls, and the polls themselves ran at ~85 fps (60 frames per 0.7 s)
  for (const b of big) {
    const i = polls.indexOf(b);
    assert.equal(polls[i - 1].h0, polls[i - 1].h1);
    assert.ok(b.f1 - b.f0 > 50);
  }
});

test("a bridge failure loses nothing: the next poll covers the gap", async () => {
  const game = new SimGame(() => 10);
  game.advance(2000);
  let failedOnce = false;
  const records = await collect(game, { seconds: 30, fail: (call) => { if (call === 40 && !failedOnce) { failedOnce = true; return true; } return false; } });
  assert.equal(records.filter((r) => r.failed).length, 1);
  const result = reconstruct(records);
  assert.equal(result.segments.length, 1);
  assert.equal(result.discontinuities.length, 0);
  const segment = result.segments[0];
  const start = baselineAdds(records);
  assert.deepEqual(segment.frames.map((f) => f.ms), game.truth.slice(start, start + segment.frames.length));
});

test("a gap of 4096 engine frames or more is never joined: the poller skips the read and the reader starts a segment", async () => {
  const game = new SimGame(() => 5);
  game.advance(1000);
  const bridge = fakeBridge(game, { callMs: 20 });
  const ring = await openRing(bridge.call);
  const poller = new RingPoller({ call: bridge.call, ring, now: () => game.time });
  const records = [await poller.poll()];
  game.advance(25000); // 5000 frames
  records.push(await poller.poll());
  game.advance(500);
  records.push(await poller.poll());
  assert.equal(records[1].skipped, true);
  assert.deepEqual(records[1].vals, []);
  const result = reconstruct(records);
  assert.equal(result.segments.length, 2);
  assert.match(result.discontinuities[0].reason, /skipped/);
  assert.equal(result.segments[1].frames.length > 0, true);
});

test("a ring that is not the 4096-slot one fails fast and names the required plugin", async () => {
  const oldPlugin = async (tool, args) => {
    if (args.action === 'root') return { value: { handle: 'p' } };
    if (args.member === '_frameStats') return { value: { handle: 's' } };
    throw new Error("no member '_ms' (the 0.9.1 ring has _samples/_next)");
  };
  await assert.rejects(() => openRing(oldPlugin), /Required: UiBridge plugin with FrameStats\._ms\/_head\/_count/);
  assert.equal(FRAME_RING_CAPACITY, CAPACITY);
});
