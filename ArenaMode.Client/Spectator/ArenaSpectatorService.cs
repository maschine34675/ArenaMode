using ArenaMode.Compat;
using ArenaMode.Events;
using ArenaMode.Hud;
using ArenaMode.Session;
using Comfort.Common;
using EFT;
using EFT.CameraControl;
using EFT.InputSystem;
using EFT.UI;
using EFT.UI.Screens;
using Redeploy.Core.Engine;
using Redeploy.Core.View;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace ArenaMode.Spectator;

/// <summary>
/// Owns a bounded post-death camera without replacing the native FPS camera or
/// changing any participant. Tactical supplies the immutable match identities
/// and a live resolver; this service independently rejects drift on every tick.
/// </summary>
internal static class ArenaSpectatorService
{
    private const float MinimumPendingSeconds = 0.02f;
    private const float ExternalFreecamWaitSeconds = 5f;
    private const float MaximumPendingSeconds = 75f;
    private const float FollowDistance = 2.15f;
    private const float ShoulderOffset = 0.48f;
    private const float HeightOffset = 0.32f;
    private const float CollisionRadius = 0.14f;
    private const float CollisionPadding = 0.08f;

    private static readonly object InputOwner = new();
    private static readonly HashSet<string> AllowedProfileIds = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, Player> FrozenBodiesByProfileId =
        new(StringComparer.Ordinal);
    private static readonly List<ArenaSpectatorTarget> LivingTargets = new();
    private static readonly ReferenceSet<Player> SeenBodies = new();
    private static readonly HashSet<string> SeenProfiles = new(StringComparer.Ordinal);

    private static bool _initialized;
    private static bool _pending;
    private static bool _ownsCamera;
    private static bool _renderFaulted;
    private static bool _renderFailureLogged;
    private static bool _compatibilityFailureLogged;
    private static float _notBefore;
    private static float _pendingDeadline;
    private static int _worldCollisionMask;
    private static ArenaSpectatorRequest _request;
    private static ArenaSpectatorTarget _currentTarget;
    private static Camera _camera;
    private static Vector3 _renderPosition;
    private static Quaternion _renderRotation;

    internal static bool IsPending => _pending;
    internal static bool IsActive => _ownsCamera;
    internal static bool IsReleased => !_pending && !_ownsCamera;
    internal static bool OwnsCamera => _ownsCamera;

    /// <summary>
    /// Team-mode replay advertisement must use this. A present but unobservable
    /// Killcam is suppressed rather than allowed to race the spectator camera.
    /// </summary>
    internal static bool OptionalKillcamReplayMayRun => KillcamReplayCompat.ReplayMayRun;

