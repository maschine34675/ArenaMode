using EFT;
using UnityEngine;

namespace ArenaMode.Movement;

/// <summary>Local ground slide. All displacement still uses EFT's one native motion call.</summary>
public class ArenaSlideController : MonoBehaviour
{
    public static float MinSlideSpeed = 3.5f;
    public static float SlideDuration = 1.5f;
    public static float SlideFriction = 3f;
    public static float PoseThreshold = .85f;
    public static float SlideWindow = .3f;
    public static float FadeOutDuration = .2f;
    internal const float LandingInputWindow = .35f, LandingStateWindow = .5f;

    internal Player Player;
    internal MovementContext Context;
    private bool _isSliding, _wasSprinting, _returning;
    private Vector3 _slideVelocity, _sprintVelocity, _sampleVelocity, _returnVelocity;
    private Vector3 _landingVelocity;
    private float _slideStarted, _startSpeed, _startAnimSpeed, _sprintEnded = -1f;
    private float _sampleAt = -1f, _queuedAt = -1f, _contactAt = -1f, _preparedAt = -1f;
    private float _landingJumpAt = -1f;
    private float _landingJumpWindow;
    private bool _leftGround;
    private BaseMovementState _lastSprintState, _sprintExitState;
    private float _lastSprintAt = -1f, _sprintExitAt = -1f;

    internal bool IsFadingOut;
    internal float FadeStartTime, FadeLastSpeed;
    public bool IsSliding => _isSliding;
    public Vector3 SlideVelocity => _slideVelocity;
    public float NormalizedSlideAnimSpeed => !_isSliding || _startAnimSpeed < .001f
        ? -1f : _startAnimSpeed * _slideVelocity.magnitude / Mathf.Max(_startSpeed, .001f);

    public void Init(Player player, MovementContext movementContext)
    {
        Clear();
        Player = player;
        Context = movementContext;
        ArenaSlideTransitions.Bind(this);
    }

    private void Update() => Tick();
    private void OnDisable() => Clear();

    internal void Clear()
    {
        _isSliding = _wasSprinting = _returning = _leftGround = IsFadingOut = false;
        _slideVelocity = _sprintVelocity = _sampleVelocity = _returnVelocity = Vector3.zero;
        _landingVelocity = Vector3.zero;
        _sprintEnded = _sampleAt = _queuedAt = _contactAt = _preparedAt = _landingJumpAt = -1f;
        _lastSprintState = _sprintExitState = null;
        _lastSprintAt = _sprintExitAt = -1f;
        _landingJumpWindow = 0f;
    }

