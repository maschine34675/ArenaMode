// Reconstruct UiBridge's frame-time ring (plugin 0.9.4 and later, read on 0.10.1) without invoking or changing game code.
//
// WHAT THE RING IS (UiBridge.Client/Core/FrameStats.cs, read 2026-11-19):
//   `_ms` is a float[4096] of frame durations (Time.unscaledDeltaTime * 1000), `_at` the stopwatch time of each.
//   One slot is ONE frame, written once per Unity frame from Plugin.Update on the main thread.
//   `_head` is the index of the OLDEST kept frame and `_count` how many are kept; the next write goes to
//   (_head + _count) % 4096. Frames also fall out by AGE: Evict() moves `_head` forward (and `_count` down) for every frame
//   older than 30 s, and when the ring is full the oldest is overwritten the same way.
//   So `_head` is NOT a write cursor. It advances as old frames are evicted, i.e. with the frame rate of ~30 s earlier,
//   and it stands still while no old frame is due (the first poll after a hitch 30 s ago). The write cursor is
//   w = (_head + _count) % 4096: every Add moves head+count by exactly one (an eviction moves head up and count down),
//   so w advances by exactly one slot per recorded frame whatever the age eviction does.
//   A collector that treats `_head` as the cursor (the 2026-11-19 stand-in did) reads the frames that were evicted since
//   its last poll: complete, but about 30 s old, and a 8 s stall shows up as a head that stands still for 8 s followed by
//   one 8000 ms entry. See README "What the ring is".
//
// A poll record (written by ring-poll.mjs, kind 'ring'):
//   { phase, wallMs0, wallMs, f0, h0, c0, from, n, vals, f1, h1, c1, skipped? }
//   f = Time.frameCount, h = _head, c = _count, read in ONE main-thread call before (0) and after (1) the slot reads.
//   from = the write cursor of the previous successful poll (null for the first one), n = (w0 - from) mod 4096, vals = the n
//   slots from..w0-1 in write order. h0/c0 give w0 = (h0 + c0) % 4096, h1/c1 give w1.
//
// What the reconstruction can prove:
//   * the slots read were not overwritten while reading: n + (w1 - w0) <= 4096, otherwise the poll is rejected
//   * the cursor advance between two polls equals the Time.frameCount advance (FRAME_COUNT_TOLERANCE, 0): a poll whose
//     advance differs breaks the claim "one slot per frame", is recorded as an integrity error and blocks a pass
//   * no wrap is guessed: two polls 4096 or more engine frames apart cannot be joined (a new segment starts)
// What it cannot prove: that every frame is a "real" one (a hitch is a frame like any other, see README).

export const FRAME_RING_CAPACITY = 4096;
export const PAGE_SIZE = 200;
/**
 * No tolerance, argued from the UiBridge source (Plugin.Update, read 2026-11-19): `_frameStats.Add` runs first in Update and
 * `_dispatcher.Drain` last, in the same call on the main thread, and the inspector multi-read executes inside Drain. The
 * cursor (`_head`, `_count`) and `Time.frameCount` of one read therefore belong to the same frame, and between two polls the
 * cursor advance must equal the engine counter advance exactly. A frame added "just before or after the read" cannot happen:
 * that earlier reasoning was wrong. A tolerance of one frame would also let a recording error of one frame per poll (about
 * 2 percent of a 0.7 s poll at 85 fps) through both this gate and the 3 percent wall-clock gate. The only way the source lets
 * a frame go unrecorded is `Add` dropping a NaN, negative or infinite duration, which does not occur in a running game; if a
 * smoke run shows deltas other than 0, read the poll records and argue the cause, do not widen this.
 */
export const FRAME_COUNT_TOLERANCE = 0;

const modulo = (value, m = FRAME_RING_CAPACITY) => ((value % m) + m) % m;
const integer = (value) => Number.isSafeInteger(value) && value >= 0;
const frameMs = (value) => typeof value === "number" && Number.isFinite(value) && value >= 0;