    /// <summary>
    /// The native owner rejects every command once its player is dead, including
    /// Escape before EftGamePlayerOwner can open the raid menu. Permit only that
    /// native exit translation while this service owns the exact corpse camera.
    /// </summary>
    internal static bool TryTranslateEscape(
        EftGamePlayerOwner owner,
        ECommand command,
        out InputNode.ETranslateResult result)
    {
        result = InputNode.ETranslateResult.Ignore;
        if (command != ECommand.Escape
            || !_ownsCamera
            || !ArenaSession.IsActive
            || !PitFireTeamCompat.TeamArenaActive)
        {
            return false;
        }

        ArenaSpectatorRequest request = _request;
        if (request == null || ReferenceEquals(owner, null))
            return false;

        bool epochMatches;
        bool requestEligible;
        bool battleUiActive;
        try
        {
            epochMatches = request.ReadEpoch != null && request.ReadEpoch() == request.Epoch;
            requestEligible = request.IsEligible != null && request.IsEligible();
            battleUiActive = EftScreenManager.Instance != null
                             && EftScreenManager.Instance.CheckCurrentScreen(EEftScreenType.BattleUI);
        }
        catch (Exception ex)
        {
            Plugin.ModLogger.LogWarning($"Arena spectator Escape gate failed closed: {ex.Message}");
            return false;
        }

        Player expectedBody = request.DeadPlayer;
        Player ownerBody;
        try
        {
            ownerBody = owner.Player;
        }
        catch
        {
            return false;
        }

        var gate = new SpectatorEscapeGate(
            escapeCommand: true,
            arenaActive: true,
            teamArenaActive: true,
            ownsCamera: true,
            battleUiActive,
            requestEligible,
            epochMatches,
            !ReferenceEquals(expectedBody, null) && expectedBody != null && expectedBody.IsYourPlayer,
            !ReferenceEquals(expectedBody, null)
                && expectedBody != null
                && expectedBody.HealthController?.IsAlive == false,
            expectedBody,
            ownerBody);
        if (!SpectatorPolicy.ShouldTranslateEscape(gate))
            return false;

        try
        {
            if (!owner.TranslateExitScreenInput(command))
                return false;
            result = InputNode.ETranslateResult.BlockAll;
            Plugin.ModLogger.LogInfo("Arena spectator routed Escape to the native raid menu.");
            return true;
        }
        catch (Exception ex)
        {
            Plugin.ModLogger.LogWarning($"Arena spectator could not open the native raid menu: {ex}");
            return false;
        }
    }

    internal static string CurrentStatusLine
    {
        get
        {
            if (!_ownsCamera || _currentTarget == null)
                return null;

            try
            {
                if (_renderFaulted || !HasExactCameraOwnership()
                    || string.IsNullOrEmpty(_currentTarget.ProfileId)
                    || !FrozenBodiesByProfileId.TryGetValue(_currentTarget.ProfileId, out Player frozenBody))
                {
                    return null;
                }

                return SpectatorHealthReadout.ReadStatus(
                    ArenaSession.IsActive, PitFireTeamCompat.TeamArenaActive, _ownsCamera,
                    _request, _currentTarget, frozenBody);
            }
            catch
            {
                // The camera/body may disappear between Tick and this GUI event.
                return null;
            }
        }
    }

    internal static void Initialize()
    {
        if (_initialized)
            return;

        _initialized = true;
        RaidLifecycle.EnsurePatched();
        RaidLifecycle.OnRaidStarted += OnRaidStarted;
        RaidLifecycle.OnRaidEnded += OnRaidEnded;
        ArenaEvents.OnPlayerRespawned += OnPlayerRespawned;
    }

    internal static void Shutdown()
    {
        if (!_initialized)
            return;

        Stop("plugin shutdown");
        RaidLifecycle.OnRaidStarted -= OnRaidStarted;
        RaidLifecycle.OnRaidEnded -= OnRaidEnded;
        ArenaEvents.OnPlayerRespawned -= OnPlayerRespawned;
        _initialized = false;
    }

