import test from "node:test";
import assert from "node:assert/strict";
import {
  FRAME_RING_CAPACITY, FrameRingReader, analyzeSegment, analyzeRecords, nearestRank, planPageReads, ringWriteSlot, windowsByFrameTime, describeFrames,
} from "./ring-analysis.mjs";

const CAP = FRAME_RING_CAPACITY;
const mod = (value) => ((value % CAP) + CAP) % CAP;

// A hand-built poll record. w0 is the write cursor at the first read, k the writes that happen while the slots are read.
// The count is fixed at 1000 kept frames: head = w - count, which is how the real fields relate (see ring-analysis.mjs).
function record({ from, w0, n, f0, vals, k = 0, wallMs0 = f0 * 10, count = 1000, phase = "combat" }) {
  return { phase, wallMs0, wallMs: wallMs0 + 5, f0, h0: mod(w0 - count), c0: count, from, n,
    vals: vals ?? Array(n).fill(10), f1: f0 + k, h1: mod(w0 + k - count), c1: count };
}

function fixtureSegment(values, expectedFrames = values.length) {
  return {
    id: 0, startSequence: 0, endSequence: expectedFrames, integrityErrors: [],
    frames: values.map((ms, sequence) => ({ sequence, ms, wallMs: 0 })),
  };
}

test("nearest-rank uses a per-frame distribution, includes endpoints, and leaves input alone", () => {
  const values = [50, 10, 10, 10, 10, 10, 10, 10, 10, 10, 10, 10, 10, 10, 10, 10, 10, 10, 10, 10];
  assert.equal(nearestRank(values, 0.95), 10);
  assert.equal(nearestRank(values, 0.99), 50);
  assert.equal(nearestRank(values, 0), 10);
  assert.equal(nearestRank(values, 1), 50);
  assert.equal(values[0], 50);
  assert.equal(nearestRank([], 0.95), null);
  assert.throws(() => nearestRank([NaN], 0.95));
  assert.throws(() => nearestRank([8], 95));
});

test("the write slot is (head + count) mod 4096, and an age eviction (head up, count down) does not move it", () => {
  assert.equal(ringWriteSlot(100, 3000), 3100);
  assert.equal(ringWriteSlot(4000, 200), 104);
  assert.equal(ringWriteSlot(101, 2999), ringWriteSlot(100, 3000)); // one frame evicted by age, none written
  assert.equal(ringWriteSlot(0, 4096), 0);
});

test("page reads cover the slots in write order, at most 200 each, never across the array end", () => {
  assert.deepEqual(planPageReads(10, 0), []);
  assert.deepEqual(planPageReads(10, 70), [{ offset: 10, limit: 70 }]);
  assert.deepEqual(planPageReads(0, 450), [{ offset: 0, limit: 200 }, { offset: 200, limit: 200 }, { offset: 400, limit: 50 }]);
  assert.deepEqual(planPageReads(4090, 20), [{ offset: 4090, limit: 6 }, { offset: 0, limit: 14 }]);
  assert.deepEqual(planPageReads(4000, 300), [{ offset: 4000, limit: 96 }, { offset: 0, limit: 200 }, { offset: 200, limit: 4 }]);
  for (const [from, n] of [[0, 4095], [123, 4000], [4095, 1], [2000, 2500]]) {
    const reads = planPageReads(from, n);
    assert.equal(reads.reduce((s, r) => s + r.limit, 0), n);
    assert.ok(reads.every((r) => r.limit >= 1 && r.limit <= 200 && r.offset + r.limit <= CAP));
  }
});

test("the first record only sets the baseline; later records add their slots in write order with running sequences", () => {
  const reader = new FrameRingReader();
  const base = reader.push(record({ from: null, w0: 500, n: 0, f0: 2000 }));
  assert.equal(base.accepted, true);
  assert.equal(base.added, 0);
  const second = reader.push(record({ from: 500, w0: 503, n: 3, f0: 2003, vals: [11, 12, 13] }));
  assert.deepEqual([second.beforeSequence, second.afterSequence, second.added, second.frameCountDelta], [0, 3, 3, 0]);
  const third = reader.push(record({ from: 503, w0: 505, n: 2, f0: 2005, vals: [14, 15] }));
  assert.deepEqual([third.beforeSequence, third.afterSequence], [3, 5]);
  const segment = reader.result().segments[0];
  assert.deepEqual(segment.frames.map((f) => f.ms), [11, 12, 13, 14, 15]);
  assert.deepEqual(segment.frames.map((f) => f.sequence), [0, 1, 2, 3, 4]);
  assert.equal(analyzeSegment(segment).missingFrames, 0);
});

test("a record that crosses the array end keeps the cursor arithmetic modulo 4096", () => {
  const reader = new FrameRingReader();
  reader.push(record({ from: null, w0: 4094, n: 0, f0: 9000 }));
  reader.push(record({ from: 4094, w0: 3, n: 5, f0: 9005, vals: [1, 2, 3, 4, 5] }));
  const segment = reader.result().segments[0];
  assert.equal(segment.endSequence, 5);
  assert.equal(reader.result().discontinuities.length, 0);
});

