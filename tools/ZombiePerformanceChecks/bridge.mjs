import fs from 'node:fs/promises';
import path from 'node:path';
import { pathToFileURL } from 'node:url';

// Reuse gpu-run's configured MCP proxy and SDK; never store credentials in evidence.
export async function connect(lane = 'zombiebench-20261013') {
  const base = process.env.UIBRIDGE_GPU_RUN || path.resolve('../UiBridge/tools/gpu-run');
  const mod = name => import(pathToFileURL(path.join(base, 'lib', name)).href);
  const { resolveUiBridgeConfig, ownLabelFor } = await mod('config.mjs');
  const { importSdk } = await mod('sdkImport.mjs');
  const { withOwnLaneLease } = await mod('lease.mjs');
  const configuration = JSON.parse(await fs.readFile(path.join(process.env.USERPROFILE, '.claude.json'), 'utf8'));
  const config = resolveUiBridgeConfig(configuration, 'uibridge-2', lane);
  const { Client } = await importSdk('client/index.js');
  const { StdioClientTransport } = await importSdk('client/stdio.js');
  const client = new Client({ name: 'arena-zombie-benchmark', version: '1.0.0' }, { capabilities: {} });
  try { await client.connect(new StdioClientTransport({ ...config, stderr: 'ignore' })); }
  catch { await client.close().catch(()=>{}); throw new Error('UiBridge MCP connection failed; inspect the configured proxy locally. Connection arguments are withheld.'); }
  const raw = async (name, args = {}) => {
    const result = await client.callTool({ name: `uibridge_${name}`, arguments: args });
    if (result.isError) throw new Error(result.content?.filter(c => c.type === 'text').map(c => c.text).join('\n') || 'UiBridge error');
    if (!result.structuredContent) throw new Error(`Missing structured result: ${name}`);
    return result.structuredContent;
  };
  return { call: raw, act: (name, args) => withOwnLaneLease(a => raw(name, a), args, { ownLabel: ownLabelFor(lane) }), close: () => client.close() };
}

export async function assertSoloZombie(bridge, status) {
  if (!status.raid?.inRaid || !status.raid.playerAlive || status.session?.nickname !== 'SPT-GPU-2') throw new Error('Live SPT-GPU-2 test-profile raid required');
  const field = async (name, member) => {
    const handle = (await bridge.call('inspect', { action: 'type', name })).value.handle;
    try { return (await bridge.call('inspect', { action: 'read', handle, member })).value.value; }
    finally { await bridge.call('inspect', { action: 'release', handle }); }
  };
  if (await field('ArenaMode.Session.ArenaSession', 'ActiveMode') !== 'zombie'
    || await field('ArenaMode.Compat.PitFireTeamCompat', 'HasFrozenTeamRoster') !== false) throw new Error('Active solo Arena Zombie mode required');
}

if (process.argv[1] && import.meta.url === pathToFileURL(path.resolve(process.argv[1])).href) {
  const bridge = await connect();
  try {
    const status = await bridge.call('status', { plugins: 'none', harmony: 'none' });
    const input = JSON.parse(await fs.readFile(process.argv[2], 'utf8'));
    const results = [{ tool: 'status', result: status }];
    const refs = {};
    for (const step of input) {
      if (!['status', 'inspect', 'player', 'places', 'bots', 'log', 'config', 'inventory'].includes(step.tool)) throw new Error('Probe CLI accepts read-only tools only');
      const args = structuredClone(step.args || {});
      if (step.handleRef) args.handle = refs[step.handleRef];
      const result = await bridge.call(step.tool, args);
      if (step.ref) refs[step.ref] = result.handle?.id || result.handle || result.value?.handle;
      results.push({ ...step, result });
    }
    for (const handle of new Set(Object.values(refs).filter(Boolean))) await bridge.call('inspect', { action: 'release', handle }).catch(()=>{});
    await fs.writeFile(process.argv[3], JSON.stringify(results, null, 2));
    console.log(JSON.stringify(results));
  } finally { await bridge.close(); }
}