    /// <summary>
    /// Arms one death-specific lease. The nine allowed ids are copied now, so a
    /// mutable provider cannot silently recruit ambient or replacement bots.
    /// </summary>
    internal static bool RequestStart(ArenaSpectatorRequest request, out string problem)
    {
        problem = null;
        if (request == null)
        {
            problem = "the spectator request was null";
            return false;
        }

        if (ReferenceEquals(request.DeadPlayer, null)
            || request.DeadPlayer == null
            || !request.DeadPlayer.IsYourPlayer)
        {
            problem = "the spectator request did not identify the exact local player body";
            return false;
        }

        if (request.ReadEpoch == null || request.IsEligible == null || request.TargetProvider == null)
        {
            problem = "the spectator request was missing its epoch, eligibility or target provider";
            return false;
        }

        if (request.Cooperative != (ArenaSession.ActiveMode == Shared.ArenaPopulationPolicy.Zombie))
        {
            problem = "the spectator roster shape does not match the active mode";
            return false;
        }

        if (!SpectatorPolicy.TryFreezeAllowedProfileIds(
                request.AllowedProfileIds,
                out string[] profileIds,
                out problem,
                request.Cooperative))
        {
            return false;
        }

        string localProfileId = request.DeadPlayer.ProfileId?.Trim();
        foreach (string profileId in profileIds)
        {
            if (string.Equals(profileId, localProfileId, StringComparison.Ordinal))
            {
                problem = "the local player appeared in the non-local spectator identity set";
                return false;
            }
        }

        try
        {
            if (request.ReadEpoch() != request.Epoch)
            {
                problem = "the spectator request epoch was already stale";
                return false;
            }
        }
        catch (Exception ex)
        {
            problem = "the spectator request epoch could not be read: " + ex.GetBaseException().Message;
            return false;
        }

        Stop("superseded spectator request");
        SpectatorHealthEffects.ResetForLease();
        AllowedProfileIds.Clear();
        foreach (string profileId in profileIds)
            AllowedProfileIds.Add(profileId);

        _request = request;
        _pending = true;
        _notBefore = Time.unscaledTime + MinimumPendingSeconds;
        _pendingDeadline = Time.unscaledTime + MaximumPendingSeconds;
        _compatibilityFailureLogged = false;
        SpectatorHealthEffects.ClearForDeadLocal(request.DeadPlayer);
        Plugin.ModLogger.LogInfo(
            $"Arena spectator pending for epoch {request.Epoch}; waiting for optional camera owners to release.");
        return true;
    }

    internal static void Tick()
    {
        try
        {
            TickUnsafe();
        }
        catch (Exception ex)
        {
            Plugin.ModLogger.LogWarning($"Arena spectator tick failed closed: {ex}");
            Stop("an unexpected spectator tick failure occurred");
        }
    }

    private static void TickUnsafe()
    {
        if (!_pending && !_ownsCamera)
            return;

        if (_renderFaulted)
        {
            Stop("camera render callback failed");
            return;
        }

        if (!TryValidateLease(out bool localPlayerDead, out string leaseProblem))
        {
            Stop(leaseProblem);
            return;
        }

        if (!localPlayerDead)
        {
            Stop("the local player became alive again");
            return;
        }

        // Keep the shared FPS camera clean throughout Killcam's lead-in and
        // playback. This is idempotent and refuses any body but the exact corpse
        // accepted by this spectator lease.
        SpectatorHealthEffects.ClearForDeadLocal(_request.DeadPlayer);

        if (!TryReadLivingTargets(out string targetProblem))
        {
            Stop(targetProblem);
            return;
        }

        OptionalCameraOwnerState killcam = KillcamReplayCompat.ReadState(
            out float killcamRemaining,
            out string killcamProblem);
        OptionalCameraOwnerState freecam = TerkoizFreecamProbe.ReadState(out string freecamProblem);

        if (_ownsCamera)
        {
            if (!HasExactCameraOwnership())
            {
                Stop("the native camera controller or main camera changed");
                return;
            }

            if (killcam == OptionalCameraOwnerState.Busy
                || killcam == OptionalCameraOwnerState.Incompatible
                || freecam == OptionalCameraOwnerState.Busy
                || freecam == OptionalCameraOwnerState.Incompatible)
            {
                Stop(OwnerProblem(killcam, killcamProblem, freecam, freecamProblem));
                return;
            }

            if (LivingTargets.Count == 0)
            {
                Stop("no living fixed 5v5 participant remained to observe");
                return;
            }

            UpdateCurrentTargetFromInput();
            if (!TryCacheFollowPose(_currentTarget, out string poseProblem))
                Stop(poseProblem);
            return;
        }

        if (Time.unscaledTime < _notBefore)
            return;

        var gate = new SpectatorStartGate(
            ArenaSession.IsActive,
            PitFireTeamCompat.TeamArenaActive,
            requestEligible: true,
            epochMatches: true,
            localPlayerDead,
            LivingTargets.Count,
            killcam,
            freecam);
        SpectatorStartDecision decision = SpectatorPolicy.DecideStart(gate);
        if (decision == SpectatorStartDecision.Cancel)
        {
            Stop(OwnerProblem(killcam, killcamProblem, freecam, freecamProblem));
            return;
        }

        if (decision == SpectatorStartDecision.Wait)
        {
            if (killcam == OptionalCameraOwnerState.Busy && killcamRemaining > 0f)
            {
                float observedBound = Time.unscaledTime + Mathf.Clamp(killcamRemaining + 1f, 2f, MaximumPendingSeconds);
                _pendingDeadline = Mathf.Min(_pendingDeadline, observedBound);
            }
            else if (freecam == OptionalCameraOwnerState.Busy)
            {
                _pendingDeadline = Mathf.Min(
                    _pendingDeadline,
                    Time.unscaledTime + ExternalFreecamWaitSeconds);
            }

            if (Time.unscaledTime >= _pendingDeadline)
            {
                Stop(LivingTargets.Count == 0
                    ? "no living fixed 5v5 participant appeared within the bounded start window"
                    : "optional camera owner did not release within its bounded hand-off window");
            }
            return;
        }

        StartFollowing();
    }