    internal void Tick()
    {
        if (!ArenaSlideTransitions.Owns(this))
        {
            Clear();
            enabled = false; // Corpses and replaced input owners must stop ticking too.
            return;
        }
        float now = Time.time;
        if (!ArenaSlideTransitions.Finite(now) || !ArenaSlideTransitions.ValidDelta(Time.deltaTime))
        { Clear(); return; }
        bool groundState = ArenaSlideTransitions.GroundState(Context.CurrentState);
        bool supported = ArenaSlideTransitions.Supported(Context);
        ObserveSprintExit();
        bool sprintExit = IsSprintExit(Context.CurrentState);
        bool slideState = IsSlideState(Context.CurrentState);
        bool contact = supported && Player.CharacterController.isGrounded;
        bool airborne = ArenaSlideTransitions.AirState(Context.CurrentState);
        if (airborne && !contact) _leftGround = true;

        if (_queuedAt >= 0f)
        {
            // Sprint.Enter restores native standing pose at landing. The queued
            // crouch is explicit input intent; only a subsequent native pose
            // command cancels it, not that automatic state-entry pose reset.
            if ((!groundState && !airborne && Context.CurrentState is not JumpLandingState)
                || now < _queuedAt || (_contactAt < 0f && now - _queuedAt > LandingInputWindow)
                || (_contactAt >= 0f && (!supported || now - _contactAt > LandingStateWindow)))
                CancelLanding();
            else
            {
                if (_contactAt < 0f && contact && Descending()) ConfirmContact(now);
                if (_contactAt >= 0f && slideState && supported)
                {
                    Vector3 velocity = _landingVelocity;
                    CancelLanding();
                    if (FreshSample() && velocity.magnitude >= MinSlideSpeed) StartSlide(velocity);
                }
            }
        }

        bool sprinting = Player.IsSprintEnabled && groundState && supported;
        if (sprinting && FreshSample())
        {
            _sprintVelocity = _sampleVelocity;
            _lastSprintState = Context.CurrentState;
            _lastSprintAt = now;
            _sprintExitState = null;
            _sprintExitAt = -1f;
        }
        if (_wasSprinting && !sprinting && (groundState || sprintExit) && supported) _sprintEnded = now;
        if (!_isSliding && (groundState || sprintExit) && supported && _sprintEnded >= 0f
            && now - _sprintEnded <= SlideWindow && Context.PoseLevel < PoseThreshold
            && FreshSample() && _sprintVelocity.magnitude >= MinSlideSpeed)
        {
            // Never reuse a faster pre-wall sprint sample.
            StartSlide(Vector3.ClampMagnitude(_sampleVelocity, _sprintVelocity.magnitude));
        }
        if (_isSliding)
        {
            bool pendingJump = (_preparedAt >= 0f && now >= _preparedAt && now - _preparedAt <= .25f)
                || (_landingJumpAt >= 0f && now >= _landingJumpAt && now - _landingJumpAt <= _landingJumpWindow);
            if (!slideState || !supported) EndSlide(false);
            else if (now - _slideStarted > SlideDuration || _slideVelocity.sqrMagnitude < .25f
                || (!pendingJump && (Context.PoseLevel >= .95f || sprinting))) EndSlide(true);
            else _slideVelocity = Vector3.MoveTowards(_slideVelocity, Vector3.zero,
                SlideFriction * Mathf.Clamp(Time.deltaTime, 0f, .1f));
        }
        if (!slideState || !supported) _returning = false;
        if (_returning && now - FadeStartTime >= FadeOutDuration) _returning = false;
        _wasSprinting = sprinting;
    }

    internal void CrouchIntent()
    {
        if (!ArenaSlideTransitions.Owns(this)) { Clear(); return; }
        if (Context.PoseLevel >= PoseThreshold) { CancelInputIntent(); return; }
        if (!ArenaSlideTransitions.AirState(Context.CurrentState) || !FreshSample()) return;
        if (!Context.IsGrounded && !Player.CharacterController.isGrounded) _leftGround = true;
        if (!_leftGround) return;
        if (_sampleVelocity.magnitude < MinSlideSpeed) return;
        // This is a fresh native crouch command, not a held/repeated polling request.
        _queuedAt = Time.time;
        _contactAt = -1f;
        _landingVelocity = Vector3.zero;
    }

