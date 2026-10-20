param([string]$PublishedClient, [string]$NativeAssembly)
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$project = Join-Path $PSScriptRoot 'SkillsExtendedCompatChecks.csproj'
if (!$PublishedClient) { $PublishedClient = Join-Path $repo 'artifacts/candidate-audit-20261020/ai/Skills-Extended-binary/BepInEx/plugins/SkillsExtended/SkillsExtended.Client.Skills.dll' }
if (!$NativeAssembly) { $NativeAssembly = [IO.Path]::GetFullPath((Join-Path $repo '../../EscapeFromTarkov_Data/Managed/Assembly-CSharp.dll')) }
foreach ($inputFile in @($PublishedClient, $NativeAssembly)) {
    if (!(Test-Path -LiteralPath $inputFile -PathType Leaf)) { throw "Metadata input missing: $inputFile" }
}
$run = [Guid]::NewGuid().ToString('N')
$evidence = Join-Path $repo "artifacts/compat-first-20261020/skills/checks/$run"
[IO.Directory]::CreateDirectory($evidence) | Out-Null
$source = Join-Path $repo 'ArenaMode.Client/Compat'
$files = @('ArenaSkillsExtendedCompat.cs', 'ArenaSkillsExtendedContract.cs', 'ArenaSkillsExtendedScope.cs')
$original = @{}
foreach ($name in $files) { $original[$name] = [IO.File]::ReadAllText((Join-Path $source $name)) }

function Check([string]$Name, [string]$File = '', [string]$Find = '', [string]$Replace = '', [string]$Failure = '') {
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
    $log = Join-Path $copy 'result.log'
    & dotnet run --project $project -c "Skills_${run}_$Name" "-p:ProductionRoot=$copy" -- $PublishedClient $NativeAssembly *> $log
    $exitCode = $LASTEXITCODE; $output = [IO.File]::ReadAllText($log)
    if ($Failure) {
        if ($exitCode -eq 0 -or $output -match 'error (CS|MSB|NETSDK)\d+' -or !$output.Contains("ASSERT: $Failure")) {
            throw "Mutation did not compile and fail its expected assertion: $Name ($log)`n$output"
        }
        Write-Output "SKILLS_EXTENDED_MUTANT_REJECTED: $Name"
    } elseif ($exitCode -ne 0 -or $output -notmatch 'SKILLS_EXTENDED_COMPAT_CHECKS_PASSED \(\d+\)') {
        throw "Production checks failed: $log`n$output"
    } else { Write-Output $output.Trim() }
}
Check 'before'
Check 'signal-receipt-leak' 'ArenaSkillsExtendedCompat.cs' 'return !OwnsWorld(world);' 'return true;' 'Arena Signals never reserve a receipt'
Check 'body-callback-leak' 'ArenaSkillsExtendedCompat.cs' 'return ReferenceEquals(body, null) || !Scope.OwnsBody(body, body.GameWorld);' 'return true;' 'Arena callbacks suspended while other subscribers run'
Check 'medical-detach-missing' 'ArenaSkillsExtendedCompat.cs' 'body.ActiveHealthController.EffectStartedEvent -= _contract.MedicalHandler;' '' 'only foreign Arena XP handlers detached'
Check 'nato-detach-missing' 'ArenaSkillsExtendedCompat.cs' 'body.Skills.OnMasteringExperienceChanged -= _contract.NatoHandler;' '' 'only foreign Arena XP handlers detached'
Check 'eastern-detach-missing' 'ArenaSkillsExtendedCompat.cs' 'body.Skills.OnMasteringExperienceChanged -= _contract.EasternHandler;' '' 'only foreign Arena XP handlers detached'
Check 'lost-world-lease' 'ArenaSkillsExtendedScope.cs' 'if (_worlds.TryGetValue(world, out _)) return true;' 'if (_worlds.TryGetValue(world, out _)) return false;' 'only foreign Arena XP handlers detached'
$sourceLineEnd = if ($original['ArenaSkillsExtendedCompat.cs'].Contains("`r`n")) { "`r`n" } else { "`n" }
$boot = "private static void BeforeBoot(GameWorld __0)${sourceLineEnd}    { if (_contract == null) return; try { Capture(__0); } catch (Exception ex) { Fault(ex); } }"
Check 'boot-lease-missing' 'ArenaSkillsExtendedCompat.cs' $boot 'private static void BeforeBoot(GameWorld __0) { }' 'second Boot captures confirmed loading world before bots'
Check 'ordinary-world-capture' 'ArenaSkillsExtendedScope.cs' 'if (!exactCurrent || !ReferenceEquals(world, currentWorld)) return false;' 'if (!ReferenceEquals(world, currentWorld)) return false;' 'ordinary XP and receipt positive control'
Check 'wrong-world-capture' 'ArenaSkillsExtendedScope.cs' 'if (!exactCurrent || !ReferenceEquals(world, currentWorld)) return false;' 'if (!exactCurrent) return false;' 'foreign same-map world not captured'
Check 'pending-token-missing' 'ArenaSkillsExtendedScope.cs' 'Protocol.HasNativeEndIdentity(nativeIdentity, token)' 'true' 'start request marker is not confirmation'
Check 'bot-game-mismatch' 'ArenaSkillsExtendedCompat.cs' '&& ReferenceEquals(Singleton<IBotGame>.Instance, game);' '&& true;' 'wrong bot game untouched when started'
Check 'native-world-mismatch' 'ArenaSkillsExtendedCompat.cs' '!ReferenceEquals(game.GameWorld, world)' 'false' 'wrong current game world untouched'
Check 'loading-profile-mismatch' 'ArenaSkillsExtendedCompat.cs' 'ReferenceEquals(game.LocalPlayer.Profile, game.Profile)' 'true' 'loading local profile mismatch untouched'
Check 'older-version-admitted' 'ArenaSkillsExtendedContract.cs' 'version < ReviewedVersion' 'false' 'unsafe contract rejected'
Check 'unknown-body-consumer' 'ArenaSkillsExtendedContract.cs' 'member == BoundPlayer && method != Started && !callbacks.Contains(method)' 'false' 'unsafe contract rejected'
Check 'native-hook-receipt-missing' 'ArenaSkillsExtendedContract.cs' 'patches?.Postfixes.Count(p => p.PatchMethod == Started) != 1' 'false' 'missing native subscription receipt rejected'
Check 'after'
foreach ($name in $files) {
    if ([IO.File]::ReadAllText((Join-Path $source $name)) -ne $original[$name]) { throw "Production changed during verification: $name" }
}
Write-Output "SKILLS_EXTENDED_COMPAT_CHECKS_OK; 16 compiled negative controls; evidence: $evidence"
