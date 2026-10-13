import fs from 'node:fs';
import { createHash } from 'node:crypto';
import { connect, assertSoloZombie } from './bridge.mjs';

// Operates an already-started solo Arena raid. Start/leave only via gpu-run recipes.
const out = process.argv[2];
const duration = Number(process.argv[3] || 300);
if (!out || !(duration >= 30 && duration <= 900)) throw new Error('run.mjs output.jsonl seconds(30..900)');
const bridge = await connect();
const log = (kind, data) => fs.appendFileSync(out, JSON.stringify({ kind, wallMs: Date.now(), ...data }) + '\n');
let collecting = true, collector, phase = 'setup', scopeAccepted = false;
const retainedHandles = [];
const read = async (handle, member) => (await bridge.call('inspect', { action: 'read', handle, member })).value.value;
const open = async args => (await bridge.call('inspect', args)).value.handle;
const action = async (tool, args) => {
  try { const result = await bridge.act(tool, args); log('action', { tool, args, result }); return result; }
  catch (error) { log('action-error', { tool, args, error: error.message }); return null; }
};
try {
  const status = await bridge.call('status', { plugins: 'none', harmony: 'none' });
  await assertSoloZombie(bridge, status);
  scopeAccepted = true;
  log('initial-status', { result: status, duration, runnerSha256: createHash('sha256').update(fs.readFileSync(new URL(import.meta.url))).digest('hex') });
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
  const candidates = places.places.filter(p => !p.details?.snipeZone && p.y > -1 && p.y < 2);
  let route = [];
  for (const p of candidates) {
    if (route.every(q => Math.hypot(p.x-q.x,p.z-q.z) > 24)) route.push(p);
    if (route.length === 6) break;
  }
  if (process.argv[4]) {
    route = JSON.parse(fs.readFileSync(process.argv[4], 'utf8'));
    if (route.some(p => !places.places.some(q => q.id === p.id))) throw new Error('Saved route does not match current map');
  }
  if (route.length < 3) throw new Error('Too few separated route places');
  log('route', { route });
  // Start each comparison at the same native spawn, before collecting warmup.
  const positioned = await action('teleport', { place: route[0].id });
  if (!positioned) throw new Error('Benchmark start anchor unavailable');
  const plugin = await open({ action: 'root', name: 'plugin:com.maschine.UiBridge' });
  const stats = (await bridge.call('inspect', { action: 'read', handle: plugin, member: '_frameStats' })).value.handle;
  const samples = (await bridge.call('inspect', { action: 'read', handle: stats, member: '_samples' })).value.handle;
  retainedHandles.push(plugin, stats, samples);
  // Status exposes the same Time.frameCount without a weak inspector Type wrapper.
  // The actual ring is strongly retained by the plugin throughout this process.
  collector = (async () => {
    while (collecting) {
      try {
        const f0 = (await bridge.call('status', { plugins: 'none', harmony: 'none' })).bridge.frame;
        const n0 = await read(stats, '_next');
        const p0 = await bridge.call('inspect', { action: 'collection', handle: samples, offset: 0, limit: 200 });
        const p1 = await bridge.call('inspect', { action: 'collection', handle: samples, offset: 200, limit: 100 });
        const n1 = await read(stats, '_next');
        const f1 = (await bridge.call('status', { plugins: 'none', harmony: 'none' })).bridge.frame;
        log('ring', { phase, f0, n0, page0: p0.items.map(x=>x.value), page1: p1.items.map(x=>x.value), n1, f1 });
      } catch (error) { log('capture-error', { error: error.message }); }
      if (collecting) await bridge.call('wait', { seconds: 0.35 });
    }
  })();
  phase = 'warmup';
  log('phase', { phase });
  await bridge.call('wait', { seconds: 30 });
  phase = 'combat';
  log('phase', { phase });
  const started = Date.now();
  let lastPlayer = 0, lastBots = 0, lastMove = 0, lastTeleport = 0, lastProgress = 0, routeIndex = 0;
  while (Date.now()-started < duration*1000) {
    const elapsed = (Date.now()-started)/1000;
    // A new wave kit can replace ActiveHealthController. UiBridge 0.9.1 otherwise
    // keeps guarding the old controller; reapply its public test hook each loop.
    if (!await action('hook', { action: 'godmode', enabled: true })) throw new Error('Godmode refresh failed');
    if (elapsed-lastTeleport >= 55) {
      routeIndex = (routeIndex+1)%route.length;
      const place = route[routeIndex].id;
      await action('walk', { stop: true });
      await action('teleport', { place });
      lastTeleport = elapsed;
    }
    if (elapsed-lastMove >= 18 || lastMove === 0) {
      await action('walk', { place: route[(routeIndex+1)%route.length].id, seconds: 17, sprint: false });
      lastMove = elapsed || .01;
    }
    if (elapsed-lastPlayer >= 3 || lastPlayer === 0) {
      const player = await bridge.call('player');
      log('player', { elapsed, result: player });
      if (!player.alive || !player.arena?.active) throw new Error('Benchmark raid ended unexpectedly');
      if (elapsed-lastProgress >= 30) {
        console.log(JSON.stringify({ elapsed: Math.round(elapsed), alive: player.bots.alive, kills: player.arena.kills, position: player.position }));
        lastProgress = elapsed;
      }
      lastPlayer = elapsed || .01;
    }
    if (elapsed-lastBots >= 20 || lastBots === 0) {
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
  await bridge.call('wait', { seconds: 1.2 });
  collecting = false;
  await collector;
  log('final-player', { result: await bridge.call('player') });
  const finalStatus = await bridge.call('status', { plugins: 'none', harmony: 'none' });
  log('final-status', { result: finalStatus });
  if (finalStatus.bridge.instanceId !== status.bridge.instanceId) throw new Error('Process identity changed during measurement');
  console.log('Benchmark completed: '+out);
} finally {
  collecting = false;
  if (collector) await collector.catch(error => log('collector-error', { error: error.message }));
  if (scopeAccepted) {
    await action('walk', { stop: true });
    await action('look', { stop: true });
  }
  // Keep godmode until protected leave; it and infiniteAmmo end with that raid.
  for (const handle of retainedHandles) await bridge.call('inspect', { action: 'release', handle }).catch(()=>{});
  await bridge.close();
}