/** The next write slot, from the two fields the inspector reads. */
export function ringWriteSlot(head, count) {
  return modulo(head + count);
}

/** The page reads (offset, limit) that cover `n` slots from slot `from`, in write order, never crossing the array end. */
export function planPageReads(from, n, capacity = FRAME_RING_CAPACITY, pageSize = PAGE_SIZE) {
  const reads = [];
  let offset = modulo(from, capacity), left = n;
  while (left > 0) {
    const limit = Math.min(pageSize, left, capacity - offset);
    reads.push({ offset, limit });
    offset = (offset + limit) % capacity;
    left -= limit;
  }
  return reads;
}

function validateRecord(record) {
  if (!record || typeof record !== "object") return "record must be an object";
  for (const key of ["f0", "f1", "h0", "h1", "c0", "c1", "n"]) if (!integer(record[key])) return `${key} must be a nonnegative safe integer`;
  if (record.h0 >= FRAME_RING_CAPACITY || record.h1 >= FRAME_RING_CAPACITY) return "ring head must be an integer from 0 to 4095";
  if (record.c0 > FRAME_RING_CAPACITY || record.c1 > FRAME_RING_CAPACITY) return "ring count must be an integer from 0 to 4096";
  if (!Number.isFinite(record.wallMs0) || !Number.isFinite(record.wallMs)) return "wall clock must be finite";
  if (record.from !== null && !(integer(record.from) && record.from < FRAME_RING_CAPACITY)) return "from must be null or a slot index";
  if (!Array.isArray(record.vals)) return "vals must be an array";
  if (record.f1 < record.f0) return "engine counter reversed within the record";
  if (record.f1 - record.f0 >= FRAME_RING_CAPACITY) return "the record spans a complete ring wrap";
  return null;
}

/** Stateful, deterministic reconstruction. No I/O, polling, timers or GPU operations. */
export class FrameRingReader {
  constructor() {
    this._segments = [];
    this._discontinuities = [];
    this._invalidRecords = [];
    this._previous = null;
    this._count = 0;
  }

  /** Add one poll record in order. Anything unprovable breaks continuity; no wrap count is guessed. */
  push(record) {
    const index = this._count++;
    const invalid = validateRecord(record);
    if (invalid) {
      const event = { index, reason: invalid, wallMs: record?.wallMs ?? null };
      this._invalidRecords.push(event);
      this._discontinuities.push(event);
      this._previous = null;
      return { accepted: false, ...event };
    }
    const w0 = ringWriteSlot(record.h0, record.c0), w1 = ringWriteSlot(record.h1, record.c1);
    const k = modulo(w1 - w0);
    let reason = null, beforeSequence = 0, expectedN = 0;
    if (this._previous) {
      const previous = this._previous;
      expectedN = modulo(w0 - previous.w0);
      if (record.skipped) reason = "the poll skipped its slot reads";
      else if (record.f0 < previous.f0) reason = "engine counter restarted";
      else if (record.f0 - previous.f0 >= FRAME_RING_CAPACITY) reason = "between-poll wrap count is unproven";
      else if (record.wallMs0 < previous.wallMs0) reason = "wall clock reversed";
      else if (record.from !== previous.w0) reason = "a poll is missing between these two records";
      else if (record.n !== expectedN || record.vals.length !== expectedN) reason = "the record's slot count does not follow from the cursors";
      else if (record.n + k > FRAME_RING_CAPACITY) reason = "slots may have been overwritten while they were read";
      else beforeSequence = previous.afterSequence;
    }
    if (reason) {
      this._discontinuities.push({ index, reason, wallMs: record.wallMs });
      this._previous = null;
    }
    const first = !this._previous;
    if (first) {
      this._segments.push({
        id: this._segments.length, startSequence: 0, endSequence: 0, firstWallMs: record.wallMs, lastWallMs: record.wallMs,
        polls: 0, invalidValuesDiscarded: 0, frameCountMismatches: 0, integrityErrors: [], frames: new Map(),
      });
    }
    const segment = this._segments.at(-1);
    let added = 0, invalidValues = 0, frameCountDelta = 0;
    if (!first) {
      // The reader's claim "one slot per frame" is checked against the engine's own counter on every poll.
      frameCountDelta = record.n - (record.f0 - this._previous.f0);
      if (Math.abs(frameCountDelta) > FRAME_COUNT_TOLERANCE) {
        segment.frameCountMismatches++;
        segment.integrityErrors.push({ sequence: beforeSequence, kind: "frame-count", cursorAdvance: record.n, engineFrames: record.f0 - this._previous.f0 });
      }
      for (let i = 0; i < record.n; i++) {
        const ms = record.vals[i];
        if (!frameMs(ms)) { invalidValues++; continue; }
        segment.frames.set(beforeSequence + i, { sequence: beforeSequence + i, ms, wallMs: record.wallMs });
        added++;
      }
    }
    const afterSequence = beforeSequence + (first ? 0 : record.n);
    segment.endSequence = afterSequence;
    segment.lastWallMs = record.wallMs;
    segment.polls++;
    segment.invalidValuesDiscarded += invalidValues;
    this._previous = { w0, f0: record.f0, wallMs0: record.wallMs0, afterSequence };
    return { accepted: true, index, segmentId: segment.id, beforeSequence, afterSequence, added, invalidValues, frameCountDelta, discontinuity: reason };
  }

