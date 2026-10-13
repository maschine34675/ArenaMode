// Reconstruct UiBridge's 300-frame float ring without invoking or changing game code.
// Each collection page is copied on Unity's main thread, but the two pages need not
// describe the same frame. Cursor/engine-frame brackets prove which slots stayed intact.
//
// API:
//   const ring = new FrameRingReconstructor();
//   const mark = ring.push({ f0, n0, page0, page1, n1, f1, wallMs });
//   const { segments, discontinuities, invalidSnapshots } = ring.result();
//   analyzeSegment(segments[0], { endSequence: mark.afterSequence, targetFps: 60 });
// Read order: frameCount, _next, slots 0..199, slots 200..299, _next, frameCount.
// n0/n1 are the next WRITE slot; f0/f1 are Time.frameCount. wallMs is metadata.
// A segment's sequence 0 is the first frame written AFTER its first n0 read.
// Pre-existing ring data is excluded. Keep sampling briefly after an end marker
// to recover touched slots, then analyze with that marker's endSequence.

export const FRAME_RING_CAPACITY = 300;
const PAGE_SPLIT = 200;
const modulo = (value) => ((value % FRAME_RING_CAPACITY) + FRAME_RING_CAPACITY) % FRAME_RING_CAPACITY;
const integer = (value) => Number.isSafeInteger(value) && value >= 0;
const frameMs = (value) => typeof value === "number" && Number.isFinite(value) && value >= 0;

function validateSnapshot(snapshot) {
  if (!snapshot || typeof snapshot !== "object") return "snapshot must be an object";
  if (!integer(snapshot.f0) || !integer(snapshot.f1)) return "engine frame counters must be nonnegative safe integers";
  if (!integer(snapshot.n0) || !integer(snapshot.n1)
      || snapshot.n0 >= FRAME_RING_CAPACITY || snapshot.n1 >= FRAME_RING_CAPACITY) {
    return "ring cursors must be integers from 0 to 299";
  }
  if (!Number.isFinite(snapshot.wallMs)) return "wallMs must be finite";
  if (!Array.isArray(snapshot.page0) || snapshot.page0.length !== PAGE_SPLIT
      || !Array.isArray(snapshot.page1) || snapshot.page1.length !== FRAME_RING_CAPACITY - PAGE_SPLIT) {
    return "collection pages must contain exactly 200 and 100 values";
  }
  if (snapshot.f1 < snapshot.f0) return "engine counter reversed within snapshot";
  if (snapshot.f1 - snapshot.f0 >= FRAME_RING_CAPACITY) return "snapshot can contain a complete ring wrap";
  if (modulo(snapshot.n1 - snapshot.n0) > snapshot.f1 - snapshot.f0) {
    return "cursor advancement exceeds the snapshot engine-frame bracket";
  }
  return null;
}

/** Stateful, deterministic collector. No I/O, polling, timers or GPU operations. */
export class FrameRingReconstructor {
  constructor() {
    this._segments = [];
    this._discontinuities = [];
    this._invalidSnapshots = [];
    this._previous = null;
    this._snapshotCount = 0;
  }

