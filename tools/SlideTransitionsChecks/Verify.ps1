$ErrorActionPreference='Stop'
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
. (Join-Path $repo 'tools/Invoke-ArenaSourceChecks.ps1')
$project=Join-Path $PSScriptRoot 'SlideTransitionsChecks.csproj'
$slide=Join-Path $repo 'ArenaMode.Client/Movement/ArenaSlideController.cs'
$transitions=Join-Path $repo 'ArenaMode.Client/Movement/ArenaSlideTransitions.cs'
$faults=@(
    @{Name='AdditiveSlide';Path=$slide;Property='SlideSource';Find='motion.x = velocity.x * dt;';Replace='motion.x += velocity.x * dt;';Failure='slide replaces root motion without speed addition'},
    @{Name='SecondMove';Path=$slide;Property='SlideSource';Find='motion.x = velocity.x * dt;';Replace='Context.DirectApplyMotion(motion, dt); motion.x = velocity.x * dt;';Failure='one native movement call per sample'},
    @{Name='VerticalWrite';Path=$slide;Property='SlideSource';Find='motion.x = velocity.x * dt;';Replace='motion.y = 0f; motion.x = velocity.x * dt;';Failure='slide preserves native vertical motion'},
    @{Name='BankWallSpeed';Path=$slide;Property='SlideSource';Find='if (_isSliding && velocity.magnitude < _slideVelocity.magnitude) _slideVelocity = velocity;';Replace='';Failure='collision permanently reduces slide momentum'},
    @{Name='ExternalDisplacement';Path=$transitions;Property='TransitionsSource';Find='velocity = Vector3.ClampMagnitude(velocity, requested.magnitude);';Replace='';Failure='depenetration cannot create landing slide speed'},
    @{Name='StaleAirHeading';Path=$slide;Property='SlideSource';Find='ArenaAirControl.ResetHeading(Context);';Replace='';Failure='slide jump retires previous AirControl heading'},
    @{Name='DoubleScale';Path=$slide;Property='SlideSource';Find='velocity.magnitude / scale';Replace='velocity.magnitude';Failure='jump seed divides motion scale exactly once'},
    @{Name='UnboundedInput';Path=$slide;Property='SlideSource';Find='LandingInputWindow = .35f';Replace='LandingInputWindow = 35f';Failure='expired air crouch intent cannot slide'},
    @{Name='NativeLandingPose';Path=$slide;Property='SlideSource';Find='if ((!groundState && !airborne && Context.CurrentState is not JumpLandingState)';Replace='if (Context.PoseLevel >= PoseThreshold || (!groundState && !airborne && Context.CurrentState is not JumpLandingState)';Failure='native sprint landing pose reset preserves queued crouch'},
    @{Name='DiscardContactMomentum';Path=$slide;Property='SlideSource';Find='Vector3 velocity = _landingVelocity;';Replace='Vector3 velocity = _sampleVelocity;';Failure='native zero sprint entry preserves physical landing momentum'},
    @{Name='BankLandingWallSpeed';Path=$slide;Property='SlideSource';Find='_landingVelocity *= Mathf.Clamp01(velocity.magnitude / requestedSpeed);';Replace='_landingVelocity *= 1f;';Failure='wall after zero request permanently reduces landing reserve'},
    @{Name='RefreshLandingContact';Path=$slide;Property='SlideSource';Find='if (contact && Descending() && _contactAt < 0f)';Replace='if (contact && Descending())';Failure='repeated physical contact cannot renew deadline or raise landing reserve'},
    @{Name='CancelLandingReserve';Path=$slide;Property='SlideSource';Find='internal void CancelLanding() { _queuedAt = _contactAt = -1f; _landingVelocity = Vector3.zero; }';Replace='internal void CancelLanding() { _queuedAt = _contactAt = -1f; }';Failure='expired contact removes landing reserve'},
    @{Name='ClearLandingReserve';Path=$slide;Property='SlideSource';Find="_slideVelocity = _sprintVelocity = _sampleVelocity = _returnVelocity = Vector3.zero;`n        _landingVelocity = Vector3.zero;";Replace='_slideVelocity = _sprintVelocity = _sampleVelocity = _returnVelocity = Vector3.zero;';Failure='clear removes landing reserve'},
    @{Name='LandingSprintWitness';Path=$slide;Property='SlideSource';Find='if (Context.CurrentState?.GetType() == typeof(SprintPlayerState))';Replace='if (false)';Failure='landing sprint exit retains admitted contact slide before Tick'},
    @{Name='NativeSprintExit';Path=$slide;Property='SlideSource';Find='&& Context.CurrentState?.GetType() == typeof(SprintToIdleTransitionState))';Replace='&& false)';Failure='landing sprint exit retains admitted contact slide before Tick'},
    @{Name='EarlySprintExitMotion';Path=$slide;Property='SlideSource';Find='ObserveSprintExit(); // Native animator entry can precede our next Tick.';Replace='';Failure='landing sprint exit retains admitted contact slide before Tick'},
    @{Name='EarlySprintExitWitness';Path=$slide;Property='SlideSource';Find='(_wasSprinting || _isSliding || recentSprintEnd)';Replace='(_wasSprinting || recentSprintEnd)';Failure='landing sprint exit retains admitted contact slide before Tick'},
    @{Name='ReleasedSprintExit';Path=$slide;Property='SlideSource';Find='|| recentSprintEnd)';Replace=')';Failure='recent witnessed sprint release admits later transition crouch'},
    @{Name='FutureSprintRelease';Path=$slide;Property='SlideSource';Find='now >= _sprintEnded';Replace='true';Failure='backward clock cannot admit future sprint release'},
    @{Name='OldSprintExitWitness';Path=$slide;Property='SlideSource';Find='now - _lastSprintAt <= SlideWindow';Replace='true';Failure='fresh motion cannot renew stale sprint exit witness'},
    @{Name='BackwardSprintExitClock';Path=$slide;Property='SlideSource';Find='now >= _lastSprintAt';Replace='true';Failure='backward clock cannot bind sprint exit witness'},
    @{Name='NonSprintExitWitness';Path=$slide;Property='SlideSource';Find='&& _lastSprintState?.GetType() == typeof(SprintPlayerState)';Replace='';Failure='active Move slide cannot witness native sprint exit'},
    @{Name='RefreshSprintExitBinding';Path=$slide;Property='SlideSource';Find='if (_sprintExitState != null) return;';Replace='';Failure='repeated transition observers never renew binding time'},
    @{Name='LateSprintCrouch';Path=$slide;Property='SlideSource';Find='&& now - _sprintEnded <= SlideWindow && Context.PoseLevel < PoseThreshold';Replace='&& true && Context.PoseLevel < PoseThreshold';Failure='delayed crouch cannot extend original sprint release window'},
    @{Name='UnboundSprintExit';Path=$slide;Property='SlideSource';Find='private bool IsSprintExit(BaseMovementState state) => ReferenceEquals(state, _sprintExitState)';Replace='private bool IsSprintExit(BaseMovementState state) => true';Failure='replacement transition cannot borrow bound slide'},
    @{Name='StaleSprintExit';Path=$slide;Property='SlideSource';Find='Time.time - _sprintExitAt <= SlideWindow + SlideDuration + FadeOutDuration';Replace='true';Failure='stale native transition binding expires'},
    @{Name='SprintJumpHoldTooShort';Path=$slide;Property='SlideSource';Find='_landingJumpWindow = BufferedJumpWindow;';Replace='_landingJumpWindow = LandingStateWindow;';Failure='reserved sprint exit slide survives native transition longer than half second'},
    @{Name='ForeignSprintExit';Path=$slide;Property='SlideSource';Find=@("&& ReferenceEquals(Context.PreviousState, _lastSprintState)`n            && Context.CurrentState?.GetType() == typeof(SprintToIdleTransitionState)","&& ReferenceEquals(Context.PreviousState, _lastSprintState)`n        && _sprintExitAt");Replace=@('&& Context.CurrentState?.GetType() == typeof(SprintToIdleTransitionState)','&& _sprintExitAt');Failure='first transition motion rejects foreign prior sprint'},
    @{Name='ProximityGround';Path=$slide;Property='SlideSource';Find='bool contact = supported && Player.CharacterController.isGrounded;';Replace='bool contact = supported;';Failure='ground proximity alone cannot start landing slide'},
    @{Name='UpwardContact';Path=$slide;Property='SlideSource';Find='if (contact && Descending() && _contactAt < 0f)';Replace='if (contact && _contactAt < 0f)';Failure='upward physical pulse cannot start landing slide'},
    @{Name='JumpPreparationExpiry';Path=$slide;Property='SlideSource';Find='Time.time - _preparedAt > .25f';Replace='false';Failure='expired jump preparation cannot seed later entry'},
    @{Name='JumpPreviousState';Path=$slide;Property='SlideSource';Find='|| !ArenaSlideTransitions.GroundState(Context.PreviousState)';Replace='';Failure='airborne reentry never reuses slide jump momentum'},
    @{Name='NoNativeStand';Path=$slide;Property='SlideSource';Find='Context.SetPoseLevel(1f, true)';Replace='true';Failure='slide jump requests native standing pose'},
    @{Name='DeferredSlideStand';Path=$slide;Property='SlideSource';Find='Context.SetPoseLevel(1f, true)';Replace='Context.SetPoseLevel(1f, false)';Failure='slide immediate standing retains native force skill semantics'},
    @{Name='EqualPoseSync';Path=$slide;Property='SlideSource';Find='Context.SmoothedPoseLevel = Context.PoseLevel;';Replace='';Failure='already logical standing slide synchronizes native smoothed pose after headroom'},
    @{Name='EqualPoseHeadroom';Path=$slide;Property='SlideSource';Find='Context.CanStandAt(1f) && ';Replace='';Failure='equal logical pose cannot bypass current headroom for jump sync'},
    @{Name='ClampedJumpPose';Path=$slide;Property='SlideSource';Find='&& ArenaSlideTransitions.Owns(this) && Context.PoseLevel > .6f)';Replace='&& ArenaSlideTransitions.Owns(this))';Failure='clamped low pose cannot prepare slide jump'},
    @{Name='LandingJumpUpdateOrder';Path=$slide;Property='SlideSource';Find='|| (_landingJumpAt >= 0f && now >= _landingJumpAt && now - _landingJumpAt <= _landingJumpWindow)';Replace='';Failure='reserved jump survives native sprint entry before owner tick'},
    @{Name='PoseCallbackReentry';Path=$slide;Property='SlideSource';Find="if (velocity.magnitude < MinSlideSpeed || !Context.SetPoseLevel(0f, false)`n            || !ArenaSlideTransitions.Owns(this)) return;";Replace='if (velocity.magnitude < MinSlideSpeed || !Context.SetPoseLevel(0f, false)) return;';Failure='pose callback cannot commit stale slide'},
    @{Name='FadeWall';Path=$slide;Property='SlideSource';Find='if (_returning && velocity.magnitude < _returnVelocity.magnitude) _returnVelocity = velocity;';Replace='';Failure='fade cannot restore collision lost momentum'},
    @{Name='FadeNeverReturns';Path=$slide;Property='SlideSource';Find='if (t >= 1f) { _returning = false; return false; }';Replace='';Failure='slide fade returns fully to native motion'},
    @{Name='WorldIsolation';Path=$transitions;Property='TransitionsSource';Find='ReferenceEquals(_world, Singleton<GameWorld>.Instance)';Replace='true';Failure='world replacement clears slide'},
    @{Name='GameIsolation';Path=$transitions;Property='TransitionsSource';Find='ReferenceEquals(_game, Singleton<AbstractGame>.Instance)';Replace='true';Failure='game replacement clears slide'},
    @{Name='ControllerIsolation';Path=$transitions;Property='TransitionsSource';Find='ReferenceEquals(_controller, p.CharacterController)';Replace='true';Failure='character controller replacement clears slide'},
    @{Name='RebindCommit';Path=$transitions;Property='TransitionsSource';Find='|| __state.Epoch != _epoch) return;';Replace=') return;';Failure='old motion cannot commit after same component rebind'},
    @{Name='RebindFinalizer';Path=$transitions;Property='TransitionsSource';Find='if (__state.Epoch != _epoch || __state.Slide == null';Replace='if (__state.Slide == null';Failure='old motion finalizer cannot clear rebound slide'},
    @{Name='InputReplacement';Path=$transitions;Property='TransitionsSource';Find='if (_slide != null && ReferenceEquals(__instance.Player, _slide.Player)) Reset();';Replace='';Failure='input owner replacement clears slide'},
    @{Name='ForeignInput';Path=$transitions;Property='TransitionsSource';Find='if (_input != null && !ReferenceEquals(_input, __instance)) { Reset(); return; }';Replace='';Failure='translator replacement clears slide'},
    @{Name='SuppressMove';Path=$transitions;Property='TransitionsSource';Find="if (!__runOriginal || !Owns(_slide)) { Reset(); return; }`n            var velocity";Replace="if (!Owns(_slide)) { Reset(); return; }`n            var velocity";Failure='suppressed native move clears transition'},
    @{Name='ErrorCleanup';Path=$transitions;Property='TransitionsSource';Find='if (__exception != null) Reset();';Replace='';Failure='native motion exception is preserved and clears slide'}
)
$evidence=Join-Path $repo ('artifacts/movement-transitions-20261115/slide/'+[guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($evidence)
$before=@{}
foreach($path in @($slide,$transitions)) { $before[$path]=(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash }
# The runner builds ONLY this standalone linked-source project; it has no mod project reference or deploy target.
$output=Test-ArenaSourceFaults -Project $project -Token 'SLIDE_CHECKS_PASSED' -Faults $faults -RequireSingleSite
$output | Set-Content -LiteralPath (Join-Path $evidence 'checks.log') -Encoding utf8
$output | Write-Output
foreach($path in @($slide,$transitions)) {
    if((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -cne $before[$path]) {throw 'Production source changed during check'}
}
$before | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $evidence 'sources.json') -Encoding utf8
Write-Output "SLIDE_TRANSITIONS_VERIFIED ($($faults.Count) source faults; $evidence)"