    internal void CancelLanding() { _queuedAt = _contactAt = -1f; _landingVelocity = Vector3.zero; }
    private void ObserveSprintExit()
    {
        // Crouch can start a slide while CurrentState is still Sprint, clearing
        // _wasSprinting before the animator enters its exit. Bind at either Tick
        // or the first motion callback, using the same fresh sampled Sprint.
        // A bound exit cannot be replaced or have its deadline refreshed.
        if (_sprintExitState != null) return;
        float now = Time.time;
        // Sprint release can precede the crouch by one or more standing ticks.
        // Retain that witnessed release only inside its original start window.
        bool recentSprintEnd = _sprintEnded >= 0f && now >= _sprintEnded && now - _sprintEnded <= SlideWindow;
        if ((_wasSprinting || _isSliding || recentSprintEnd) && _lastSprintAt >= 0f
            && now >= _lastSprintAt && now - _lastSprintAt <= SlideWindow
            && ArenaSlideTransitions.Supported(Context) && FreshSample()
            && _lastSprintState?.GetType() == typeof(SprintPlayerState)
            && ReferenceEquals(Context.PreviousState, _lastSprintState)
            && Context.CurrentState?.GetType() == typeof(SprintToIdleTransitionState))
        {
            _sprintExitState = Context.CurrentState;
            _sprintExitAt = now;
        }
    }
    private bool IsSprintExit(BaseMovementState state) => ReferenceEquals(state, _sprintExitState)
        && state?.GetType() == typeof(SprintToIdleTransitionState)
        && ReferenceEquals(Context.PreviousState, _lastSprintState)
        && _sprintExitAt >= 0f && Time.time >= _sprintExitAt
        && Time.time - _sprintExitAt <= SlideWindow + SlideDuration + FadeOutDuration;
    internal bool IsSlideState(BaseMovementState state) => ArenaSlideTransitions.SlideState(state) || IsSprintExit(state);
    internal bool NeedsBufferedJump => Context.CurrentState?.GetType() == typeof(JumpLandingState)
        || IsSprintExit(Context.CurrentState);
    // Native sprint exit clips can outlast 0.5s (observed ~0.7s). The owning
    // jump dispatcher captures this once; ordinary landing keeps its 0.5s bound.
    internal float BufferedJumpWindow => IsSprintExit(Context.CurrentState) ? 1f
        : Context.CurrentState?.GetType() == typeof(JumpLandingState) ? LandingStateWindow : 0f;
    internal void CancelInputIntent()
    {
        CancelLanding();
        _landingJumpAt = _preparedAt = -1f;
        EndSlide(true);
    }
    private bool FreshSample() => _sampleAt >= 0f && Time.time >= _sampleAt && Time.time - _sampleAt <= .2f;
    private bool Descending() => Context.CurrentState is not JumpPlayerState jump
        || (jump._jumpState != JumpPlayerState.EJumpState.PushingFromTheGround && jump._currentLiftforce.y <= 0f);
    private void ConfirmContact(float now)
    {
        if (_leftGround && _queuedAt >= 0f && now >= _queuedAt && now - _queuedAt <= LandingInputWindow)
        {
            _contactAt = now;
            // Capture the successful contact move, before native Sprint entry
            // can restart root motion at zero. Never borrow pre-collision speed.
            _landingVelocity = _sampleVelocity;
        }
        else CancelLanding();
    }

    private void StartSlide(Vector3 velocity)
    {
        if (velocity.magnitude < MinSlideSpeed || !Context.SetPoseLevel(0f, false)
            || !ArenaSlideTransitions.Owns(this)) return;
        _isSliding = true;
        _returning = IsFadingOut = false;
        _slideVelocity = velocity;
        _startSpeed = velocity.magnitude;
        _startAnimSpeed = Context._smoothedCharacterMovementSpeed;
        _slideStarted = Time.time;
        _sprintEnded = _preparedAt = _landingJumpAt = -1f;
        if (Context.CurrentState?.GetType() == typeof(SprintPlayerState))
        {
            // A confirmed landing slide can cancel Sprint before a ticking
            // sprint sample exists. This admitted slide owns that exact exit too.
            _lastSprintState = Context.CurrentState;
            _lastSprintAt = Time.time;
            _sprintExitState = null;
            _sprintExitAt = -1f;
        }
        // A sprint jump can re-enable sprint at native landing. Crouch slide
        // admission uses EFT's ordinary sprint cancellation, never animator state.
        Context.EnableSprint(false);
    }

    private void EndSlide(bool blendMotion)
    {
        if (!_isSliding) return;
        FadeLastSpeed = Mathf.Max(0f, NormalizedSlideAnimSpeed);
        FadeStartTime = Time.time;
        IsFadingOut = true;
        _returnVelocity = _slideVelocity;
        _returning = blendMotion;
        _isSliding = false;
        _slideVelocity = Vector3.zero;
        _preparedAt = _landingJumpAt = -1f;
    }

    internal bool ApplyMotion(ref Vector3 motion, float dt)
    {
        if (!_isSliding && !_returning) return false;
        if (!ArenaSlideTransitions.ValidDelta(dt) || !ArenaSlideTransitions.Owns(this))
        { Clear(); return false; }
        ObserveSprintExit(); // Native animator entry can precede our next Tick.
        if (!IsSlideState(Context.CurrentState) || !ArenaSlideTransitions.Supported(Context))
        { Clear(); return false; }
        Vector3 velocity = _slideVelocity;
        if (!_isSliding)
        {
            float t = Mathf.Clamp01((Time.time - FadeStartTime) / FadeOutDuration);
            if (t >= 1f) { _returning = false; return false; }
            Vector3 native = new Vector3(motion.x / dt, 0f, motion.z / dt);
            velocity = Vector3.Lerp(_returnVelocity, native, t);
        }
        // Already real m/s: replace root XZ AFTER its one scale, never add a
        // second velocity and never touch gravity/slope motion in Y.
        motion.x = velocity.x * dt;
        motion.z = velocity.z * dt;
        return true;
    }

