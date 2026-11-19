import { FRAME_RING_CAPACITY, ringWriteSlot, planPageReads } from './ring-analysis.mjs';

// The collector side of the UiBridge frame ring (see ring-analysis.mjs for what the ring is). It takes the bridge's
// `call` function, so tests run it against a simulated FrameStats and the real run against the live plugin.
//
// One poll = cursor read, slot reads, cursor read:
//   1. ONE multi-read (main thread, atomic): FrameStats._head, FrameStats._count, UnityEngine.Time.frameCount.
//   2. The slots from the previous poll's write cursor up to this one's, in pages of at most 200.
//   3. The cursors again, so the reader can prove the slots were not overwritten while they were copied.
// The record carries the raw numbers; ring-analysis.mjs decides what they prove.

export const MIN_PLUGIN_NOTE = 'UiBridge plugin with FrameStats._ms/_head/_count (4096 slots, source history 2026-10-23, plugin 0.9.4 line; verified against 0.10.1)';

/** Reads the three ring handles. A plugin with the older 300-slot ring (_samples/_next) fails here with the version note. */
export async function openRing(call) {
  const open = async (args) => (await call('inspect', args)).value.handle;
  const plugin = await open({ action: 'root', name: 'plugin:com.maschine.UiBridge' });
  const read = async (handle, member) => (await call('inspect', { action: 'read', handle, member })).value;
  let stats, ms;
  try {
    stats = (await read(plugin, '_frameStats')).handle;
    ms = (await read(stats, '_ms')).handle;
    await read(stats, '_head');
    await read(stats, '_count');
  } catch (error) {
    throw new Error(`The frame ring layout is not the one this collector reads (${error.message}). Required: ${MIN_PLUGIN_NOTE}.`);
  }
  return { plugin, stats, ms, handles: [plugin, stats, ms] };
}

export class RingPoller {
  /**
   * @param call   bridge.call(tool, args) returning the structured result
   * @param ring   the result of openRing
   * @param now    wall clock in ms (injectable for tests)
   */
  constructor({ call, ring, now = Date.now, capacity = FRAME_RING_CAPACITY }) {
    this._call = call;
    this._ring = ring;
    this._now = now;
    this._capacity = capacity;
    this._last = null; // { w, f } of the last successful poll
  }

  async _cursor() {
    const r = await this._call('inspect', { action: 'read', reads: [
      { handle: this._ring.stats, member: '_head' },
      { handle: this._ring.stats, member: '_count' },
      { type: 'UnityEngine.Time', member: 'frameCount' },
    ] });
    const [h, c, f] = r.values.map((entry) => entry.value.value);
    return { h, c, f };
  }

  /** One poll record (kind 'ring' without the kind/phase wrapper). Throws on a bridge error; the cursor is then not advanced. */
  async poll() {
    const wallMs0 = this._now();
    const a = await this._cursor();
    const w0 = ringWriteSlot(a.h, a.c);
    const record = { wallMs0, f0: a.f, h0: a.h, c0: a.c, from: this._last ? this._last.w : null, n: 0, vals: [] };
    if (this._last) {
      record.n = ((w0 - this._last.w) % this._capacity + this._capacity) % this._capacity;
      if (a.f - this._last.f >= this._capacity) record.skipped = true; // the advance is ambiguous: do not read, the reader starts a segment
      else {
        for (const { offset, limit } of planPageReads(this._last.w, record.n, this._capacity)) {
          const page = await this._call('inspect', { action: 'collection', handle: this._ring.ms, offset, limit });
          for (const item of page.items) record.vals.push(item.value);
        }
      }
    }
    const b = await this._cursor();
    Object.assign(record, { f1: b.f, h1: b.h, c1: b.c, wallMs: this._now() });
    this._last = { w: w0, f: a.f };
    return record;
  }
}
