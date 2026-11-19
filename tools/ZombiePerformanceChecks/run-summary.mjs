import { FrameRingReader, analyzeSegment, windowsByFrameTime, describeFrames } from './ring-analysis.mjs';
import { runIntegrity } from './run-integrity.mjs';

// The arithmetic of analyze.mjs on an array of run events (JSONL lines), kept apart from the file I/O so the tests run it.
// The analyzed interval is the interior of the combat phase: the frames the first combat-labelled poll delivered are
// dropped (they began before the phase did), and the interval ends with the last combat-labelled poll.
export function summarizeRun(events, { cap = 56, durationOverride, input = null } = {}) {
  const reader = new FrameRingReader();
  const marks = [];
  for (const event of events) {
    if (event.kind === 'ring') marks.push({ ...reader.push(event), phase: event.phase, wallMs: event.wallMs, wallMs0: event.wallMs0 });
  }
  const result = reader.result();
  const combat = marks.filter((m) => m.phase === 'combat' && m.accepted);
  const first = combat[0], last = combat.at(-1);
  let stats = null, values = [], interval = null;
  if (first && last && first.segmentId === last.segmentId) {
    const segment = result.segments[first.segmentId];
    interval = { startSequence: first.afterSequence, endSequence: last.afterSequence };
    stats = analyzeSegment(segment, interval);
    values = segment.frames.filter((f) => f.sequence >= interval.startSequence && f.sequence < interval.endSequence).map((f) => f.ms);
  }
  const players = events.filter((e) => e.kind === 'player' && e.wallMs >= first?.wallMs && e.wallMs <= last?.wallMs);
  let totalSeconds = 0, aliveSeconds = 0, distance = 0, walkingDistance = 0;
  for (let i = 1; i < players.length; i++) {
    const dt = (players[i].wallMs - players[i - 1].wallMs) / 1000;
    totalSeconds += dt;
    if (players[i - 1].result.bots.alive >= Math.ceil(cap * .9)) aliveSeconds += dt;
    const a = players[i - 1].result.position, b = players[i].result.position;
    const delta = Math.hypot(a.x - b.x, a.y - b.y, a.z - b.z);
    distance += delta;
    if (!events.some((e) => e.kind === 'action' && e.tool === 'teleport' && e.wallMs >= players[i - 1].wallMs && e.wallMs <= players[i].wallMs)) walkingDistance += delta;
  }
  const final = events.findLast((e) => e.kind === 'final-player')?.result || players.at(-1)?.result;
  const initialRecord = events.find((e) => e.kind === 'initial-status');
  const initialStatus = initialRecord?.result;
  const finalStatus = events.findLast((e) => e.kind === 'final-status')?.result;
  const complete = !!finalStatus;
  const captureErrors = events.filter((e) => e.kind === 'capture-error' || e.kind === 'collector-error');
  const activityStart = events.find((e) => e.kind === 'phase' && e.phase === 'combat')?.wallMs;
  const activityEnd = events.find((e) => e.kind === 'phase' && e.phase === 'drain')?.wallMs;
  if (durationOverride !== undefined && initialRecord?.duration !== undefined && durationOverride !== initialRecord.duration) throw new Error('Duration override conflicts with recorded run request');
  const frameSeconds = values.length ? values.reduce((s, v) => s + v, 0) / 1000 : undefined;
  const wallSeconds = first && last ? (last.wallMs0 - first.wallMs0) / 1000 : undefined;
  const integrity = runIntegrity({ requestedSeconds: durationOverride ?? initialRecord?.duration, activityStart, activityEnd,
    firstSample: first?.wallMs, lastSample: last?.wallMs, initialStatus, finalStatus, captureErrors: captureErrors.length,
    frameSeconds, wallSeconds, frameCountMismatches: stats ? stats.integrityErrors.length : undefined });
  const qualifiedPass = complete && integrity.passed && result.segments.length === 1 && !result.discontinuities.length
    && !result.invalidRecords.length && !!stats?.passesP95;
  const fires = events.filter((e) => e.kind === 'action' && e.args?.action === 'fire');
  const alive = players.map((p) => p.result.bots.alive);
  let accumulated = 0;
  const spikes = [];
  for (const ms of values) { accumulated += ms; if (ms > 100) spikes.push({ atSecond: Number((accumulated / 1000).toFixed(1)), ms: Math.round(ms) }); }
  return {
    input, cap, mode: initialRecord?.mode ?? 'harness', complete, integrity, qualifiedPass,
    combatSeconds: first && last ? (last.wallMs0 - first.wallMs0) / 1000 : 0,
    stats, windows30s: windowsByFrameTime(values, 30000), overall: describeFrames(values),
    spikesOver100ms: spikes, entriesOver2s: values.filter((v) => v >= 2000).map((v) => Math.round(v)),
    discontinuities: result.discontinuities, invalidRecords: result.invalidRecords, capturedSegments: result.segments.length,
    actualAliveMin: alive.length ? Math.min(...alive) : null, actualAliveMax: alive.length ? Math.max(...alive) : null,
    actualAliveMean: alive.length ? Number((alive.reduce((s, a) => s + a, 0) / alive.length).toFixed(1)) : null, aliveSamples: alive.length,
    nearCapSeconds: aliveSeconds, populationObservedSeconds: totalSeconds,
    kills: final?.arena?.kills, deaths: final?.arena?.deaths,
    weapon: events.find((e) => e.kind === 'initial-player')?.result?.hands?.weapon ?? null,
    positionTravelIncludingTeleports: distance, walkingDistanceLowerBound: walkingDistance,
    shotCount: fires.reduce((n, e) => n + (e.result?.shots?.length ?? e.result?.shotCount ?? 0), 0), fireCalls: fires.length,
    shots: fires.map((e) => e.result),
    teleports: events.filter((e) => e.kind === 'action' && e.tool === 'teleport').map((e) => e.result),
    errors: events.filter((e) => e.kind === 'capture-error' || e.kind === 'action-error'),
    finalStatus,
  };
}
