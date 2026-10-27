param()
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '../Invoke-ArenaSourceChecks.ps1')
$project = Join-Path $PSScriptRoot 'RewardRecoveryUiChecks.csproj'
$source = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../ArenaMode.Client/Session/ArenaRewardRecoveryFlow.cs'))
$faults = @(
    @{ Name='implicit-waiver'; Path=$source; Property='RecoveryFlowSource';
       Find='ConfirmWithoutPayment = true'; Replace='ConfirmWithoutPayment = false';
       Failure='confirmation carries explicit waiver only' },
    @{ Name='private-approval'; Path=$source; Property='RecoveryFlowSource';
       Find='!Status.RequiresNormalSession && !Status.Queued'; Replace='!Status.Queued';
       Failure='private session cannot submit' },
    @{ Name='wrong-review'; Path=$source; Property='RecoveryFlowSource';
       Find='!CanConfirm || !string.Equals(reviewedId, _reviewId, StringComparison.Ordinal)'; Replace='!CanConfirm';
       Failure='wrong reviewed identity cannot submit' },
    @{ Name='reuse-login'; Path=$source; Property='RecoveryFlowSource';
       Find='ReferenceEquals(_session, session) && string.Equals(_profile, profile, StringComparison.Ordinal)';
       Replace='string.Equals(_profile, profile, StringComparison.Ordinal)';
       Failure='same profile new login invalidates review' },
    @{ Name='reuse-profile'; Path=$source; Property='RecoveryFlowSource';
       Find='ReferenceEquals(_session, session) && string.Equals(_profile, profile, StringComparison.Ordinal)';
       Replace='ReferenceEquals(_session, session)';
       Failure='same session new profile invalidates review' },
    @{ Name='approve-before-refresh'; Path=$source; Property='RecoveryFlowSource';
       Find='if (Busy || _session == null || string.IsNullOrEmpty(_profile)) return false;';
       Replace='if (_session == null || string.IsNullOrEmpty(_profile)) return false;';
       Failure='duplicate confirmation sends one request' }
)
Test-ArenaSourceFaults -Project $project -Token 'REWARD_RECOVERY_UI_CHECKS_PASSED' -Properties @() -Faults $faults
Write-Output 'REWARD_RECOVERY_UI_FAULTS_PASSED'
