import fs from 'node:fs/promises';
import { connect, assertSoloZombie } from './bridge.mjs';
const bridge = await connect();
try {
  const status = await bridge.call('status', { plugins: 'none', harmony: 'none' });
  await assertSoloZombie(bridge, status);
  console.log('godmode', JSON.stringify(await bridge.act('hook', { action: 'godmode', enabled: true })));
  console.log('ammo', JSON.stringify(await bridge.act('hook', { action: 'infiniteAmmo', enabled: true, keepCondition: true })));
  const results = {};
  for (const [name, args] of [['player', {}], ['places', { kind: 'spawn', max: 150 }], ['bots', { max: 100 }]]) results[name] = await bridge.call(name, args);
  await fs.writeFile(process.argv[2], JSON.stringify(results, null, 2));
  console.log(JSON.stringify(results.player));
} finally { await bridge.close(); }
