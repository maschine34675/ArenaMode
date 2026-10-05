using ArenaMode.Combat.Akimbo;

int checks = 0;
void Need(bool value, string reason) { checks++; if (!value) throw new Exception(reason); }
var ids = Enumerable.Range(0, 7).Select(_ => new object()).ToArray();
AkimboOwner Owner(object[] identities = null, int epoch = 7, int round = 2)
{
    var v = identities ?? ids;
    return new(v[0], v[1], v[2], v[3], v[4], v[5], v[6], epoch, round);
}
AkimboLease New() => new(Owner());
bool Current(AkimboLease lease, object[] identities = null, int epoch = 7, int round = 2, bool live = true)
    => lease.Current(Owner(identities, epoch, round), live);
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
Need(!new AkimboLease(default).Current(default, true), "invalid owner cannot become current");
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

var bindings = new AkimboBindings();
Need(!bindings.MayAttach(default) && bindings.Attach(default) == null, "incomplete owner cannot be attached");
Need(bindings.MayAttach(Owner()), "first eligible owner admitted");
var first = bindings.Attach(Owner());
Need(first != null && Current(first), "first binding owns the exact weapon");
for (int i = 0; i < 1000; i++)
    Need(!bindings.MayAttach(Owner()) && bindings.Attach(Owner()) == null && ReferenceEquals(bindings.Lease, first),
        "stable owner never builds a second binding");
bindings.Reject(Owner());
Need(!Current(first), "failure retires the old lease");
bindings.Release(); // Busy/context interruption must not remove the rejection.
for (int i = 0; i < 1000; i++)
    Need(!bindings.MayAttach(Owner()) && bindings.Attach(Owner()) == null, "failed owner is not retried each frame");
foreach (int index in Enumerable.Range(0, ids.Length))
{
    var replacement = ids.ToArray(); replacement[index] = new object();
    Need(bindings.MayAttach(Owner(replacement)), "fresh physical owner permits a new attempt " + index);
}
Need(bindings.MayAttach(Owner(epoch: 8)) && bindings.MayAttach(Owner(round: 3)), "new round or session permits a new attempt");
bindings.Clear(); // Explicit OFF releases both the view lifetime and its failure latch.
Need(bindings.MayAttach(Owner()), "explicit off-on permits retry");
var second = bindings.Attach(Owner());
Need(second.BeginShot(true, true, true, true, true), "new lease shot begins");
first.EndShot();
Need(!second.BeginShot(true, true, true, true, true), "old shot finalizer cannot release new shot guard");
bindings.Release(); second.EndShot();
Need(!Current(second) && bindings.MayAttach(Owner()), "weapon release retires old shots but permits a redraw");

Need(AkimboEligibility.Supports(true, "pistol", false, false), "handgun including automatic and revolver admitted");
Need(!AkimboEligibility.Supports(true, "pistol", true, false), "flare pistol excluded");
Need(!AkimboEligibility.Supports(true, "pistol", false, true), "launcher excluded");
Need(!AkimboEligibility.Supports(false, "pistol", false, false), "native handgun type required");
foreach (string weaponClass in new[] { "shotgun", "grenadeLauncher", "smg", "assaultRifle", "", null })
    Need(!AkimboEligibility.Supports(true, weaponClass, false, false), "non-handgun revolver category excluded: " + weaponClass);
Console.WriteLine($"AKIMBO_CHECKS_PASSED ({checks} assertions)");
