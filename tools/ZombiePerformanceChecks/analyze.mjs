import fs from 'node:fs';
import { summarizeRun } from './run-summary.mjs';

// usage: node analyze.mjs run.jsonl [cap] [summary.json] [requestedSeconds]
// The arithmetic lives in run-summary.mjs (tested offline); this file only reads and writes.
const events = fs.readFileSync(process.argv[2], 'utf8').split(/\r?\n/).filter(Boolean).map((line) => JSON.parse(line));
const summary = summarizeRun(events, {
  input: process.argv[2], cap: Number(process.argv[3] || 56), durationOverride: process.argv[5] ? Number(process.argv[5]) : undefined,
});
if (process.argv[4]) fs.writeFileSync(process.argv[4], JSON.stringify(summary, null, 2));
console.log(JSON.stringify({
  ...summary, shots: summary.shots.length, teleports: summary.teleports.length, errors: summary.errors.length, finalStatus: undefined,
  windows30s: summary.windows30s.map((w) => `${w.fps} fps, mean ${w.meanMs}, p95 ${w.p95Ms}, max ${w.maxMs}`),
}, null, 2));