test("slots that may have been overwritten while they were read reject the poll and start a segment", () => {
  const reader = new FrameRingReader();
  reader.push(record({ from: null, w0: 100, n: 0, f0: 5000 }));
  // 4000 slots to read plus 200 writes during the read: slot 'from' is overwritten.
  const bad = reader.push(record({ from: 100, w0: mod(100 + 4000), n: 4000, f0: 5000 + 4000, k: 200 }));
  assert.equal(bad.accepted, true);
  const result = reader.result();
  assert.equal(result.segments.length, 2);
  assert.match(result.discontinuities[0].reason, /overwritten/);
  // exactly at the limit (n + k = 4096) is still proven
  const edge = new FrameRingReader();
  edge.push(record({ from: null, w0: 100, n: 0, f0: 5000 }));
  edge.push(record({ from: 100, w0: mod(100 + 4000), n: 4000, f0: 5000 + 4000, k: 96 }));
  assert.equal(edge.result().segments.length, 1);
});

test("a missing poll, an engine restart, a skipped read and an unproven wrap cannot join epochs", () => {
  const base = record({ from: null, w0: 100, n: 0, f0: 5000 });
  for (const [next, pattern] of [
    [record({ from: 90, w0: 110, n: 20, f0: 5020 }), /missing/],
    [record({ from: 100, w0: 110, n: 10, f0: 4990 }), /restarted/],
    [{ ...record({ from: 100, w0: 110, n: 10, f0: 5010 }), skipped: true, vals: [] }, /skipped/],
    [record({ from: 100, w0: 110, n: 10, f0: 5000 + CAP }), /wrap/],
    [record({ from: 100, w0: 110, n: 11, f0: 5010 }), /slot count/],
    [record({ from: 100, w0: 110, n: 10, f0: 5010, vals: [1, 2] }), /slot count/],
    [{ ...record({ from: 100, w0: 110, n: 10, f0: 5010 }), wallMs0: 1 }, /wall clock/],
  ]) {
    const reader = new FrameRingReader();
    reader.push(base);
    reader.push(next);
    const result = reader.result();
    assert.equal(result.segments.length, 2, pattern.source);
    assert.match(result.discontinuities[0].reason, pattern);
  }
});

test("bad record shapes fail closed and are listed", () => {
  for (const change of [
    { h0: -1 }, { h1: CAP }, { c0: CAP + 1 }, { n: 1.5 }, { f0: -1 }, { f1: NaN }, { wallMs: NaN }, { vals: "no" },
    { f1: 4999 }, { f1: 5000 + CAP }, { from: CAP }, { from: "x" },
  ]) {
    const result = analyzeRecords([{ ...record({ from: null, w0: 100, n: 0, f0: 5000 }), ...change }]);
    assert.equal(result.invalidRecords.length, 1, JSON.stringify(change));
    assert.equal(result.passesP95, false);
  }
  assert.equal(analyzeRecords([null]).invalidRecords.length, 1);
});

test("the cursor must advance as far as the engine frame counter: a difference is an integrity error that blocks a pass", () => {
  const reader = new FrameRingReader();
  reader.push(record({ from: null, w0: 100, n: 0, f0: 5000 }));
  // within tolerance (two frames either way): accepted without an error
  assert.equal(reader.push(record({ from: 100, w0: 150, n: 50, f0: 5052 })).frameCountDelta, -2);
  assert.equal(reader.push(record({ from: 150, w0: 200, n: 50, f0: 5100 })).frameCountDelta, 2);
  assert.equal(reader.result().segments[0].integrityErrors.length, 0);
  // the engine ran 30 frames more than the ring recorded: the ring is not "one slot per frame" for this poll
  const skewed = reader.push(record({ from: 200, w0: 250, n: 50, f0: 5180 }));
  assert.equal(skewed.frameCountDelta, -30);
  const segment = reader.result().segments[0];
  assert.equal(segment.integrityErrors.length, 1);
  assert.equal(segment.integrityErrors[0].kind, "frame-count");
  assert.equal(segment.frameCountMismatches, 1);
  assert.equal(analyzeSegment(segment).passesP95, false);
  // an interval before the bad poll is still analyzable
  assert.equal(analyzeSegment(segment, { endSequence: 100 }).passesP95, true);
});

test("a cursor that advances while the engine counter stands still is also an error (the stand-in's 'ring hole' mirrored)", () => {
  const reader = new FrameRingReader();
  reader.push(record({ from: null, w0: 100, n: 0, f0: 5000 }));
  const mark = reader.push(record({ from: 100, w0: 160, n: 60, f0: 5000 }));
  assert.equal(mark.frameCountDelta, 60);
  assert.equal(reader.result().segments[0].integrityErrors.length, 1);
});

