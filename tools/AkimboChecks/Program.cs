using ArenaMode.Combat.Akimbo;

int checks = 0;
void Need(bool value, string reason) { checks++; if (!value) throw new Exception(reason); }
var ids = Enumerable.Range(0, 6).Select(_ => new object()).ToArray();
AkimboLease New(float now = 10f) => new(ids[0], ids[1], ids[2], ids[3], ids[4], ids[5], 7, 2, now);
bool Current(AkimboLease lease, object[] identities = null, int epoch = 7, int round = 2, float now = 11f, bool live = true)
{
    var v = identities ?? ids;
    return lease.Current(v[0], v[1], v[2], v[3], v[4], v[5], epoch, round, now, live);
}
var state = New();
Need(Current(state), "live exact lease admitted");
for (int i = 0; i < ids.Length; i++)
{
    var changed = ids.ToArray(); changed[i] = new object();
    Need(!Current(state, changed), "replacement identity rejected " + i);
    changed[i] = null;
    Need(!Current(state, changed), "missing identity rejected " + i);
}
Need(!Current(state, live: false), "ordinary raid or invalid live scope rejected");
Need(!Current(state, epoch: 8), "new raid rejected");
Need(!Current(state, round: 3), "new round rejected");
Need(Current(state, now: 10), "activation instant admitted");
Need(!Current(state, now: 9), "clock rollback rejected");
Need(Current(state, now: 29.99f), "pre-expiry still usable");
Need(!Current(state, now: 30), "deadline exclusive");
foreach (float time in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity, -1f, float.MaxValue })
{
    Need(!Current(New(time), now: time), "invalid birth rejected");
    Need(!Current(state, now: time), "invalid or expired sample rejected");
}
for (int mask = 0; mask < 32; mask++)
{
    var lease = New();
    Need(lease.BeginShot((mask & 1) != 0, (mask & 2) != 0, (mask & 4) != 0,
        (mask & 8) != 0, (mask & 16) != 0) == (mask == 31), "all shot guards required " + mask);
}
Need(state.BeginShot(true, true, true, true, true), "first bonus admitted");
Need(!state.BeginShot(true, true, true, true, true), "nested bonus rejected");
state.EndShot();
Need(state.BeginShot(true, true, true, true, true), "next distinct native shot admitted");
state.Retire(); state.EndShot();
Need(!Current(state) && !state.BeginShot(true, true, true, true, true), "retirement cannot be revived by shot finalizer");
Console.WriteLine($"AKIMBO_CHECKS_PASSED ({checks} assertions)");