  /**
   * Add one ordered bracket. Invalid brackets break continuity; no wrap count is guessed.
   * A duplicate sequence with a different value is quarantined and fails that segment's
   * integrity gate. This exposes torn reads/restarts instead of accepting whichever won.
   */
  push(snapshot) {
    const snapshotIndex = this._snapshotCount++;
    const invalidReason = validateSnapshot(snapshot);
    if (invalidReason) {
      const event = { snapshotIndex, reason: invalidReason, wallMs: snapshot?.wallMs ?? null };
      this._invalidSnapshots.push(event);
      this._discontinuities.push(event);
      this._previous = null;
      return { accepted: false, ...event };
    }

    const ownAdvance = modulo(snapshot.n1 - snapshot.n0);
    let reason = null;
    let beforeSequence = 0;
    if (this._previous) {
      const previous = this._previous;
      // Outer bounds also cover the unobserved interval between the two n reads.
      const outerSpan = snapshot.f1 - previous.f0;
      const gapAdvance = modulo(snapshot.n0 - previous.n1);
      if (snapshot.f0 < previous.f1) reason = "engine brackets overlap or counter restarted";
      else if (outerSpan >= FRAME_RING_CAPACITY) reason = "between-snapshot wrap count is unproven";
      else if (gapAdvance + ownAdvance > outerSpan) reason = "cursor advancement exceeds adjacent engine-frame brackets";
      else if (snapshot.wallMs < previous.wallMs) reason = "snapshot wall clock reversed";
      else beforeSequence = previous.afterSequence + gapAdvance;
    }

    if (reason) {
      this._discontinuities.push({ snapshotIndex, reason, wallMs: snapshot.wallMs });
      this._previous = null;
    }
    if (!this._previous) {
      this._segments.push({
        id: this._segments.length,
        startSequence: 0,
        endSequence: 0,
        firstWallMs: snapshot.wallMs,
        lastWallMs: snapshot.wallMs,
        initialEngineFrame: snapshot.f0,
        finalEngineFrame: snapshot.f1,
        snapshots: 0,
        touchedSlotsDiscarded: 0,
        invalidValuesDiscarded: 0,
        duplicateObservations: 0,
        integrityErrors: [],
        frames: new Map(),
        conflicted: new Set(),
      });
    }
    const segment = this._segments.at(-1);
    const afterSequence = beforeSequence + ownAdvance;
    if (!Number.isSafeInteger(afterSequence)) throw new RangeError("frame sequence exceeded the safe integer range");
    const pages = [...snapshot.page0, ...snapshot.page1];
    let added = 0;
    let touched = 0;
    let invalidValues = 0;
    let duplicates = 0;
    for (let slot = 0; slot < FRAME_RING_CAPACITY; slot++) {
      // [n0,n1) may have been overwritten before, between or after the page reads.
      // Even when that slot looks plausible its epoch is ambiguous, so discard it.
      if (modulo(slot - snapshot.n0) < ownAdvance) {
        touched++;
        continue;
      }
      const sequence = beforeSequence - 1 - modulo(snapshot.n0 - 1 - slot);
      if (sequence < segment.startSequence || segment.conflicted.has(sequence)) continue;
      const ms = pages[slot];
      if (!frameMs(ms)) {
        invalidValues++;
        continue;
      }
      const old = segment.frames.get(sequence);
      if (old) {
        duplicates++;
        if (old.ms !== ms) {
          segment.frames.delete(sequence);
          segment.conflicted.add(sequence);
          segment.integrityErrors.push({ sequence, firstMs: old.ms, secondMs: ms, snapshotIndex });
        }
      } else {
        segment.frames.set(sequence, { sequence, ms, wallMs: snapshot.wallMs });
        added++;
      }
    }
    segment.endSequence = afterSequence;
    segment.lastWallMs = snapshot.wallMs;
    segment.finalEngineFrame = snapshot.f1;
    segment.snapshots++;
    segment.touchedSlotsDiscarded += touched;
    segment.invalidValuesDiscarded += invalidValues;
    segment.duplicateObservations += duplicates;
    this._previous = { ...snapshot, page0: undefined, page1: undefined, afterSequence };
    return {
      accepted: true,
      snapshotIndex,
      segmentId: segment.id,
      beforeSequence,
      afterSequence,
      added,
      touched,
      invalidValues,
      duplicates,
      discontinuity: reason,
    };
  }

  /** A detached, JSON-serializable result; callers cannot mutate collector state. */
  result() {
    return {
      capacity: FRAME_RING_CAPACITY,
      snapshotCount: this._snapshotCount,
      segments: this._segments.map(({ frames, conflicted, integrityErrors, ...metadata }) => ({
        ...metadata,
        integrityErrors: integrityErrors.map((error) => ({ ...error })),
        frames: [...frames.values()].sort((a, b) => a.sequence - b.sequence).map((frame) => ({ ...frame })),
      })),
      discontinuities: this._discontinuities.map((event) => ({ ...event })),
      invalidSnapshots: this._invalidSnapshots.map((event) => ({ ...event })),
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

/** Full-record convenience. Multiple segments cannot certify the unobserved gaps. */
export function analyzeSnapshots(snapshots, options = {}) {
  const collector = new FrameRingReconstructor();
  for (const snapshot of snapshots) collector.push(snapshot);
  const result = collector.result();
  const analyses = result.segments.map((segment) => analyzeSegment(segment, options));
  return {
    ...result,
    analyses,
    passesP95: result.segments.length === 1 && result.discontinuities.length === 0
      && result.invalidSnapshots.length === 0 && analyses[0].passesP95,
  };
}
