using EFT;
using UnityEngine;

namespace ArenaMode.Movement;

/// <summary>
/// Sprint→crouch slide (absorbed from the standalone CombatSlide mod, now
/// arena-gated and sharing ONE DirectApplyMotion bracket with the motion scale —
/// which also retires the SpeedLimit save/restore interleave for good).
/// Attached to the local player by MovementRuntime while an arena session runs.
/// </summary>
public class ArenaSlideController : MonoBehaviour
{
    // ── Tuning ──────────────────────────────────────────────────────────────
    public static float MinSlideSpeed = 3.5f;  // minimum m/s at sprint end to allow slide
    public static float SlideDuration = 1.5f;  // hard timeout in seconds
    public static float SlideFriction = 3.0f;  // deceleration in m/s²
    public static float PoseThreshold = 0.85f; // PoseLevel below this counts as "crouching"
    public static float SlideWindow = 0.3f;    // seconds after sprint ends where crouch triggers slide
    public static float FadeOutDuration = 0.2f; // seconds to blend anim speed back to natural value

    // ── Core slide state ────────────────────────────────────────────────────
    private Player _player;
    private MovementContext _ctx;

    private bool _isSliding;
    private Vector3 _slideVelocity;
    private float _slideStartTime;
    private float _slideStartAnimSpeed;

    // Sprint-end tracking
    private bool _wasSprintingLastFrame;
    private Vector3 _lastSprintVelocity;
    private float _sprintEndTime = -1f;
    private Vector3 _sprintEndVelocity;

    // ── Animation fade-out state (read by the anim patch) ───────────────────
    internal bool IsFadingOut;
    internal float FadeStartTime;
    internal float FadeLastSpeed;

    public bool IsSliding => _isSliding;
    public Vector3 SlideVelocity => _slideVelocity;

    /// <summary>Animator speed override while sliding (scales with velocity); -1 when idle.</summary>
    public float NormalizedSlideAnimSpeed
    {
        get
        {
            if (!_isSliding || _slideStartAnimSpeed < 0.001f) return -1f;
            float ratio = _slideVelocity.magnitude / Mathf.Max(_sprintEndVelocity.magnitude, 0.001f);
            return _slideStartAnimSpeed * ratio;
        }
    }

    public void Init(Player player, MovementContext movementContext)
    {
        _player = player;
        _ctx = movementContext;
    }

    private void Update()
    {
        if (_player == null || _ctx == null) return;

        // A respawn does not destroy this body. Arena keeps corpses, so without
        // this the component Unity is still ticking here belongs to a body that
        // will never slide again, and every death left one more of them running
        // for the rest of the raid (measured: 26 deaths, 26 Updates per frame).
        // Stop the dispatch rather than early-out, so the cost is bounded by the
        // live body instead of the death count. This also closes a latent write:
        // a body that died within the slide window could still reach
        // SetPoseLevel below. MovementRuntime re-enables the component if this
        // exact body is ever the live local player again.
        if (_player.HealthController?.IsAlive != true)
        {
            _isSliding = false;
            _slideVelocity = Vector3.zero;
            _sprintEndTime = -1f;
            _wasSprintingLastFrame = false;
            IsFadingOut = false;
            enabled = false;
            return;
        }

        bool isSprinting = _player.IsSprintEnabled;

        if (isSprinting)
        {
            Vector3 v = _ctx.Velocity;
            v.y = 0f;
            if (v.sqrMagnitude > 0.1f)
                _lastSprintVelocity = v;
        }

        if (_wasSprintingLastFrame && !isSprinting)
        {
            _sprintEndTime = Time.time;
            _sprintEndVelocity = _lastSprintVelocity;
        }

        if (!_isSliding)
            TryStartSlide();
        else
            UpdateSlide(isSprinting);

        _wasSprintingLastFrame = isSprinting;
    }

    private void TryStartSlide()
    {
        if (_sprintEndTime < 0f) return;
        if (Time.time - _sprintEndTime > SlideWindow) return;

        float poseLevel = _ctx._poseLevel;
        float speed = _sprintEndVelocity.magnitude;

        if (poseLevel >= PoseThreshold || speed < MinSlideSpeed) return;

        _isSliding = true;
        _slideStartTime = Time.time;
        _slideVelocity = _sprintEndVelocity;
        _slideStartAnimSpeed = _ctx._smoothedCharacterMovementSpeed;
        _sprintEndTime = -1f;

        // Drop to maximum crouch depth for the slide visual
        _ctx.SetPoseLevel(0f, false);
    }

    private void UpdateSlide(bool isSprinting)
    {
        bool timedOut = Time.time - _slideStartTime > SlideDuration;
        bool tooSlow = _slideVelocity.sqrMagnitude < 0.25f;
        bool stoodUp = _ctx._poseLevel >= 0.95f;

        if (timedOut || tooSlow || stoodUp || isSprinting)
        {
            EndSlide();
            return;
        }

        _slideVelocity = Vector3.MoveTowards(_slideVelocity, Vector3.zero, SlideFriction * Time.deltaTime);
    }

    private void EndSlide()
    {
        // Capture the last controlled anim speed BEFORE zeroing velocity.
        float lastSpeed = NormalizedSlideAnimSpeed;

        _isSliding = false;
        _slideVelocity = Vector3.zero;

        // Hand off anim control via a short fade instead of cutting abruptly —
        // otherwise the animator jumps and residual root motion "coasts".
        IsFadingOut = true;
        FadeStartTime = Time.time;
        FadeLastSpeed = lastSpeed >= 0f ? lastSpeed : 0f;
    }
}