test("invalid values remain missing, are not coerced to fast frames", () => {
  const reader = new FrameRingReader();
  reader.push(record({ from: null, w0: 100, n: 0, f0: 5000 }));
  const mark = reader.push(record({ from: 100, w0: 105, n: 5, f0: 5005, vals: [10, NaN, -1, "10", Infinity] }));
  assert.equal(mark.invalidValues, 4);
  const analysis = analyzeSegment(reader.result().segments[0]);
  assert.equal(analysis.knownFrames, 1);
  assert.equal(analysis.missingFrames, 4);
  assert.equal(analysis.passesP95, false);
});

test("5% slow or missing frames may pass; one more fails even when observed P95 is excellent", () => {
  const fiveMissing = analyzeSegment(fixtureSegment(Array(95).fill(10), 100));
  assert.equal(fiveMissing.passesP95, true);
  assert.equal(fiveMissing.conservativeP95FrameMs, 10);
  const sixMissing = analyzeSegment(fixtureSegment(Array(94).fill(10), 100));
  assert.equal(sixMissing.p95FrameMs, 10);
  assert.equal(sixMissing.passesP95, false);
  assert.equal(sixMissing.conservativeP95FrameMs, null);
  assert.equal(sixMissing.conservativeP95Unbounded, true);
  const sixSlow = analyzeSegment(fixtureSegment([...Array(94).fill(10), ...Array(6).fill(25)]));
  assert.equal(sixSlow.passesP95, false);
  assert.equal(sixSlow.p95FrameMs, 25);
  const mixed = analyzeSegment(fixtureSegment([...Array(94).fill(10), 25], 100));
  assert.equal(mixed.conservativeSlowFrames, 6);
  assert.equal(mixed.passesP95, false);
});

test("threshold uses the exact 1000/60 budget and small samples use nearest-rank conservatively", () => {
  assert.equal(analyzeSegment(fixtureSegment(Array(20).fill(1000 / 60))).passesP95, true);
  assert.equal(analyzeSegment(fixtureSegment(Array(20).fill(16.667))).passesP95, false);
  assert.equal(analyzeSegment(fixtureSegment(Array(18).fill(10), 19)).passesP95, false);
  assert.equal(analyzeSegment(fixtureSegment(Array(19).fill(10), 20)).passesP95, true);
  assert.equal(analyzeSegment(fixtureSegment([])).passesP95, false);
});

test("the FPS boundary is reciprocal P95 frame time, while average FPS uses mean frame time", () => {
  const result = analyzeSegment(fixtureSegment([10, 30]));
  assert.equal(result.meanFrameMs, 20);
  assert.equal(result.meanFps, 50);
  assert.equal(result.p5FpsBoundary, 1000 / 30);
  assert.equal(result.p99FrameMs, 30);
  assert.equal(result.totalKnownMs, 40);
});

test("explicit interval bounds exclude warmup/recovery and reject invalid or duplicate records", () => {
  const segment = fixtureSegment([100, 10, 10, 100]);
  const result = analyzeSegment(segment, { startSequence: 1, endSequence: 3 });
  assert.equal(result.knownFrames, 2);
  assert.equal(result.passesP95, true);
  assert.throws(() => analyzeSegment(segment, { endSequence: 5 }));
  assert.throws(() => analyzeSegment(segment, { startSequence: -1 }));
  assert.throws(() => analyzeSegment(segment, { targetFps: 0 }));
  assert.throws(() => analyzeSegment({ ...segment, frames: [...segment.frames, segment.frames[1]] }));
});

test("windows are consecutive 30 s of accumulated frame time over one distribution, the short tail is dropped", () => {
  const values = [...Array(3000).fill(10), ...Array(1500).fill(20), ...Array(500).fill(10)]; // 30 s, 30 s, 5 s
  const windows = windowsByFrameTime(values, 30000);
  assert.equal(windows.length, 2);
  assert.deepEqual([windows[0].frames, windows[0].p95Ms, windows[1].frames, windows[1].p95Ms], [3000, 10, 1500, 20]);
  assert.equal(describeFrames([]), null);
  assert.equal(describeFrames([10, 10]).fps, 100);
});

test("exported results are detached and JSON-safe", () => {
  const reader = new FrameRingReader();
  reader.push(record({ from: null, w0: 100, n: 0, f0: 5000 }));
  reader.push(record({ from: 100, w0: 104, n: 4, f0: 5004 }));
  const first = reader.result();
  first.segments[0].frames[0].ms = -1;
  first.segments[0].integrityErrors.push({ sequence: 0 });
  const second = reader.result();
  assert.notEqual(second.segments[0].frames[0].ms, -1);
  assert.equal(second.segments[0].integrityErrors.length, 0);
  assert.deepEqual(JSON.parse(JSON.stringify(second)), second);
});
