param([string]$PublishedDll)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '../Invoke-ArenaSourceChecks.ps1')
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
if (-not $PublishedDll) { $PublishedDll = Join-Path $repo 'artifacts/optional-mod-releases/visualassist-1.2.0/VisualAssist.dll' }
if (-not (Test-Path -LiteralPath $PublishedDll)) { throw "Official VisualAssist 1.2.0 fixture missing: $PublishedDll" }
$project = Join-Path $PSScriptRoot 'VisualAssistThrowChecks.csproj'
$source = Join-Path $repo 'ArenaMode.Client/Compat/ArenaVisualAssistCompat.cs'
dotnet build (Join-Path $PSScriptRoot 'NativeCompile.csproj') -c Release --nologo -p:BaseIntermediateOutputPath=obj/native/ -p:OutputPath=bin/native/
if ($LASTEXITCODE) { throw 'VisualAssist throw actual-EFT API build failed' }
function Fault($name, $find, $replace, $failure) {
    @{ Name=$name; Path=$source; Property='CompatSource'; Find=$find; Replace=$replace; Failure=$failure }
}
$faults = @(
    (Fault 'normal-raid' 'if (!ArenaSession.IsActive) return true;' '' 'ordinary raid preserves foreign behavior'),
    (Fault 'native-result' '__result = true;' '__result = false;' 'first quick throw uses native physics'),
    (Fault 'old-owner' '!ReferenceEquals(_owner.GetValue(arc), __1)' 'false' 'stale physical owner excluded'),
    (Fault 'old-hands' '!ReferenceEquals(__1.HandsController, __0)' 'false' 'stale hands excluded'),
    (Fault 'old-world' '!ReferenceEquals(world.MainPlayer, __1)' 'false' 'foreign world player excluded'),
    (Fault 'old-game' '!ReferenceEquals(game.LocalPlayer, __1)' 'false' 'foreign game player excluded'),
    (Fault 'old-raid' '!ArenaSession.IsExactActiveRaid(game, world)' 'false' 'stale raid excluded'),
    (Fault 'low-high' '__3 != (hands.CurrentOperation is Player.GrenadeHandsController.LowThrowOperation)' 'false' 'mismatched low flag uses native physics'),
    (Fault 'stale-cache' '_prediction.SetValue(arc, prediction);' '' 'ordinary throw refreshes stale position'),
    (Fault 'nonfinite' '!Finite(position) || !Finite(force)' 'false' 'invalid position preserves native throw'),
    (Fault 'zero-force' '|| force.sqrMagnitude <= 0f' '' 'zero force preserves native throw'),
    (Fault 'no-cleanup' 'if (_prefix != null) Patches.Unpatch(_prefix, HarmonyPatchType.Prefix, Patches.Id);' '' 'owned hook removed')
)
$arguments = @($PublishedDll)
Test-ArenaSourceFaults $project 'VisualAssist throw checks passed' @() $faults -RequireSingleSite -Arguments $arguments
