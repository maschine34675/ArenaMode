// Test support only: a faithful JavaScript port of UiBridge.Core.FrameStats (UiBridge.Client/Core/FrameStats.cs, read
// 2026-11-19) plus a fake bridge that answers the inspector calls the collector makes. The port keeps the parts the
// collector depends on: float32 slots, `_head` = oldest kept frame, `_count`, eviction by AGE (30 s) before every Add,
// overwrite of the oldest when 4096 are kept, and the write slot (_head + _count) % 4096.

export const CAPACITY = 4096;
export const RETENTION_MS = 30000;

export class SimFrameStats {
  constructor() {
    this.ms = new Float32Array(CAPACITY);
    this.at = new Float64Array(CAPACITY);
    this.head = 0;
    this.count = 0;
    this.adds = 0; // ground truth: frames ever added
  }
  evict(nowMs) {
    while (this.count > 0 && nowMs - this.at[this.head] > RETENTION_MS) {
      this.head = (this.head + 1) % CAPACITY;
      this.count--;
    }
  }
  add(ms, nowMs) {
    if (Number.isNaN(ms) || !Number.isFinite(ms) || ms < 0) return;
    this.evict(nowMs);
    if (this.count === CAPACITY) {
      this.head = (this.head + 1) % CAPACITY;
      this.count--;
    }
    const slot = (this.head + this.count) % CAPACITY;
    this.ms[slot] = ms;
    this.at[slot] = nowMs;
    this.count++;
    this.adds++;
  }
}

/**
 * A game that renders frames of scripted durations. `durationAt(t)` gives the frame time in ms at game time t (ms);
 * `truth` keeps every frame's float32 value in order, which is what a faithful collector must reproduce.
 */
export class SimGame {
  constructor(durationAt) {
    this.stats = new SimFrameStats();
    this.durationAt = durationAt;
    this.time = 0; // ms
    this.frameCount = 1000;
    this.truth = [];
  }
  /** Run frames until `ms` of game time has passed (a frame that straddles the end completes). */
  advance(ms) {
    const end = this.time + ms;
    while (this.time < end) {
      const d = this.durationAt(this.time);
      this.time += d;
      this.frameCount++;
      this.stats.add(d, this.time);
      if (Number.isFinite(d) && d >= 0) this.truth.push(Math.fround(d));
    }
  }
}

/**
 * The bridge as the collector sees it. Every call first lets the game run `callMs` of game time (a bridge round trip
 * costs real time), so polls interleave with frames exactly as in the live run. `hooks` may inject faults.
 */
export function fakeBridge(game, { callMs = 20, fail = () => false } = {}) {
  const handles = { plugin: 'h-plugin', stats: 'h-stats', ms: 'h-ms' };
  const member = (name) => {
    if (name === '_head') return game.stats.head;
    if (name === '_count') return game.stats.count;
    throw new Error(`no such member ${name}`);
  };
  let calls = 0;
  return {
    get calls() { return calls; },
    async call(tool, args) {
      calls++;
      if (fail(calls, tool, args)) throw new Error('simulated bridge failure');
      game.advance(callMs);
      if (tool !== 'inspect') throw new Error(`unexpected tool ${tool}`);
      if (args.action === 'root') return { value: { handle: handles.plugin } };
      if (args.action === 'collection') {
        if (args.handle !== handles.ms) throw new Error('collection of an unknown handle');
        if (args.limit > 200) throw new Error('page limit above 200');
        const items = [];
        for (let i = 0; i < args.limit; i++) items.push({ value: game.stats.ms[(args.offset + i) % CAPACITY] });
        return { items };
      }
      if (args.action === 'read' && args.reads) {
        return {
          frame: game.frameCount,
          values: args.reads.map((r) => ({
            value: { value: r.type === 'UnityEngine.Time' ? game.frameCount : member(r.member) },
          })),
        };
      }
      if (args.action === 'read') {
        if (args.member === '_frameStats') return { value: { handle: handles.stats } };
        if (args.member === '_ms') return { value: { handle: handles.ms } };
        return { value: { value: member(args.member) } };
      }
      throw new Error(`unexpected inspect action ${args.action}`);
    },
  };
}
