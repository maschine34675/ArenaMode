param([string[]]$PublishedAssembly)
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$project = Join-Path $PSScriptRoot 'LootingBotsCompatChecks.csproj'
$scope = Join-Path $repo 'ArenaMode.Client/Compat/ArenaLootingBotsScope.cs'
$compat = Join-Path $repo 'ArenaMode.Client/Compat/ArenaLootingBotsCompat.cs'
$run = [Guid]::NewGuid().ToString('N')
$evidence = Join-Path $repo "artifacts/looting-bots-checks/$run"
[IO.Directory]::CreateDirectory($evidence) | Out-Null
if (!$PublishedAssembly) {
    # Optional binary metadata; never download/install third-party mods here.
    $PublishedAssembly = @('published','published-rc') | ForEach-Object {
        $path = Join-Path $repo "artifacts/compat-audit-20261019b/looting-bots/$_/BepInEx/plugins/Skwizzy.LootingBots/Skwizzy.LootingBots.dll"
        if (Test-Path -LiteralPath $path -PathType Leaf) { $path }
    }
}
foreach ($path in $PublishedAssembly) { if (!(Test-Path -LiteralPath $path -PathType Leaf)) { throw "PublishedAssembly missing: $path" } }
function Check([string]$Name, [string]$ScopeFile, [string]$CompatFile, [string]$Failure = '') {
    $log = Join-Path $evidence "$Name.log"
    $arguments = @('run','--project',$project,'-c',"Looting_${run}_$Name", "-p:ScopeSource=$ScopeFile", "-p:CompatSource=$CompatFile")
    if ($PublishedAssembly) { $arguments += @('--') + $PublishedAssembly }
    & dotnet @arguments *> $log
    $code = $LASTEXITCODE
    $output = [IO.File]::ReadAllText($log)
    if ($Failure) {
        if ($code -eq 0 -or $output -match 'error (CS|MSB|NETSDK)\d+' -or !$output.Contains("ASSERT: $Failure")) {
            throw "Mutant did not compile and fail intended assertion: $Name ($log)`n$output"
        }
        Write-Output "Rejected compiled mutant: $Name"
    } elseif ($code -ne 0 -or $output -notmatch 'LOOTING_BOTS_CHECKS_PASSED \(\d+\)') { throw "Production checks failed ($log)`n$output" }
    else { Write-Output $output.Trim() }
}
Check 'before' $scope $compat
$mutants = @(
    @{ Name='ordinary'; Source='scope'; Find='if (active) return true;'; Replace='return true;'; Failure='ordinary PMC' },
    @{ Name='request-marker'; Source='scope'; Find='&& Protocol.HasNativeEndIdentity(nativeId, token)'; Replace='&& true'; Failure='request only marker' },
    @{ Name='early-loading'; Source='compat'; Find='game.Status == GameStatus.Running ||'; Replace='false ||'; Failure='confirmed loading before Active' },
    @{ Name='foreign-world'; Source='compat'; Find='!ReferenceEquals(game.GameWorld, world)'; Replace='false'; Failure='foreign same-map world' },
    @{ Name='stale-body'; Source='compat'; Find='ReferenceEquals(world.GetEverExistedPlayerByID(player.ProfileId), player)'; Replace='true'; Failure='unregistered body' },
    @{ Name='pft-lost'; Source='scope'; Find='return !commandable;'; Replace='return true;'; Failure='PFT loading companion' },
    @{ Name='tdm-exempt'; Source='compat'; Find='pftTeam, companion, mode);'; Replace='pftTeam || mode == "teamdeathmatch", companion || mode == "teamdeathmatch", mode == "teamdeathmatch" ? "tactical" : mode);'; Failure='TDM both sides despite stale companion id' },
    @{ Name='direct-search'; Source='compat'; Find='if (!Suppress(__instance, _contract?.FinderOwner)) return true;'; Replace='return true;'; Failure='confirmed loading before Active acquired scan ticket returned exactly once' },
    @{ Name='ticket-leak'; Source='compat'; Find='_contract.ReturnTicket(__0);'; Replace='/* ticket leaked */'; Failure='confirmed loading before Active acquired scan ticket returned exactly once' },
    @{ Name='corpse-retention'; Source='compat'; Find='=> !Suppress(__instance, _contract?.FinderOwner);'; Replace='=> true;'; Failure='confirmed loading before Active mutation entries' },
    @{ Name='rc-inventory'; Source='compat'; Find='if (!Suppress(__instance, _contract?.InventoryOwner, preservePftCommands: false)) return true;'; Replace='return true;'; Failure='confirmed loading before Active mutation entries' },
    @{ Name='pft-spawn-gear'; Source='compat'; Find='preservePftCommands: false'; Replace='preservePftCommands: true'; Failure='PFT loading companion mutation entries' }
)
try {
    foreach ($mutant in $mutants) {
        $source = if ($mutant.Source -eq 'scope') { $scope } else { $compat }
        $text = [IO.File]::ReadAllText($source)
        if ([regex]::Matches($text, [regex]::Escape($mutant.Find)).Count -ne 1) { throw "Ambiguous mutation: $($mutant.Name)" }
        $file = Join-Path $evidence ($mutant.Name + '.cs')
        [IO.File]::WriteAllText($file, $text.Replace($mutant.Find, $mutant.Replace))
        $scopeFile = if ($mutant.Source -eq 'scope') { $file } else { $scope }
        $compatFile = if ($mutant.Source -eq 'compat') { $file } else { $compat }
        Check $mutant.Name $scopeFile $compatFile $mutant.Failure
    }
} finally { Check 'after' $scope $compat }
Write-Output "Evidence: $evidence; compiled mutants: $($mutants.Count); published metadata inputs: $(@($PublishedAssembly).Count)"
Write-Output 'LOOTING_BOTS_COMPAT_VERIFIED'
