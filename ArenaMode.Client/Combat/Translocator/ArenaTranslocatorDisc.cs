using System;
using System.Numerics;

namespace ArenaMode.Combat.Translocator;

/// <summary>
/// One bounded ballistic body. Every displacement, including impact separation,
/// needs a world sweep and an allowed-area segment; a small end point alone is
/// not evidence that a disc did not cross a thin wall or a concave area boundary.
/// </summary>
internal sealed class ArenaTranslocatorDisc
{
    internal const float Radius = .085f;
    internal const float Skin = .006f;
    internal const float LaunchSpeed = 18f;
    internal const float MaxStep = 1f / 120f;
    internal const int MaxSteps = 12;
    internal enum Flight { Flying, Resting, Lost }
    internal enum TraceKind { Clear, Hit, Invalid }
    internal readonly struct Trace
    {
        internal readonly TraceKind Kind;
        internal readonly Vector3 Position, Normal;
        internal Trace(TraceKind kind, Vector3 position, Vector3 normal = default)
        { Kind = kind; Position = position; Normal = normal; }
    }

    internal Vector3 Position { get; private set; }
    internal Vector3 Velocity { get; private set; }
    internal Vector3 SurfaceNormal { get; private set; } = Vector3.UnitY;
    internal Flight State { get; private set; }
    internal float FlightSeconds { get; private set; }

    internal ArenaTranslocatorDisc(Vector3 position, Vector3 velocity)
    {
        Position = position; Velocity = velocity;
        State = Finite(position) && Finite(velocity) ? Flight.Flying : Flight.Lost;
    }

    internal static bool Finite(Vector3 p)
        => Good(p.X) && Good(p.Y) && Good(p.Z);
    private static bool Good(float f) => !float.IsNaN(f) && !float.IsInfinity(f);

    internal void Lose() { State = Flight.Lost; Velocity = Vector3.Zero; }
    internal void ReleaseSupport() { if (State == Flight.Resting) State = Flight.Flying; }

    internal void Advance(float seconds, Func<Vector3, Vector3, Trace> sweep,
        Func<Vector3, Vector3, bool> inside)
    {
        if (State != Flight.Flying || !(seconds > 0f) || !Good(seconds)) return;
        if (sweep == null || inside == null) { Lose(); return; }
        // A stalled renderer cannot create an unbounded catch-up physics burst.
        float remaining = Math.Min(seconds, MaxStep * MaxSteps);
        for (int step = 0; step < MaxSteps && remaining > 0f && State == Flight.Flying; step++)
        {
            float dt = Math.Min(remaining, MaxStep); remaining -= dt;
            FlightSeconds += dt;
            // No persistent projectile falling forever below a whole-map scene.
            if (FlightSeconds > 30f) { Lose(); break; }
            Velocity += new Vector3(0f, -9.81f * dt, 0f);
            var end = Position + Velocity * dt;
            if (!Finite(end) || !inside(Position, end)) { Lose(); break; }
            Trace trace;
            try { trace = sweep(Position, end); }
            catch { Lose(); throw; }
            if ((trace.Kind != TraceKind.Clear && trace.Kind != TraceKind.Hit) || !Finite(trace.Position)
                || !inside(Position, trace.Position)) { Lose(); break; }
            if (trace.Kind == TraceKind.Clear)
            {
                // The host may never substitute a farther point for the sweep.
                if (Vector3.DistanceSquared(trace.Position, end) > .000001f) { Lose(); break; }
                Position = end;
                continue;
            }
            if (!Finite(trace.Normal) || trace.Normal.LengthSquared() < .5f
                || Vector3.DistanceSquared(Position, trace.Position) > Vector3.DistanceSquared(Position, end) + .01f)
            { Lose(); break; }
            var normal = Vector3.Normalize(trace.Normal);
            // Host reports the sphere center at first contact, not the surface
            // hit point. Move only the tiny separation away from that contact.
            var separated = trace.Position + normal * Skin;
            if (!inside(trace.Position, separated)) { Lose(); break; }
            // Sweep backwards from the separated center: the contact center
            // may touch its supporting collider, so a forward empty-start
            // test would reject legitimate bounces. Only contact at the end
            // of this reverse segment is allowed; a neighbouring wall is not.
            var separation = sweep(separated, trace.Position);
            if ((separation.Kind != TraceKind.Clear && separation.Kind != TraceKind.Hit)
                || !Finite(separation.Position)
                || Vector3.DistanceSquared(separation.Position, trace.Position) > .000001f)
            { Lose(); break; }
            Position = separated;
            float incoming = Vector3.Dot(Velocity, normal);
            if (incoming < 0f) Velocity -= 1.28f * incoming * normal;
            Velocity *= .68f;
            SurfaceNormal = normal;
            if (normal.Y >= .65f && Velocity.LengthSquared() <= 1.8f * 1.8f)
            { State = Flight.Resting; Velocity = Vector3.Zero; }
            // Discard the rest of this tiny step at a collision. Subsequent
            // displacement always obtains a fresh sweep; no tunnelling fallback.
        }
    }
}