    internal void AppliedMotion(Vector3 velocity, bool contact, float requestedSpeed)
    {
        velocity.y = 0f;
        if (!ArenaSlideTransitions.Finite(velocity.x) || !ArenaSlideTransitions.Finite(velocity.z)
            || !ArenaSlideTransitions.Finite(velocity.sqrMagnitude)
            || !ArenaSlideTransitions.Finite(requestedSpeed) || requestedSpeed < 0f) { Clear(); return; }
        _sampleVelocity = velocity;
        _sampleAt = Time.time;
        // Native landing clips may request zero or accelerate from rest. That
        // is not collision loss. A real requested move can only reduce the
        // contact reserve by its applied/requested planar retention, never grow
        // it or renew its existing contact deadline.
        if (_queuedAt >= 0f && _contactAt >= 0f && requestedSpeed > 0f)
            _landingVelocity *= Mathf.Clamp01(velocity.magnitude / requestedSpeed);
        if (_isSliding && velocity.magnitude < _slideVelocity.magnitude) _slideVelocity = velocity;
        if (_returning && velocity.magnitude < _returnVelocity.magnitude) _returnVelocity = velocity;
        if (contact && Descending() && _contactAt < 0f) ConfirmContact(Time.time);
    }

    internal void PrepareJump()
    {
        CancelLanding();
        if (!_isSliding || !ArenaSlideTransitions.Owns(this)
            || !ArenaSlideTransitions.Supported(Context) || !FreshSample() || !Context.CanJump
            || !Player.Physical.CanJump) return;
        if (NeedsBufferedJump)
        {
            // The sole Jump owner reserves this input and dispatches later.
            // Preserve this slide through native Sprint.Enter's standing/sprint
            // reset regardless of which component Update runs first.
            if (_landingJumpAt < 0f)
            {
                _landingJumpAt = Time.time;
                _landingJumpWindow = BufferedJumpWindow;
            }
            return;
        }
        if (!ArenaSlideTransitions.GroundState(Context.CurrentState)) return;
        // MOVE -> Jump also requires the animator's smoothed Level > .6. Use
        // native immediate pose (as Sprint crouch does), retaining its headroom
        // and physical maximum. This intentionally skips gradual pose stamina;
        // native Jump entry still owns its one ordinary stamina debit.
        // Check headroom explicitly: SetPoseLevel's equal-pose early return skips
        // both CanStandAt and force's smoothed-pose sync on an already standing pose.
        _preparedAt = -1f;
        if (Context.CanStandAt(1f) && Context.SetPoseLevel(1f, true)
            && ArenaSlideTransitions.Owns(this) && Context.PoseLevel > .6f)
        {
            if (Context.SmoothedPoseLevel != Context.PoseLevel)
                Context.SmoothedPoseLevel = Context.PoseLevel;
            if (!ArenaSlideTransitions.Owns(this)) return;
            _preparedAt = Time.time;
            _landingJumpAt = -1f;
        }
    }

    internal void JumpEntered(JumpPlayerState jump)
    {
        if (!_isSliding || _preparedAt < 0f || Time.time - _preparedAt > .25f || !FreshSample()
            || !ArenaSlideTransitions.GroundState(Context.PreviousState)) { EndSlide(false); return; }
        Vector3 velocity = _slideVelocity;
        EndSlide(false);
        // Enter has already computed native lift, stamina and animation. Seed
        // only its horizontal memory. Native ProcessJumpMovement then returns
        // smoothly through AIR_LERP and AirControl retains its existing steering.
        float scale = ArenaMovementPatches.MotionScale(Context);
        jump._initialMagnitude = velocity.magnitude / scale;
        jump._currentMagnitude = jump._initialMagnitude;
        if (velocity.sqrMagnitude > .000001f) jump._initialNormalizedDirection = velocity.normalized;
        ArenaAirControl.ResetHeading(Context);
    }
}