  /** A detached, JSON-serializable result; callers cannot mutate reader state. */
  result() {
    return {
      capacity: FRAME_RING_CAPACITY,
      recordCount: this._count,
      segments: this._segments.map(({ frames, integrityErrors, ...metadata }) => ({
        ...metadata,
        integrityErrors: integrityErrors.map((error) => ({ ...error })),
        frames: [...frames.values()].sort((a, b) => a.sequence - b.sequence).map((frame) => ({ ...frame })),
      })),
      discontinuities: this._discontinuities.map((event) => ({ ...event })),
      invalidRecords: this._invalidRecords.map((event) => ({ ...event })),
    };
  }
}

/** Nearest-rank percentile; p is 0..1, the empty distribution returns null. */
export function nearestRank(values, p) {
  if (!Array.isArray(values) || !values.every(frameMs)) throw new TypeError("values must be finite nonnegative numbers");
  if (!Number.isFinite(p) || p < 0 || p > 1) throw new RangeError("percentile must be between 0 and 1");
  if (values.length === 0) return null;
  const sorted = [...values].sort((a, b) => a - b);
  return sorted[Math.max(0, Math.ceil(p * sorted.length) - 1)];
}

/**
 * Analyze a HALF-OPEN sequence interval within one proven segment. Missing frames are
 * treated as infinitely slow for the conservative P95 gate. No-data never passes.
 * A finite conservativeP95FrameMs is an upper bound after that worst-case padding;
 * null + conservativeP95Unbounded means missing data may occupy the 95th percentile.
 */