    /// <summary>
    /// Synchronous and idempotent. Clear ownership first so the narrow camera
    /// patch fails open even if a later cleanup action throws.
    /// </summary>
    internal static void Stop(string reason)
    {
        bool hadState = _pending || _ownsCamera || _request != null;
        Player playerToRestore = _request?.DeadPlayer;
        bool returnOwnedCameraToNative = false;
        bool restoreFirstPerson = false;
        try
        {
            bool optionalOwnersPermitRestore = OptionalOwnersPermitNativeRestore();
            returnOwnedCameraToNative = _ownsCamera
                                        && HasBoundCameraController(playerToRestore)
                                        && optionalOwnersPermitRestore;
            restoreFirstPerson = !ReferenceEquals(playerToRestore, null)
                                 && playerToRestore != null
                                 && playerToRestore.HealthController?.IsAlive == true
                                 && optionalOwnersPermitRestore;
        }
        catch
        {
            // A destroyed Unity player cannot be restored and must not block cleanup.
        }

        _ownsCamera = false;
        _pending = false;

        try
        {
            Application.onBeforeRender -= ApplyRenderPose;
        }
        catch (Exception ex)
        {
            Plugin.ModLogger.LogWarning($"Arena spectator render callback release failed: {ex.Message}");
        }

        try
        {
            ArenaInputBlocker.Request(InputOwner, false);
        }
        catch (Exception ex)
        {
            Plugin.ModLogger.LogWarning($"Arena spectator input release failed: {ex.Message}");
        }

        _request = null;
        _currentTarget = null;
        _camera = null;
        _renderFaulted = false;
        _renderFailureLogged = false;
        _notBefore = 0f;
        _pendingDeadline = 0f;
        LivingTargets.Clear();
        AllowedProfileIds.Clear();
        FrozenBodiesByProfileId.Clear();
        SeenBodies.Clear();
        SeenProfiles.Clear();

        if (hadState)
            Plugin.ModLogger.LogInfo($"Arena spectator released ({reason ?? "unspecified"}).");

        if (returnOwnedCameraToNative || restoreFirstPerson)
            RestoreControllerOperation(playerToRestore, restoreFirstPerson);
    }

    /// <summary>Backstop for either a replacement body or a same-body restore.</summary>
    internal static void RestoreFirstPerson(Player liveLocalPlayer)
    {
        if (ReferenceEquals(liveLocalPlayer, null)
            || liveLocalPlayer == null
            || liveLocalPlayer.HealthController?.IsAlive != true
            || !Singleton<PlayerCameraController>.Instantiated)
        {
            return;
        }

        RestoreControllerOperation(liveLocalPlayer, forceLivePosition: true);
    }

