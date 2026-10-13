import fs from 'node:fs';
import { FrameRingReconstructor, analyzeSegment } from './ring-analysis.mjs';
import { runIntegrity } from './run-integrity.mjs';
const events = fs.readFileSync(process.argv[2], 'utf8').trim().split('\n').map(JSON.parse);
const ring = new FrameRingReconstructor();
const marks = [];
for (const event of events) if (event.kind === 'ring') marks.push({ ...ring.push(event), phase: event.phase, wallMs: event.wallMs });
const result = ring.result();
const combat = marks.filter(m => m.phase === 'combat' && m.accepted);
const first = combat[0], last = combat.at(-1);
let stats = null;
if (first && last && first.segmentId === last.segmentId) stats = analyzeSegment(result.segments[first.segmentId], { startSequence: first.afterSequence, endSequence: last.beforeSequence });
const players = events.filter(e => e.kind === 'player' && e.wallMs >= first?.wallMs && e.wallMs <= last?.wallMs);
let totalSeconds = 0, aliveSeconds = 0, distance = 0, walkingDistance = 0;
const cap = Number(process.argv[3] || 56);
for (let i=1;i<players.length;i++) {
  const dt=(players[i].wallMs-players[i-1].wallMs)/1000;
  totalSeconds+=dt;
  if (players[i-1].result.bots.alive >= Math.ceil(cap*.9)) aliveSeconds+=dt;
  const a=players[i-1].result.position,b=players[i].result.position;
  const delta = Math.hypot(a.x-b.x,a.y-b.y,a.z-b.z);
  distance+=delta;
  if (!events.some(e=>e.kind==='action' && e.tool==='teleport' && e.wallMs>=players[i-1].wallMs && e.wallMs<=players[i].wallMs)) walkingDistance+=delta;
}
const final = events.findLast(e=>e.kind==='final-player')?.result || players.at(-1)?.result;
const initialRecord = events.find(e=>e.kind==='initial-status');
const initialStatus = initialRecord?.result;
const finalStatus = events.findLast(e=>e.kind==='final-status')?.result;
const complete = !!finalStatus;
const captureErrors = events.filter(e=>e.kind==='capture-error' || e.kind==='collector-error');
const activityStart = events.find(e=>e.kind==='phase' && e.phase==='combat')?.wallMs;
const activityEnd = events.find(e=>e.kind==='phase' && e.phase==='drain')?.wallMs;
const durationOverride = process.argv[5] ? Number(process.argv[5]) : undefined;
if (durationOverride !== undefined && initialRecord?.duration !== undefined && durationOverride !== initialRecord.duration) throw new Error('Duration override conflicts with recorded run request');
const integrity = runIntegrity({ requestedSeconds: durationOverride ?? initialRecord?.duration, activityStart, activityEnd,
  firstSample: first?.wallMs, lastSample: last?.wallMs, initialStatus, finalStatus, captureErrors: captureErrors.length });
const qualifiedPass = complete && integrity.passed && result.segments.length === 1 && !result.discontinuities.length && !result.invalidSnapshots.length && !!stats?.passesP95;
const minuteWindows = [];
if (stats) for (let t=first.wallMs;t+59000<=last.wallMs;t+=60000) {
  const a=combat.find(m=>m.wallMs>=t), b=combat.findLast(m=>m.wallMs<=t+60000);
  const window=analyzeSegment(result.segments[first.segmentId],{startSequence:a.afterSequence,endSequence:b.beforeSequence});
  minuteWindows.push({startSeconds:(a.wallMs-first.wallMs)/1000,p95FrameMs:window.p95FrameMs,p5FpsBoundary:window.p5FpsBoundary,coverage:window.coverage});
}
const summary = {
  input: process.argv[2], cap, complete, integrity, qualifiedPass,
  combatSeconds: first && last ? (last.wallMs-first.wallMs)/1000 : 0,
  stats, discontinuities: result.discontinuities, invalidSnapshots: result.invalidSnapshots,
  capturedSegments: result.segments.length,
  actualAliveMin: players.length ? Math.min(...players.map(p=>p.result.bots.alive)) : null,
  actualAliveMax: players.length ? Math.max(...players.map(p=>p.result.bots.alive)) : null,
  nearCapSeconds: aliveSeconds, populationObservedSeconds: totalSeconds,
  kills: final?.arena?.kills, positionTravelIncludingTeleports: distance, walkingDistanceLowerBound: walkingDistance,
  shotCount: events.filter(e=>e.kind==='action' && e.args?.action==='fire').reduce((n,e)=>n+(e.result.shotCount||0),0), minuteWindows,
  shots: events.filter(e=>e.kind==='action' && e.args?.action==='fire').map(e=>e.result),
  teleports: events.filter(e=>e.kind==='action' && e.tool==='teleport').map(e=>e.result),
  errors: events.filter(e=>e.kind==='capture-error' || e.kind==='action-error'),
  finalStatus: events.findLast(e=>e.kind==='final-status')?.result,
};
if (process.argv[4]) fs.writeFileSync(process.argv[4], JSON.stringify(summary, null, 2));
console.log(JSON.stringify({ ...summary, shots: summary.shots.length, teleports: summary.teleports.length, errors: summary.errors.length, finalStatus: undefined }, null, 2));
