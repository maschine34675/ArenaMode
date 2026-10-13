import fs from 'node:fs';
import { connect } from './bridge.mjs';

// Keeps godmode on across the raid-start body swap, then records a short
// contact window. Does not walk, shoot, or change settings.
const outPath = process.argv[2] || 'hold.json';
const stay = process.argv.includes('--stay');
const anchor = { x: 61.5, y: -2.6, z: 6.4 };
const pocket = p => p && ((p.x > 8 && p.x < 16 && p.z > 50 && p.z < 62) || p.z < -60);
const bridge = await connect('zombie-cap56');
const rows = [];
const note = (kind, data) => {
  const row = { kind, wallMs: Date.now(), ...data };
  rows.push(row);
  console.log(JSON.stringify(row));
};
const dist = player => {
  const list = player?.bots?.nearest || player?.nearest || [];
  const first = Array.isArray(list) ? list[0] : null;
  return first?.distance ?? first?.meters ?? null;
};
try {
  for (let i = 0; i < 6; i++) {
    const hooked = await bridge.act('hook', { action: 'godmode', enabled: true, force: true });
    note('godmode', { i, ok: hooked != null });
    await bridge.call('wait', { seconds: 1 });
    const status = await bridge.call('status', { plugins: 'none', harmony: 'none' });
    if (!status.raid?.inRaid) {
      note('ended', { screen: status.screen, alive: status.raid?.playerAlive });
      break;
    }
  }
  let player = await bridge.call('player');
  note('player', { position: player.position, botsAlive: player.bots?.alive, nearest: dist(player) });
  if (!stay && (pocket(player.position) || (dist(player) ?? 99) > 12)) {
    const moved = await bridge.act('teleport', { ...anchor, reachable: false, force: true });
    note('anchor', { ok: moved != null });
    await bridge.call('wait', { seconds: 8 });
    for (let i = 0; i < 3; i++) await bridge.act('hook', { action: 'godmode', enabled: true, force: true });
  }
  for (let sample = 0; sample < 4; sample++) {
    await bridge.act('hook', { action: 'godmode', enabled: true, force: true });
    const status = await bridge.call('status', { plugins: 'none', harmony: 'none' });
    player = await bridge.call('player');
    const bots = await bridge.call('bots', { aliveOnly: true, max: 120 });
    const distances = (bots.bots || []).map(bot => bot.distance).filter(n => typeof n === 'number');
    note('sample', {
      fps: status.performance,
      alive: player.bots?.alive ?? bots.bots?.length,
      nearest: dist(player),
      within3: distances.filter(d => d <= 3).length,
      within15: distances.filter(d => d <= 15).length,
      farthest: distances.length ? Math.max(...distances) : null,
    });
    if (!status.raid?.inRaid) break;
    if (sample < 3) await bridge.call('wait', { seconds: 8 });
  }
} finally {
  fs.writeFileSync(outPath, JSON.stringify(rows, null, 2));
  await bridge.close();
}