    private static bool OptionalOwnersPermitNativeRestore()
    {
        OptionalCameraOwnerState killcam = KillcamReplayCompat.ReadState(out _, out _);
        OptionalCameraOwnerState freecam = TerkoizFreecamProbe.ReadState(out _);
        return (killcam == OptionalCameraOwnerState.Absent || killcam == OptionalCameraOwnerState.Idle)
               && (freecam == OptionalCameraOwnerState.Absent || freecam == OptionalCameraOwnerState.Idle);
    }

    private static bool HasBoundCameraController(Player expectedPlayer)
    {
        if (ReferenceEquals(expectedPlayer, null)
            || expectedPlayer == null
            || !Singleton<PlayerCameraController>.Instantiated)
        {
            return false;
        }

        return ReferenceEquals(
            Singleton<PlayerCameraController>.Instance.Player,
            expectedPlayer);
    }

    private static void RestoreControllerOperation(Player player, bool forceLivePosition)
    {
        if (ReferenceEquals(player, null) || player == null
            || !Singleton<PlayerCameraController>.Instantiated)
        {
            return;
        }

        try
        {
            PlayerCameraController controller = Singleton<PlayerCameraController>.Instance;
            if (!ReferenceEquals(controller.Player, player))
                return;

            controller.InitiateOperation<FirstPersonCameraState>();
            Camera camera = CameraManager.Instance?.Camera;
            if (forceLivePosition && camera != null && player.CameraPosition != null)
                CameraManager.Instance.ForceSetPosition(player.CameraPosition.position);
        }
        catch (Exception ex)
        {
            Plugin.ModLogger.LogWarning($"Arena spectator native-camera restore failed: {ex}");
        }
    }

    private static void StartFollowing()
    {
        try
        {
            if (!Singleton<PlayerCameraController>.Instantiated)
                throw new InvalidOperationException("PlayerCameraController was unavailable");

            PlayerCameraController controller = Singleton<PlayerCameraController>.Instance;
            if (!ReferenceEquals(controller.Player, _request.DeadPlayer))
                throw new InvalidOperationException("the camera controller was no longer bound to the dead local body");

            _camera = CameraManager.Instance?.Camera;
            if (_camera == null)
                throw new InvalidOperationException("the main FPS camera was unavailable");

            SelectInitialTarget();
            if (!TryCacheFollowPose(_currentTarget, out string poseProblem))
                throw new InvalidOperationException(poseProblem);

            controller.InitiateOperation<LockCameraState>();
            _pending = false;
            _ownsCamera = true;
            ArenaInputBlocker.Request(InputOwner, true);
            DeathEffectsHelper.ClearDeathScreenEffects();
            SpectatorHealthEffects.ClearForDeadLocal(_request.DeadPlayer, force: true);
            Application.onBeforeRender -= ApplyRenderPose;
            Application.onBeforeRender += ApplyRenderPose;
            ApplyRenderPose();

            Plugin.ModLogger.LogInfo(
                $"Arena spectator following {_currentTarget.Side.ToString().ToLowerInvariant()} "
                + $"{_currentTarget.ProfileId}; {LivingTargets.Count} living target(s) available.");
        }
        catch (Exception ex)
        {
            Plugin.ModLogger.LogWarning($"Arena spectator could not take camera ownership: {ex}");
            Stop("camera acquisition failed");
        }
    }