export function analyzeSegment(segment, { startSequence = segment.startSequence, endSequence = segment.endSequence, targetFps = 60 } = {}) {
  if (!Number.isFinite(targetFps) || targetFps <= 0) throw new RangeError("targetFps must be positive and finite");
  if (!integer(startSequence) || !integer(endSequence) || startSequence > endSequence
      || startSequence < segment.startSequence || endSequence > segment.endSequence) {
    throw new RangeError("analysis interval must lie within the proven segment");
  }
  const selected = segment.frames.filter((frame) => frame.sequence >= startSequence && frame.sequence < endSequence);
  const seen = new Set();
  for (const frame of selected) {
    if (!integer(frame.sequence) || !frameMs(frame.ms) || seen.has(frame.sequence)) {
      throw new TypeError("segment contains invalid or duplicate frame records");
    }
    seen.add(frame.sequence);
  }
  const sorted = selected.map((frame) => frame.ms).sort((a, b) => a - b);
  const expectedFrames = endSequence - startSequence;
  const knownFrames = sorted.length;
  const missingFrames = expectedFrames - knownFrames;
  const thresholdMs = 1000 / targetFps;
  const rank = Math.ceil(expectedFrames * 0.95);
  const knownSlowFrames = sorted.filter((ms) => ms > thresholdMs).length;
  const conservativeSlowFrames = knownSlowFrames + missingFrames;
  const conservativeP95Unbounded = expectedFrames > 0 && rank > knownFrames;
  const conservativeP95FrameMs = rank > 0 && rank <= knownFrames ? sorted[rank - 1] : null;
  const totalKnownMs = sorted.reduce((sum, ms) => sum + ms, 0);
  const meanFrameMs = knownFrames ? totalKnownMs / knownFrames : null;
  const p95FrameMs = nearestRank(sorted, 0.95);
  const integrityErrors = (segment.integrityErrors ?? []).filter((error) => error.sequence >= startSequence && error.sequence < endSequence);
  return {
    segmentId: segment.id,
    startSequence,
    endSequence,
    targetFps,
    thresholdMs,
    expectedFrames,
    knownFrames,
    missingFrames,
    coverage: expectedFrames ? knownFrames / expectedFrames : null,
    totalKnownMs,
    meanFrameMs,
    meanFps: meanFrameMs > 0 ? 1000 / meanFrameMs : null,
    medianFrameMs: nearestRank(sorted, 0.5),
    p95FrameMs,
    p99FrameMs: nearestRank(sorted, 0.99),
    maxFrameMs: knownFrames ? sorted.at(-1) : null,
    p5FpsBoundary: p95FrameMs > 0 ? 1000 / p95FrameMs : null,
    knownSlowFrames,
    knownSlowFraction: knownFrames ? knownSlowFrames / knownFrames : null,
    knownOver33msFrames: sorted.filter((ms) => ms > 1000 / 30).length,
    conservativeSlowFrames,
    conservativeSlowFraction: expectedFrames ? conservativeSlowFrames / expectedFrames : null,
    conservativeP95FrameMs,
    conservativeP95Unbounded,
    integrityErrors: integrityErrors.map((error) => ({ ...error })),
    passesP95: expectedFrames > 0 && integrityErrors.length === 0
      && conservativeP95FrameMs !== null && conservativeP95FrameMs <= thresholdMs,
  };
}

/** Plain statistics of an array of frame times (ms): the per-window rows of a report. */
export function describeFrames(values) {
  if (!values.length) return null;
  const total = values.reduce((sum, ms) => sum + ms, 0);
  const round = (x, d = 2) => Number(x.toFixed(d));
  return {
    frames: values.length, seconds: round(total / 1000), fps: round(values.length / (total / 1000), 1), meanMs: round(total / values.length),
    p50Ms: round(nearestRank(values, 0.5)), p95Ms: round(nearestRank(values, 0.95)), p99Ms: round(nearestRank(values, 0.99)), maxMs: round(Math.max(...values), 1),
  };
}

/** Consecutive windows of `windowMs` of accumulated frame time (the last, shorter one is dropped). Never an average of percentiles. */
export function windowsByFrameTime(values, windowMs = 30000) {
  const windows = [];
  let current = [], accumulated = 0;
  for (const ms of values) {
    current.push(ms);
    accumulated += ms;
    if (accumulated >= windowMs) { windows.push(describeFrames(current)); current = []; accumulated = 0; }
  }
  return windows;
}

/** Full-record convenience. Multiple segments cannot certify the unobserved gaps. */
export function analyzeRecords(records, options = {}) {
  const reader = new FrameRingReader();
  for (const record of records) reader.push(record);
  const result = reader.result();
  const analyses = result.segments.map((segment) => analyzeSegment(segment, options));
  return {
    ...result,
    analyses,
    passesP95: result.segments.length === 1 && result.discontinuities.length === 0
      && result.invalidRecords.length === 0 && analyses[0].passesP95,
  };
}
