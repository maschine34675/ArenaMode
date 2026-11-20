import fs from 'node:fs';
import { createHash } from 'node:crypto';
import { connect, assertSoloZombie } from './bridge.mjs';
import { openRing, RingPoller } from './ring-poll.mjs';

// Operates an already-started solo Arena Zombie raid. Start/leave only via gpu-run recipes.
//   node run.mjs [harness|dauerlast] out.jsonl seconds(30..900) [route.json] [--profiler] [--path-budget]
// harness   (default, the older invocation without a mode word): 30 s warmup, then walking along a route of spawn places,
//           native head-directed gunfire, a relocation every 55 s.
// dauerlast: the player stands where the route's anchor (or the Arena spawn) put him and fires at the nearest visible infected.
//           15 s warmup. This is the quiet protocol of the 2026-11-19 ceiling measurement.
// --profiler     ModProfiler headless capture around the run (30 s exports); needs com.maschine.ModProfiler.
// --path-budget  after a dauerlast, DeveloperMode for ~13 s to read the "Zombie planning jobs" log line.
// Ported to the UiBridge 0.10.1 frame ring on 2026-11-19 from the operator's stand-in; NOT yet run against the game.
const args = process.argv.slice(2).filter((a) => !a.startsWith('--'));
const flags = new Set(process.argv.slice(2).filter((a) => a.startsWith('--')));
const mode = ['harness', 'dauerlast'].includes(args[0]) ? args.shift() : 'harness';
const [out, durationArg, routeFile] = args;
const duration = Number(durationArg || 300);
if (!out || !(duration >= 30 && duration <= 900)) throw new Error('run.mjs [harness|dauerlast] output.jsonl seconds(30..900) [route.json] [--profiler] [--path-budget]');
// 0.35 s, not the 0.7 s of the operator's stand-in (bench.mjs), on purpose: the poll period is poll duration plus this wait, and
// the run's coverage gate allows 1.5 s between the last combat poll's START and the end of the phase (run-summary.mjs bounds the
// interval by the poll start). With 0.7 s the period is about 1.1 s, too close to that limit for a run to qualify reliably.
// The ring itself would not overflow at either value (4096 frames last 17 s at 240 fps). The price: about twice the inspector
// calls on the main thread inside the measured frames, so figures from this collector are not directly comparable with the
// ceiling report's (stand-in) figures; compare cells of one collector only.
const POLL_WAIT_SECONDS = 0.35;
const bridge = await connect();
const log = (kind, data) => fs.appendFileSync(out, JSON.stringify({ kind, wallMs: Date.now(), ...data }) + '\n');
let collecting = true, collector, phase = 'setup', scopeAccepted = false, profilerOn = false, ring = null;
const action = async (tool, args2) => {
  try { const result = await bridge.act(tool, args2); log('action', { tool, args: args2, result }); return result; }
  catch (error) { log('action-error', { tool, args: args2, error: error.message }); return null; }
};
const MODPROFILER = 'com.maschine.ModProfiler';
const setProfiler = (on) => action('config_set', { plugin: MODPROFILER, persist: false, settings: [
  { section: 'CSV', key: 'AutoExportIntervalSeconds', value: 30 }, { section: 'Capture', key: 'Headless', value: on }] });