    private static bool TryValidateLease(out bool localPlayerDead, out string problem)
    {
        localPlayerDead = false;
        problem = null;
        ArenaSpectatorRequest request = _request;
        if (request == null)
        {
            problem = "the spectator lease disappeared";
            return false;
        }

        if (!ArenaSession.IsActive || !PitFireTeamCompat.TeamArenaActive)
        {
            problem = "the protected team Arena session ended";
            return false;
        }

        try
        {
            if (request.ReadEpoch() != request.Epoch)
            {
                problem = "the spectator lease epoch changed";
                return false;
            }

            if (!request.IsEligible())
            {
                problem = "the Tactical round no longer permits observation";
                return false;
            }
        }
        catch (Exception ex)
        {
            problem = "the spectator lease validation failed: " + ex.GetBaseException().Message;
            return false;
        }

        Player deadPlayer = request.DeadPlayer;
        if (ReferenceEquals(deadPlayer, null) || deadPlayer == null || !deadPlayer.IsYourPlayer)
        {
            problem = "the original local player body disappeared";
            return false;
        }

        localPlayerDead = deadPlayer.HealthController?.IsAlive == false;
        return true;
    }

    private static bool TryReadLivingTargets(out string problem)
    {
        problem = null;
        LivingTargets.Clear();
        SeenBodies.Clear();
        SeenProfiles.Clear();

        IReadOnlyList<ArenaSpectatorTarget> supplied;
        try
        {
            supplied = _request.TargetProvider();
        }
        catch (Exception ex)
        {
            problem = "the spectator target provider threw: " + ex.GetBaseException().Message;
            return false;
        }

        if (supplied == null)
        {
            problem = "the spectator target provider returned null";
            return false;
        }

        int required = SpectatorPolicy.RequiredNonLocalActors(_request.Cooperative);
        if (supplied.Count != required)
        {
            problem = $"the spectator target provider returned {supplied.Count}/{required} fixed participants";
            return false;
        }

        for (int index = 0; index < supplied.Count; index++)
        {
            ArenaSpectatorTarget target = supplied[index];
            Player player = target?.Player;
            if (target == null || ReferenceEquals(player, null) || player == null)
            {
                problem = $"spectator target {index + 1} was missing its player body";
                return false;
            }

            string profileId = target.ProfileId?.Trim();
            string bodyProfileId = player.ProfileId?.Trim();
            if (string.IsNullOrEmpty(profileId)
                || !AllowedProfileIds.Contains(profileId)
                || !string.Equals(profileId, bodyProfileId, StringComparison.Ordinal))
            {
                problem = $"spectator target {index + 1} was outside the frozen identity set";
                return false;
            }

            if (FrozenBodiesByProfileId.TryGetValue(profileId, out Player frozenBody))
            {
                if (!ReferenceEquals(frozenBody, player))
                {
                    problem = $"spectator target {profileId} was replaced during the same round epoch";
                    return false;
                }
            }
            else
            {
                FrozenBodiesByProfileId.Add(profileId, player);
            }

            if (!SeenProfiles.Add(profileId) || !SeenBodies.Add(player))
            {
                problem = $"spectator target {profileId} was duplicated or replaced";
                return false;
            }

            if (player.gameObject == null
                || !player.gameObject.activeInHierarchy
                || player.HealthController?.IsAlive != true)
            {
                continue;
            }

            LivingTargets.Add(target);
        }

        if (!SeenProfiles.SetEquals(AllowedProfileIds))
        {
            problem = "the spectator target provider did not return the exact frozen 5v5 identity set";
            return false;
        }

        return true;
    }

    private static void SelectInitialTarget()
    {
        string preferred = _request.PreferredProfileId;
        if (!string.IsNullOrEmpty(preferred))
        {
            for (int index = 0; index < LivingTargets.Count; index++)
            {
                if (string.Equals(LivingTargets[index].ProfileId, preferred, StringComparison.Ordinal))
                {
                    _currentTarget = LivingTargets[index];
                    return;
                }
            }
        }

        ArenaSpectatorTarget nearestAlly = null;
        float nearestDistance = float.MaxValue;
        Vector3 corpsePosition = _request.DeadPlayer.Transform.position;
        for (int index = 0; index < LivingTargets.Count; index++)
        {
            if (LivingTargets[index].Side == ArenaSpectatorSide.Ally)
            {
                float distance = (LivingTargets[index].Player.Transform.position - corpsePosition).sqrMagnitude;
                if (distance < nearestDistance)
                {
                    nearestDistance = distance;
                    nearestAlly = LivingTargets[index];
                }
            }
        }

        if (nearestAlly != null)
        {
            _currentTarget = nearestAlly;
            return;
        }

        _currentTarget = LivingTargets[0];
    }

