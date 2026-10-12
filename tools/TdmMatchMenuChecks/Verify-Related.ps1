$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
foreach ($project in @('DeathmatchMenuChecks', 'AreaTeamMenuChecks', 'ParticipantMenuChecks', 'AreaMapPreviewChecks')) {
    & dotnet run --project (Join-Path $repo ('tools/' + $project)) -c Release
    if ($LASTEXITCODE -ne 0) { throw "Related menu check failed: $project" }
}
Write-Output 'ARENA_MENU_RELATED_CHECKS_PASSED'