try {
  const status = await bridge.call('status', { plugins: 'none', harmony: 'none' });
  await assertSoloZombie(bridge, status);
  scopeAccepted = true;
  log('initial-status', { result: status, mode, duration, runnerSha256: createHash('sha256').update(fs.readFileSync(new URL(import.meta.url))).digest('hex') });
  await action('hook', { action: 'godmode', enabled: true });
  await action('hook', { action: 'infiniteAmmo', enabled: true, keepCondition: true });
  let initialPlayer = await bridge.call('player');
  for (let retry = 0; retry < 15 && !initialPlayer.hands?.weapon; retry++) {
    await bridge.call('wait', { seconds: 1 });
    initialPlayer = await bridge.call('player');
  }
  if (!initialPlayer.hands?.weapon || !initialPlayer.hands.infiniteAmmo) throw new Error('Loaded covered firearm required');
  log('initial-player', { result: initialPlayer });
  const places = await bridge.call('places', { kind: 'spawn', max: 300 });
  log('places', { result: places });
  const candidates = places.places.filter((p) => !p.details?.snipeZone && p.y > -1 && p.y < 2);
  // Small maps (Air Pit) list fewer than three spawn places 24 m apart, some none at all: relax the separation step by step.
  // The harness needs a route (at least 3 places); Dauerlast only an anchor and otherwise stands where the Arena spawned the player.
  let route = [];
  for (const separation of [24, 12, 6, 0]) {
    route = [];
    for (const p of candidates) {
      if (route.every((q) => Math.hypot(p.x - q.x, p.z - q.z) > separation)) route.push(p);
      if (route.length === 6) break;
    }
    if (route.length >= 4 || (mode === 'dauerlast' && route.length >= 1 && separation <= 6)) break;
  }
  log('route-separation', { count: route.length });
  if (routeFile) {
    route = JSON.parse(fs.readFileSync(routeFile, 'utf8'));
    if (route.some((p) => !places.places.some((q) => q.id === p.id))) throw new Error('Saved route does not match current map');
  }
  if (route.length < (mode === 'harness' ? 3 : 0)) throw new Error('Too few separated route places');
  log('route', { route });
  const anchor = Math.min(Number(process.env.DAUER_ANCHOR || 0), route.length - 1);
  if (route.length) {
    // Start each comparison at the same native spawn, before collecting warmup.
    if (!await action('teleport', { place: route[anchor].id })) throw new Error('Benchmark start anchor unavailable');
    log('anchor', { index: anchor, place: route[anchor] });
  } else log('anchor', { index: null, note: 'the map lists no spawn places; the player stands where the Arena spawned him', position: initialPlayer.position });

  ring = await openRing(bridge.call);
  const poller = new RingPoller({ call: bridge.call, ring });
  // Every poll is one record; the reader (ring-analysis.mjs) proves what it can. A failed poll is logged and the next one
  // covers its interval, because the poller only advances its cursor on success: the DATA are recovered. The run is not a
  // qualified pass any more, though: runIntegrity requires captureErrors === 0, and the record of the failed poll is missing.
  // The stand-in's bots-all (unfiltered listing every 20 s) and final-population records are not ported: corpses on the
  // ground are not measurable (uibridge_bots aliveOnly=false lists registered bodies only), the `bots` record below gives the
  // alive count the analysis uses.
  collector = (async () => {
    while (collecting) {
      try { log('ring', { phase, ...await poller.poll() }); }
      catch (error) { log('capture-error', { error: error.message }); }
      if (collecting) await bridge.call('wait', { seconds: POLL_WAIT_SECONDS });
    }
  })();
  if (mode === 'dauerlast' && flags.has('--profiler')) {
    phase = 'profiler-scan';
    log('phase', { phase });
    if (!await setProfiler(true)) throw new Error('ModProfiler headless could not be switched on');
    profilerOn = true;
    log('profiler-running', { result: await bridge.call('wait', { logContains: 'headless capture running', logSource: 'maschine-ModProfiler', sinceSeq: 'action', timeoutSeconds: 50 }) });
  }
  phase = 'warmup';
  log('phase', { phase });
  await bridge.call('wait', { seconds: mode === 'dauerlast' ? 15 : 30 });
  phase = 'combat';
  log('phase', { phase });
  const started = Date.now();
  let lastPlayer = 0, lastBots = 0, lastMove = 0, lastTeleport = 0, lastProgress = 0, routeIndex = 0;
  while (Date.now() - started < duration * 1000) {
    const elapsed = (Date.now() - started) / 1000;
    // A new wave kit can replace ActiveHealthController. UiBridge otherwise keeps guarding the old
    // controller; reapply its public test hook each loop.
    if (!await action('hook', { action: 'godmode', enabled: true })) throw new Error('Godmode refresh failed');
    if (mode === 'harness') {
      if (elapsed - lastTeleport >= 55) {
        routeIndex = (routeIndex + 1) % route.length;
        await action('walk', { stop: true });
        await action('teleport', { place: route[routeIndex].id });
        lastTeleport = elapsed;
      }
      if (elapsed - lastMove >= 18 || lastMove === 0) {
        await action('walk', { place: route[(routeIndex + 1) % route.length].id, seconds: 17, sprint: false });
        lastMove = elapsed || .01;
      }
    }
    if (elapsed - lastPlayer >= 3 || lastPlayer === 0) {
      const player = await bridge.call('player');
      log('player', { elapsed, result: player });
      if (!player.alive || !player.arena?.active) throw new Error('Benchmark raid ended unexpectedly');
      if (elapsed - lastProgress >= 30) {
        console.log(JSON.stringify({ elapsed: Math.round(elapsed), alive: player.bots.alive, kills: player.arena.kills, position: player.position }));
        lastProgress = elapsed;
      }
      lastPlayer = elapsed || .01;
    }
    if (elapsed - lastBots >= 20 || lastBots === 0) {
      log('bots', { elapsed, result: await bridge.call('bots', { max: 200 }) });
      lastBots = elapsed || .01;
    }
    const aimed = await action('look', { at: 'nearestVisible', track: true, part: 'head', seconds: 3 });
    if (aimed) await action('hook', { action: 'fire', holdMs: 500 });
    await bridge.call('wait', { seconds: 1.3 });
  }
  log('phase', { phase: 'drain' });
  phase = 'drain';
  await action('walk', { stop: true });
  await action('look', { stop: true });
  if (profilerOn) { await setProfiler(false); profilerOn = false; }
  await bridge.call('wait', { seconds: 1.5 });
  collecting = false;
  await collector;
  if (mode === 'dauerlast' && flags.has('--path-budget')) {
    // After the measured windows: the developer counters of the path budget (DeveloperMode on for ~13 s, not persisted).
    const ARENA = 'com.maschine.ArenaMode';
    await action('config_set', { plugin: ARENA, persist: false, section: 'Diagnostics', key: 'DeveloperMode', value: true });
    await bridge.call('wait', { seconds: 13 });
    log('path-budget-log', { result: await bridge.call('log', { contains: 'Zombie planning jobs', max: 4, maxLineLength: 400 }) });
    await action('config_set', { plugin: ARENA, persist: false, section: 'Diagnostics', key: 'DeveloperMode', value: false });
  }
  log('final-player', { result: await bridge.call('player') });
  const finalStatus = await bridge.call('status', { plugins: 'none', harmony: 'none' });
  log('final-status', { result: finalStatus });
  if (finalStatus.bridge.instanceId !== status.bridge.instanceId) throw new Error('Process identity changed during measurement');
  console.log('Benchmark completed: ' + out);
} finally {
  collecting = false;
  if (collector) await collector.catch((error) => log('collector-error', { error: error.message }));
  if (profilerOn) await setProfiler(false).catch(() => {});
  if (scopeAccepted) {
    await action('walk', { stop: true });
    await action('look', { stop: true });
  }
  // Keep godmode until protected leave; it and infiniteAmmo end with that raid.
  for (const handle of ring?.handles ?? []) await bridge.call('inspect', { action: 'release', handle }).catch(() => {});
  await bridge.close();
}
