$ErrorActionPreference = 'Stop'
$arenaRepo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$previewSource = Join-Path $arenaRepo 'ArenaMode.Client\Session\ArenaCharacterPreview.cs'
$flowSource = Join-Path $arenaRepo 'ArenaMode.Client\Session\Preview\ArenaPreviewFlow.cs'
$stateSource = Join-Path $arenaRepo 'ArenaMode.Client\Session\Preview\ArenaPreviewLifetimeState.cs'
$project = Join-Path $PSScriptRoot 'PreviewHostLifecycleChecks.csproj'
$scratch = Join-Path $arenaRepo ('artifacts\preview-host-lifecycle\' + [guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($scratch) | Out-Null
$preview = [IO.File]::ReadAllText($previewSource)

function Get-Method([string]$source, [string]$signature) {
    $begin = $source.IndexOf($signature, [StringComparison]::Ordinal)
    if ($begin -lt 0 -or $source.IndexOf($signature, $begin + $signature.Length, [StringComparison]::Ordinal) -ge 0) {
        throw ('Expected exactly one production method: ' + $signature)
    }
    $brace = $source.IndexOf('{', $begin)
    if ($brace -lt 0) { throw ('Method has no opening brace: ' + $signature) }
    $depth = 0
    for ($cursor = $brace; $cursor -lt $source.Length; ++$cursor) {
        if ($source[$cursor] -eq '{') { ++$depth }
        if ($source[$cursor] -eq '}') { --$depth; if ($depth -eq 0) { return $source.Substring($begin, $cursor - $begin + 1) } }
    }
    throw ('Unbalanced production method: ' + $signature)
}

$availability = [regex]::Matches($preview, 'public bool IsAvailable\s*=>[^;]+;')
if ($availability.Count -ne 1) { throw 'Expected one expression-bodied IsAvailable property.' }
$members = @(
    $availability[0].Value,
    (Get-Method $preview 'public void Suspend()'),
    (Get-Method $preview 'public void HoldForFlow('),
    (Get-Method $preview 'public void SetFlowGuard(')
)
$adapterText = @'
// Generated verbatim production members. Constructor, loader/renderer and release
// remain controlled boundaries in BoundaryDoubles.cs, not native GPU execution.
using System;
using UnityEngine;
using Object = UnityEngine.Object;
using ArenaMode.Session.Preview;
namespace ArenaMode.Session;
internal sealed partial class ArenaCharacterPreview {
'@ + "`n" + ($members -join "`n") + "`n}`n"
$adapterSource = Join-Path $scratch 'PreviewAdapter.cs'
[IO.File]::WriteAllText($adapterSource, $adapterText)
$sourceHashes = [ordered]@{}
foreach ($path in @($previewSource, $flowSource, $stateSource)) { $sourceHashes[$path] = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash }
$sourceHashes | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $scratch 'source-hashes.json')

function Invoke-Case([string]$name, [string]$flowPath, [string]$adapterPath) {
    $caseRoot = Join-Path $scratch $name
    $output = & dotnet run --project $project -c Release "-p:PreviewFlowSource=$flowPath" "-p:PreviewAdapterSource=$adapterPath" "-p:BaseIntermediateOutputPath=$caseRoot\obj\" "-p:BaseOutputPath=$caseRoot\bin\" 2>&1
    $exitCode = $LASTEXITCODE
    $text = $output -join "`n"
    [IO.File]::WriteAllText((Join-Path $scratch ($name + '.log')), $text)
    return @{ Exit = $exitCode; Text = $text }
}
$baseline = Invoke-Case 'baseline' $flowSource $adapterSource
Write-Output $baseline.Text
if ($baseline.Exit -ne 0) { throw ('Production preview lifecycle baseline failed. Evidence: ' + $scratch) }

$flow = [IO.File]::ReadAllText($flowSource)
$attachMethod = Get-Method $flow 'private static bool TryAttach('
$attachFlush = [regex]::Matches($attachMethod, 'FlushPendingHide\([^;]*\);')
if ($attachFlush.Count -ne 1) { throw 'Expected exactly one pending-hide drain inside TryAttach.' }
$queueMethod = Get-Method $flow 'private static void QueueHostHide('
$queueAssignment = [regex]::Matches($queueMethod, '_pendingHostHide\s*=\s*[^;]+;')
if ($queueAssignment.Count -ne 1) { throw 'Expected exactly one pending-hide assignment inside QueueHostHide.' }
$mutations = @(
    @{ Name = 'inline-host-callback'; Source = $flow; Target = 'flow'; Old = $queueAssignment[0].Value; New = 'SuspendHost(host, generation, false);' },
    @{ Name = 'skip-pre-attach-flush'; Source = $flow; Target = 'flow'; Old = $attachMethod; New = $attachMethod.Replace($attachFlush[0].Value, '') },
    @{ Name = 'erase-current-native-request'; Source = $flow; Target = 'flow'; Old = $attachMethod; New = $attachMethod.Replace($attachFlush[0].Value, 'FlushPendingHide();') },
    @{ Name = 'allow-reentrant-close'; Source = $flow; Target = 'flow'; Old = 'if (screen == null || !screen.gameObject.activeInHierarchy) return;'; New = 'if (screen == null) return;' },
    @{ Name = 'missing-native-object-availability'; Source = $adapterText; Target = 'adapter'; Old = $availability[0].Value; New = 'public bool IsAvailable => !_lifetime.IsDisposed;' }
)
$rejected = 0
foreach ($mutation in $mutations) {
    # Normalize only line endings in isolated generated input for portable anchor matching.
    $source = $mutation.Source.Replace("`r`n", "`n")
    $old = $mutation.Old.Replace("`r`n", "`n")
    if (-not $source.Contains($old)) { throw ('Mutation anchor missing: ' + $mutation.Name) }
    if ($source.IndexOf($old) -ne $source.LastIndexOf($old)) { throw ('Ambiguous mutation anchor: ' + $mutation.Name) }
    $mutantPath = Join-Path $scratch ($mutation.Name + '.cs')
    [IO.File]::WriteAllText($mutantPath, $source.Replace($old, $mutation.New))
    $flowPath = if ($mutation.Target -eq 'flow') { $mutantPath } else { $flowSource }
    $adapterPath = if ($mutation.Target -eq 'adapter') { $mutantPath } else { $adapterSource }
    $result = Invoke-Case $mutation.Name $flowPath $adapterPath
    if ($result.Exit -eq 0) { throw ('Compiled defect survived: ' + $mutation.Name) }
    if ($result.Text -notmatch 'ASSERTION_FAILED: System.InvalidOperationException:') { throw ('Defect did not fail a runtime assertion: ' + $mutation.Name + "`n" + $result.Text) }
    ++$rejected
    Write-Output ('Rejected compiled production defect: ' + $mutation.Name)
}
foreach ($path in $sourceHashes.Keys) {
    if ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $sourceHashes[$path]) { throw ('Production input changed during verification: ' + $path) }
}
Write-Output ("PREVIEW_HOST_NEGATIVE_CONTROLS_PASSED: $rejected rejected")
Write-Output ('Evidence: ' + $scratch)
