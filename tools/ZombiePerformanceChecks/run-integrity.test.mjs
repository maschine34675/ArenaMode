import test from 'node:test';
import assert from 'node:assert/strict';
import { runIntegrity } from './run-integrity.mjs';
const status = { bridge: { instanceId: 'same' }, raid: { location: 'factory' } };
const valid = { requestedSeconds: 240, activityStart: 1000, activityEnd: 241000, firstSample: 1400, lastSample: 240600, initialStatus: status, finalStatus: status };
test('complete declared workload passes; an early short segment never substitutes for it', () => {
  assert.equal(runIntegrity(valid).passed, true);
  assert.equal(runIntegrity({ ...valid, activityEnd: 4500, lastSample: 4400 }).passed, false);
});
test('missing or invalid metadata, changed process/map, late capture and capture errors fail closed', () => {
  for (const change of [{ requestedSeconds: undefined }, { requestedSeconds: Infinity }, { requestedSeconds: 0 },
    { activityEnd: undefined }, { activityStart: 5000 }, { lastSample: 242000 }, { lastSample: 230000 },
    { finalStatus: undefined }, { finalStatus: { ...status, bridge: { instanceId: 'other' } } },
    { finalStatus: { ...status, raid: { location: 'labs' } } }, { captureErrors: 1 }]) {
    assert.equal(runIntegrity({ ...valid, ...change }).passed, false, JSON.stringify(change));
  }
});
