import test from "node:test";
import assert from "node:assert/strict";
import { FrameRingReconstructor, analyzeSegment, analyzeSnapshots, nearestRank } from "./ring-analysis.mjs";

const CAPACITY = 300;
const mod = (value) => ((value % CAPACITY) + CAPACITY) % CAPACITY;

// Independent fake writer: kth Update writes value(k) at slot (k-1) modulo300.
// Each page has its OWN observation time, reproducing a torn two-page collection.
function ringAt(writes, value) {
  return Array.from({ length: CAPACITY }, (_, slot) => {
    const newestWrite = writes - mod(writes - 1 - slot);
    return value(newestWrite);
  });
}

function snapshot(writes, { d0 = 1, d1 = 2, d2 = 3, d3 = 4, after = 5, value = (k) => k + 1000, wallMs = writes * 10 } = {}) {
  return {
    f0: writes,
    n0: mod(writes + d0),
    page0: ringAt(writes + d1, value).slice(0, 200),
    page1: ringAt(writes + d2, value).slice(200),
    n1: mod(writes + d3),
    f1: writes + after,
    wallMs,
  };
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

test("initial history is excluded; touched slots recover later with correct epochs and no duplication", () => {
  const collector = new FrameRingReconstructor();
  const first = collector.push(snapshot(398)); // n0=99, initial global write399
  assert.equal(first.added, 0);
  assert.equal(first.afterSequence, 3);
  const second = collector.push(snapshot(519));
  const end = second.afterSequence;
  assert.equal(end, 124);
  collector.push(snapshot(638)); // wraps the ring and recovers the prior tail
  const segment = collector.result().segments[0];
  const analysis = analyzeSegment(segment, { endSequence: end });
  assert.equal(analysis.knownFrames, end);
  assert.equal(analysis.missingFrames, 0);
  assert.equal(segment.duplicateObservations > 0, true);
  for (const frame of segment.frames) assert.equal(frame.ms, 1400 + frame.sequence);
  assert.equal(new Set(segment.frames.map((frame) => frame.sequence)).size, segment.frames.length);
});

test("two-page races across both ring and page boundaries never misattribute a sample", () => {
  for (const initial of [590, 695, 790, 895]) {
    const collector = new FrameRingReconstructor();
    const base = initial + 1;
    for (let sample = 0; sample < 20; sample++) {
      const writes = initial + 71 * sample;
      collector.push(snapshot(writes, { d1: 10, d2: 18, d3: 22, after: 24 }));
    }
    const result = collector.result();
    assert.equal(result.segments.length, 1);
    assert.equal(result.discontinuities.length, 0);
    for (const frame of result.segments[0].frames) assert.equal(frame.ms, base + frame.sequence + 1001);
    assert.equal(result.segments[0].integrityErrors.length, 0);
  }
});

test("proven empty brackets still expose past samples without inventing current frames", () => {
  const collector = new FrameRingReconstructor();
  const sameFrame = { d0: 0, d1: 0, d2: 0, d3: 0, after: 0, value: () => 10 };
  collector.push(snapshot(300, sameFrame));
  collector.push(snapshot(450, sameFrame));
  const segment = collector.result().segments[0];
  assert.equal(segment.endSequence, 150);
  const analysis = analyzeSegment(segment);
  assert.equal(analysis.coverage, 1);
  assert.equal(analysis.passesP95, true);
});

test("unproven wraps start separate segments instead of guessing missing epochs", () => {
  const result = analyzeSnapshots([snapshot(300), snapshot(450), snapshot(900), snapshot(1040)]);
  assert.equal(result.segments.length, 2);
  assert.match(result.discontinuities[0].reason, /wrap count/);
  assert.equal(result.segments[1].startSequence, 0);
  assert.equal(result.passesP95, false);
});

test("outer bounds are required even when the gap between f1 and next f0 is below300", () => {
  const collector = new FrameRingReconstructor();
  collector.push(snapshot(300, { d1: 80, d2: 120, d3: 140, after: 150 }));
  collector.push(snapshot(580, { d1: 10, d2: 20, d3: 30, after: 40 }));
  assert.equal(collector.result().segments.length, 2); // 620-300 >=300
});

test("bad bracket shapes, within-bracket wraps and impossible advances fail closed", () => {
  for (const change of [
    { page0: Array(199).fill(10) },
    { n0: -1 }, { n1: 300 }, { n0: 1.5 }, { f0: -1 }, { f1: NaN },
    { f1: 600 }, { f1: 299 }, { n1: 100 }, { wallMs: NaN },
  ]) {
    const result = analyzeSnapshots([{ ...snapshot(300), ...change }]);
    assert.equal(result.invalidSnapshots.length, 1);
    assert.equal(result.passesP95, false);
  }
});

test("engine restart and impossible between-snapshot advances cannot join epochs", () => {
  for (const next of [snapshot(299), { ...snapshot(450), n0: 295, n1: 298 }]) {
    const collector = new FrameRingReconstructor();
    collector.push(snapshot(300));
    collector.push(next);
    assert.equal(collector.result().segments.length, 2);
    assert.equal(collector.result().discontinuities.length, 1);
  }
});

test("differing duplicate samples are quarantined and block otherwise passing data", () => {
  const collector = new FrameRingReconstructor();
  const opts = { value: () => 10 };
  collector.push(snapshot(300, opts));
  collector.push(snapshot(400, opts));
  const corrupted = snapshot(410, opts);
  corrupted.page0[10] = 11; // stable global write311, segment sequence9
  collector.push(corrupted);
  collector.push(snapshot(420, opts));
  const segment = collector.result().segments[0];
  assert.equal(segment.integrityErrors.length, 1);
  assert.equal(segment.frames.some((frame) => frame.sequence === 9), false);
  assert.equal(analyzeSegment(segment).passesP95, false);
});

test("invalid values remain missing, are not coerced to fast frames, and can recover", () => {
  const collector = new FrameRingReconstructor();
  const opts = { value: () => 10 };
  collector.push(snapshot(300, opts));
  const corrupted = snapshot(400, opts);
  corrupted.page0[20] = NaN;
  corrupted.page0[21] = Infinity;
  corrupted.page0[22] = -1;
  corrupted.page0[23] = "10";
  const mark = collector.push(corrupted);
  assert.equal(mark.invalidValues, 4);
  collector.push(snapshot(500, opts));
  assert.equal(analyzeSegment(collector.result().segments[0], { endSequence: mark.afterSequence }).missingFrames, 0);
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

test("exported results are detached and JSON-safe", () => {
  const collector = new FrameRingReconstructor();
  collector.push(snapshot(300));
  collector.push(snapshot(420));
  const first = collector.result();
  first.segments[0].frames[0].ms = -1;
  first.segments[0].integrityErrors.push({ sequence: 0 });
  const second = collector.result();
  assert.notEqual(second.segments[0].frames[0].ms, -1);
  assert.equal(second.segments[0].integrityErrors.length, 0);
  assert.deepEqual(JSON.parse(JSON.stringify(second)), second);
});
