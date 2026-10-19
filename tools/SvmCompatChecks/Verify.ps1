param([string]$PublishedAssembly)
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$project = Join-Path $PSScriptRoot 'SvmCompatChecks.csproj'
$serverProject = Join-Path $repo 'ArenaMode.Server/ArenaMode.Server.csproj'
$source = Join-Path $repo 'ArenaMode.Server/Compat'
if (!$PublishedAssembly) {
    $PublishedAssembly = Join-Path $repo 'artifacts/ammo-raid-health-compat-20261019/releases/svm/extracted/SPT_Runtime/user/mods/[SVM] Server Value Modifier/ServerValueModifier.dll'
}
if (!(Test-Path -LiteralPath $PublishedAssembly -PathType Leaf)) { throw 'Supply the separately downloaded published SVM 2.2.3 DLL; no test mod is installed.' }
$run = [Guid]::NewGuid().ToString('N')
$evidence = Join-Path $repo "artifacts/svm-compat-checks/$run"
[IO.Directory]::CreateDirectory($evidence) | Out-Null
$files = @('ArenaSvmCompat.cs', 'ArenaSvmContract.cs')
$original = @{}
foreach ($name in $files) { $original[$name] = [IO.File]::ReadAllText((Join-Path $source $name)) }
$faults = 0
function Check([string]$Name, [string]$File = '', [string]$Find = '', [string]$Replace = '', [string]$Failure = '', [string]$Mode = '') {
    $copy = Join-Path $evidence $Name
    [IO.Directory]::CreateDirectory($copy) | Out-Null
    foreach ($sourceName in $files) {
        $content = $original[$sourceName]
        if ($sourceName -eq $File) {
            if ([regex]::Matches($content, [regex]::Escape($Find)).Count -ne 1) { throw "Ambiguous mutation: $Name" }
            $content = $content.Replace($Find, $Replace)
        }
        [IO.File]::WriteAllText((Join-Path $copy $sourceName), $content)
    }
    $buildLog = Join-Path $copy 'build.log'
    & dotnet build $serverProject -c Release --no-restore "-p:CustomAfterMicrosoftCommonTargets=$PSScriptRoot/Production.targets" "-p:SvmProductionRoot=$copy" "-p:OutputPath=$copy/server/" "-p:IntermediateOutputPath=$copy/obj/" "-p:ProjectAssetsFile=$repo/ArenaMode.Server/obj/project.assets.json" *> $buildLog
    if ($LASTEXITCODE -ne 0) { throw "Server fixture build failed: $buildLog`n$([IO.File]::ReadAllText($buildLog))" }
    $log = Join-Path $copy 'result.log'
    $arguments = @('run', '--no-restore', '--project', $project, '-c', "Svm_${run}_$Name", "-p:ServerAssemblyPath=$copy/server/maschine-ArenaMode.Server.dll", '--', $PublishedAssembly)
    if ($Mode) { $arguments += $Mode }
    & dotnet @arguments *> $log
    $exitCode = $LASTEXITCODE; $output = [IO.File]::ReadAllText($log)
    if ($Failure) {
        $intended = @($Failure -split '\|' | Where-Object { $output.Contains("ASSERT: $_") }).Count -gt 0
        if ($exitCode -eq 0 -or $output -match 'error (CS|MSB|NETSDK)\d+' -or !$intended) {
            throw "Fault did not compile and fail intended assertion: $Name ($log)`n$output"
        }
        $script:faults++; Write-Output "SVM_MUTANT_REJECTED: $Name"
    } elseif ($exitCode -ne 0 -or $output -notmatch 'SVM_(COMPAT|FALLBACK)_PASSED') {
        throw "Production checks failed: $log`n$output"
    } else { Write-Output $output.Trim() }
}
Check 'before'
Check 'absent' -Mode 'absent'
Check 'unsupported' -Mode 'unsupported'
Check 'ordinary-leak' 'ArenaSvmCompat.cs' 'if (!_current!.OrdinaryWrapperOwned && !NeedsProtectedEnd(__1, __2)) return true;' '' 'ordinary SaveLoot retains foreign behavior'
Check 'lost-protected-guard' 'ArenaSvmCompat.cs' 'if (!_current!.OrdinaryWrapperOwned && !NeedsProtectedEnd(__1, __2)) return true;' 'if (!_current!.OrdinaryWrapperOwned) return true;' 'protected Arena bypasses foreign config and mutation'
Check 'lost-wrapper-deferral' 'ArenaSvmCompat.cs' 'if (!_current!.OrdinaryWrapperOwned && !NeedsProtectedEnd(__1, __2)) return true;' 'if (!NeedsProtectedEnd(__1, __2)) return true;' 'ordinary wrapper runs SVM exactly once|SVM ignored end retires ordinary generation'
Check 'wrong-native-veto' 'ArenaSvmCompat.cs' '__result = true;' '__result = false;' 'protected Arena restores and completes'
Check 'request-identity' 'ArenaSvmCompat.cs' 'ReferenceEquals(_current.Request, request)' 'true' 'same-ID physical request cannot borrow lease'
Check 'session-identity' 'ArenaSvmCompat.cs' 'string.Equals(_current.Session, session.ToString(), StringComparison.Ordinal)' 'true' 'other session cannot borrow callback lease'
Check 'route-identity' 'ArenaSvmCompat.cs' 'string.Equals(_current.Url, EndRoute, StringComparison.Ordinal)' 'true' 'non-end route does not borrow Arena scope'
Check 'instance-identity' 'ArenaSvmCompat.cs' 'ReferenceEquals(_current!.Instance, instance)' 'true' 'other instance cannot mark wrapper ownership'
Check 'lease-cleanup' 'ArenaSvmCompat.cs' 'if (__state != null && ReferenceEquals(_current, __state)) _current = __state.Previous;' '' 'direct foreign call remains ordinary|nested callback restores outer lease'
Check 'effective-order' 'ArenaSvmCompat.cs' 'if (!(capture >= 0 && capture < wrapper && wrapper < foreign))' 'if (false)' 'contradictory foreign ordering rejects admission'
Check 'late-topology' 'ArenaSvmCompat.cs' 'try { ValidateInstalled(); return true; }' 'try { return true; }' 'ordinary end detects late topology without Arena admission poll'
Check 'frozen-retry-capture' 'ArenaSvmCompat.cs' '&& ArenaContext.OrdinaryRaids?.OwnsCapturedCallbackBridge(__2, __1.ServerId) == true)' '&& false)' 'captured completed retry suppresses SVM after original order restore'
Check 'downstream-hook' 'ArenaSvmContract.cs' 'else if (foreign.Length != 0)' 'else if (false)' 'unreviewed downstream foreign hook rejects admission'
Check 'unknown-outer-hook' 'ArenaSvmContract.cs' '|| info.Prefixes.Any(p => p.PatchMethod != ForeignPrefix' '|| false && info.Prefixes.Any(p => p.PatchMethod != ForeignPrefix' 'unavailable SVM does not enable callback bridge' 'unsupported'
Check 'after'
foreach ($name in $files) { if ([IO.File]::ReadAllText((Join-Path $source $name)) -ne $original[$name]) { throw "Production source changed: $name" } }
Write-Output "SVM_COMPAT_CHECKS_OK; $faults compiled negative controls; evidence: $evidence"
