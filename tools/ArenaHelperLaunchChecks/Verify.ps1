param()
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$run = Join-Path $repo ('artifacts/helper-launch-checks/' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($run) | Out-Null
$source = Join-Path $repo 'ArenaMode.Client/Session/ArenaHelperProcess.cs'
$text = [IO.File]::ReadAllText($source)
$anchor = 'CreateProcessW(executable, commandLine, IntPtr.Zero, IntPtr.Zero, false,'
if ([regex]::Matches($text, [regex]::Escape($anchor)).Count -ne 1) { throw 'Mutation anchor missing or ambiguous' }
$mutant = Join-Path $run 'inherited.cs'
[IO.File]::WriteAllText($mutant, $text.Replace($anchor, $anchor.Replace('false,', 'true,')))
foreach ($case in @(@{ Name='production'; Source=$source }, @{ Name='inherits-handles'; Source=$mutant })) {
    $output = Join-Path $run ($case.Name + '/')
    & dotnet build (Join-Path $PSScriptRoot 'ArenaHelperLaunchChecks.csproj') -c Release --nologo `
        "-p:LauncherSource=$($case.Source)" "-p:OutputPath=$output" `
        "-p:IntermediateOutputPath=$(Join-Path $run ($case.Name + '-obj/'))" `
        "-p:CustomAfterMicrosoftCommonTargets=$(Join-Path $repo 'tools/AirPitChecks/NoDeploy.targets')" `
        "-p:SPTUserMods=$(Join-Path $run 'must-not-exist')" *> (Join-Path $run ($case.Name + '-build.log'))
    if ($LASTEXITCODE -ne 0) { throw "Build failed: $run" }
    $lines = @(& (Join-Path $output 'ArenaHelperLaunchChecks.exe') 2>&1 | ForEach-Object { "$_" })
    $code = $LASTEXITCODE
    $lines | Set-Content -LiteralPath (Join-Path $run ($case.Name + '.log'))
    if ($case.Name -eq 'production') {
        if ($code -ne 0 -or $lines -notcontains 'PASS no-inherited-pipe') { throw "Production failed: $run" }
        $lines
    } elseif ($code -ne 1 -or $lines -notcontains 'FAIL no-inherited-pipe' -or $lines -notcontains 'PASS exact-arguments-unicode-and-spaces') {
        throw "Inheritance regression was not specifically detected: $run"
    } else { Write-Output 'PASS inherited-handle regression detected' }
}
Write-Output "ARENA_HELPER_LAUNCH_CHECKS_PASSED: $run"