    private static void UpdateCurrentTargetFromInput()
    {
        int currentIndex = IndexOfCurrentTarget();
        if (currentIndex < 0)
        {
            SelectInitialTarget();
            return;
        }

        if (Cursor.visible || IsConsoleVisible())
            return;

        int direction = 0;
        if (Input.GetKeyDown(KeyCode.Mouse0))
            direction = 1;
        else if (Input.GetKeyDown(KeyCode.Mouse1))
            direction = -1;

        if (direction == 0)
            return;

        int next = SpectatorPolicy.CycleIndex(LivingTargets.Count, currentIndex, direction);
        if (next >= 0)
        {
            _currentTarget = LivingTargets[next];
            Plugin.ModLogger.LogDebug($"Arena spectator switched to {_currentTarget.ProfileId}.");
        }
    }

    private static int IndexOfCurrentTarget()
    {
        if (_currentTarget == null)
            return -1;

        for (int index = 0; index < LivingTargets.Count; index++)
        {
            if (string.Equals(
                    LivingTargets[index].ProfileId,
                    _currentTarget.ProfileId,
                    StringComparison.Ordinal)
                && ReferenceEquals(LivingTargets[index].Player, _currentTarget.Player))
            {
                _currentTarget = LivingTargets[index];
                return index;
            }
        }

        return -1;
    }

    private static bool HasExactCameraOwnership()
    {
        if (!Singleton<PlayerCameraController>.Instantiated)
            return false;

        PlayerCameraController controller = Singleton<PlayerCameraController>.Instance;
        return ReferenceEquals(controller.Player, _request?.DeadPlayer)
               && _camera != null
               && ReferenceEquals(CameraManager.Instance?.Camera, _camera);
    }

    private static bool IsConsoleVisible()
    {
        try
        {
            return MonoBehaviourSingleton<PreloaderUI>.Instantiated
                   && MonoBehaviourSingleton<PreloaderUI>.Instance.Console?.IsConsoleVisible == true;
        }
        catch
        {
            // This is an input convenience check, never a reason to lose camera ownership.
            return false;
        }
    }

    private static bool TryCacheFollowPose(ArenaSpectatorTarget target, out string problem)
    {
        problem = null;
        Player player = target?.Player;
        if (ReferenceEquals(player, null)
            || player == null
            || player.gameObject == null
            || player.HealthController?.IsAlive != true)
        {
            problem = "the selected spectator target was no longer alive";
            return false;
        }

        Transform anchor = player.PlayerBones?.LootRaycastOrigin ?? player.CameraPosition;
        if (anchor == null)
            anchor = player.Transform?.Original;
        if (anchor == null)
        {
            problem = "the selected spectator target had no camera anchor";
            return false;
        }

        Vector2 playerRotation = player.Rotation;
        Quaternion yaw = Quaternion.Euler(0f, playerRotation.x, 0f);
        Quaternion view = Quaternion.Euler(playerRotation.y, playerRotation.x, 0f);
        Vector3 focus = anchor.position + Vector3.up * 0.12f;
        Vector3 desired = focus
            - yaw * Vector3.forward * FollowDistance
            + yaw * Vector3.right * ShoulderOffset
            + Vector3.up * HeightOffset;

        Vector3 offset = desired - focus;
        float distance = offset.magnitude;
        if (distance > Mathf.Epsilon)
        {
            int mask = WorldCollisionMask();
            if (Physics.SphereCast(
                    focus,
                    CollisionRadius,
                    offset / distance,
                    out RaycastHit hit,
                    distance,
                    mask,
                    QueryTriggerInteraction.Ignore))
            {
                float safeDistance = Mathf.Max(0.25f, hit.distance - CollisionPadding);
                desired = focus + offset.normalized * safeDistance;
            }
        }

        Vector3 lookPoint = focus + view * Vector3.forward * 4f;
        Vector3 look = lookPoint - desired;
        if (look.sqrMagnitude < 0.001f)
            look = yaw * Vector3.forward;

        _renderPosition = desired;
        _renderRotation = Quaternion.LookRotation(look.normalized, Vector3.up);
        return true;
    }

