import test from "node:test";
import assert from "node:assert/strict";
import { summarizeRun } from "./run-summary.mjs";
import { RingPoller, openRing } from "./ring-poll.mjs";
import { SimGame, fakeBridge } from "./frame-stats-sim.mjs";

// A whole simulated Dauerlast (15 s warmup, 60 s combat unless told otherwise) as the run script would log it, summarized by the analyzer's own code.
async function simulatedRun({ durationAt = () => 10, combatSeconds = 60, warmupSeconds = 15 } = {}) {
  const game = new SimGame(durationAt);
  game.advance(2000);
  const bridge = fakeBridge(game, { callMs: 20 });
  const ring = await openRing(bridge.call);
  const poller = new RingPoller({ call: bridge.call, ring, now: () => game.time });
  const status = { bridge: { instanceId: 'process-1' }, raid: { location: 'factory' } };
  const events = [{ kind: 'initial-status', wallMs: game.time, result: status, mode: 'dauerlast', duration: combatSeconds },
    { kind: 'initial-player', wallMs: game.time, result: { hands: { weapon: 'AKS-74' } } }];
  let phase = 'warmup';
  events.push({ kind: 'phase', wallMs: game.time, phase });
  const combatStart = game.time + warmupSeconds * 1000, combatEnd = combatStart + combatSeconds * 1000;
  while (game.time < combatEnd + 1500) {
    events.push({ kind: 'ring', phase, ...await poller.poll() });
    game.advance(350);
    if (phase === 'warmup' && game.time >= combatStart) { phase = 'combat'; events.push({ kind: 'phase', wallMs: game.time, phase }); }
    if (phase === 'combat' && game.time >= combatEnd) { phase = 'drain'; events.push({ kind: 'phase', wallMs: game.time, phase }); }
    if (phase === 'combat') events.push({ kind: 'player', wallMs: game.time, elapsed: (game.time - combatStart) / 1000,
      result: { bots: { alive: 32 }, position: { x: 0, y: 0, z: 0 }, arena: { kills: 3 } } });
  }
  events.push({ kind: 'final-player', wallMs: game.time, result: { arena: { kills: 3, deaths: 0 } } });
  events.push({ kind: 'final-status', wallMs: game.time, result: status });
  return { events, game };
}

test("a clean simulated Dauerlast passes every gate and reports the combat interior only", async () => {
  // an 8 s hitch during the warmup must not reach the combat statistics
  const { events } = await simulatedRun({ durationAt: (t) => (t >= 6000 && t < 6010 ? 8000 : 10), combatSeconds: 70 });
  const summary = summarizeRun(events, { cap: 32 });
  assert.equal(summary.integrity.passed, true, JSON.stringify(summary.integrity));
  assert.equal(summary.qualifiedPass, true);
  assert.equal(summary.capturedSegments, 1);
  assert.equal(summary.stats.maxFrameMs, 10);
  assert.deepEqual(summary.entriesOver2s, []);
  assert.ok(summary.stats.knownFrames > 6800 && summary.stats.knownFrames < 7200, `${summary.stats.knownFrames}`);
  assert.equal(summary.windows30s.length, 2);
  assert.equal(summary.windows30s[0].p95Ms, 10);
  assert.equal(summary.weapon, 'AKS-74');
  assert.equal(summary.mode, 'dauerlast');
  assert.equal(summary.actualAliveMax, 32);
});

test("a slow run is reported with passing integrity and a failing p95", async () => {
  const { events } = await simulatedRun({ durationAt: (t) => (t % 100 < 50 ? 25 : 10) });
  const summary = summarizeRun(events);
  assert.equal(summary.integrity.passed, true);
  assert.equal(summary.stats.passesP95, false);
  assert.equal(summary.qualifiedPass, false);
  assert.equal(summary.stats.p95FrameMs, 25);
});

test("a lost poll splits the run and no pass is claimed", async () => {
  const { events } = await simulatedRun();
  const rings = events.map((e, i) => [e, i]).filter(([e]) => e.kind === 'ring' && e.phase === 'combat');
  events.splice(rings[40][1], 1);
  const summary = summarizeRun(events);
  assert.ok(summary.discontinuities.length >= 1);
  assert.equal(summary.qualifiedPass, false);
});

test("a poll whose cursor disagrees with the engine frame counter fails the frame-count gate", async () => {
  const { events } = await simulatedRun();
  const rings = events.filter((e) => e.kind === 'ring' && e.phase === 'combat');
  rings[30].f0 += 45; // the engine "ran" 45 frames the ring never recorded
  rings[30].f1 += 45;
  const summary = summarizeRun(events);
  assert.equal(summary.integrity.frameCountMatches, false);
  assert.equal(summary.integrity.passed, false);
  assert.equal(summary.qualifiedPass, false);
});

test("a ring that is not the newest frames fails the frame-time-against-wall gate", async () => {
  const { events } = await simulatedRun();
  // every combat poll delivers only half of its frames' durations: the sum no longer matches the wall time
  for (const e of events) if (e.kind === 'ring' && e.phase === 'combat') e.vals = e.vals.map((v) => v / 2);
  const summary = summarizeRun(events);
  assert.equal(summary.integrity.frameTimeMatchesWall, false);
  assert.equal(summary.qualifiedPass, false);
});

test("a duration override that contradicts the recorded request is rejected", async () => {
  const { events } = await simulatedRun();
  assert.throws(() => summarizeRun(events, { durationOverride: 300 }), /conflicts/);
});