    private static int WorldCollisionMask()
    {
        if (_worldCollisionMask != 0)
            return _worldCollisionMask;

        _worldCollisionMask = LayerMask.GetMask(
            "Default",
            "HighPolyCollider",
            "LowPolyCollider",
            "Terrain",
            "Interactive");
        if (_worldCollisionMask == 0)
            _worldCollisionMask = Physics.DefaultRaycastLayers;
        return _worldCollisionMask;
    }

    private static void ApplyRenderPose()
    {
        if (!_ownsCamera)
            return;

        try
        {
            if (_camera == null)
                throw new MissingReferenceException("the main FPS camera was destroyed");

            Transform transform = _camera.transform;
            transform.position = _renderPosition;
            transform.rotation = _renderRotation;
        }
        catch (Exception ex)
        {
            _renderFaulted = true;
            if (!_renderFailureLogged)
            {
                _renderFailureLogged = true;
                Plugin.ModLogger.LogWarning($"Arena spectator render callback failed: {ex}");
            }
        }
    }

    private static string OwnerProblem(
        OptionalCameraOwnerState killcam,
        string killcamProblem,
        OptionalCameraOwnerState freecam,
        string freecamProblem)
    {
        string problem;
        if (killcam == OptionalCameraOwnerState.Incompatible)
            problem = killcamProblem ?? "KillcamReplay ownership could not be verified";
        else if (freecam == OptionalCameraOwnerState.Incompatible)
            problem = freecamProblem ?? "Terkoiz.Freecam ownership could not be verified";
        else if (killcam == OptionalCameraOwnerState.Busy)
            problem = "KillcamReplay took camera ownership after the spectator hand-off";
        else if (freecam == OptionalCameraOwnerState.Busy)
            problem = "Terkoiz.Freecam owns the camera";
        else
            problem = "the spectator start contract became invalid";

        if (!_compatibilityFailureLogged)
        {
            _compatibilityFailureLogged = true;
            Plugin.ModLogger.LogWarning("Arena spectator camera ownership check failed: " + problem);
        }

        return problem;
    }

    private static void OnRaidStarted(GameWorld world)
    {
        Stop("new raid boundary");
        SpectatorHealthEffects.ResetForLease();
        KillcamReplayCompat.Reset();
        TerkoizFreecamProbe.Reset();
    }

    private static void OnRaidEnded()
    {
        Stop("raid ended");
        SpectatorHealthEffects.ResetForLease();
        KillcamReplayCompat.Reset();
        TerkoizFreecamProbe.Reset();
    }

    private static void OnPlayerRespawned(Player player)
    {
        Stop("local player respawned");
        RestoreFirstPerson(player);
    }

    private sealed class ReferenceSet<T> where T : class
    {
        private readonly HashSet<T> _items = new(ReferenceComparer<T>.Instance);

        public bool Add(T value) => _items.Add(value);
        public void Clear() => _items.Clear();
    }

    private sealed class ReferenceComparer<T> : IEqualityComparer<T> where T : class
    {
        public static readonly ReferenceComparer<T> Instance = new();

        public bool Equals(T x, T y) => ReferenceEquals(x, y);
        public int GetHashCode(T obj) => RuntimeHelpers.GetHashCode(obj);
    }
}
