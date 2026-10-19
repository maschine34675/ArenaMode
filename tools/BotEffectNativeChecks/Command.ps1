[CmdletBinding(DefaultParameterSetName='Send')]
param(
    [Parameter(Mandatory, ParameterSetName='Send')]
    [ValidateSet('status','observe','buff','cover','clear','sequence')][string]$Action,
    [Parameter(Mandatory, ParameterSetName='Send')]
    [ValidatePattern('^[0-9a-f]{24}$')][string]$ProfileId,
    [Parameter(ParameterSetName='Send')][string]$BotId,
    [Parameter(ParameterSetName='Send')][ValidateSet('shield','damage','both')][string]$Effect,
    [Parameter(ParameterSetName='Send')][ValidateSet('none','full','lower-half')][string]$Cover,
    [Parameter(ParameterSetName='Send')][ValidateRange(0.1,15)][double]$Seconds = 10,
    [Parameter(Mandatory, ParameterSetName='Read')]
    [ValidatePattern('^[0-9a-f]{32}$')][string]$ReadId,
    [ValidateSet('SPT-GPU-2','192.168.178.109')][string]$GpuHost = 'SPT-GPU-2',
    [Parameter(ParameterSetName='Read')][string]$SaveAs
)
$ErrorActionPreference = 'Stop'
$fixtureRoot = "\\$GpuHost\C`$\Temp\ArenaBotEffectNativeChecks"
if (-not (Test-Path -LiteralPath $fixtureRoot -PathType Container)) {
    throw 'Fixture folder absent; parent must review/install the temporary plugin and start EFT first.'
}
if ($PSCmdlet.ParameterSetName -eq 'Read') {
    $resultPath = Join-Path $fixtureRoot "$ReadId.json"
    if (-not (Test-Path -LiteralPath $resultPath -PathType Leaf)) {
        throw 'Result not present yet. Use UiBridge wait, then this separate read action.'
    }
    $resultText = Get-Content -LiteralPath $resultPath -Raw
    if ($SaveAs) { [IO.File]::WriteAllText([IO.Path]::GetFullPath($SaveAs), $resultText) }
    $resultText
    if (-not ($resultText | ConvertFrom-Json).Ok) { exit 1 }
    exit 0
}
if ($Action -in @('observe','buff','cover','sequence') -and [string]::IsNullOrWhiteSpace($BotId)) { throw 'BotId is required.' }
if ($Action -eq 'buff' -and -not $Effect) { throw 'Effect is required for buff.' }
if ($Action -eq 'cover' -and -not $Cover) { throw 'Cover is required for cover.' }
$requestPath = Join-Path $fixtureRoot 'request.json'
if (Test-Path -LiteralPath $requestPath) { throw 'A command is pending; do not overwrite another command.' }
$requestId = [Guid]::NewGuid().ToString('N')
$request = [ordered]@{
    Id=$requestId; CreatedUtc=[DateTime]::UtcNow.ToString('o'); Action=$Action;
    ProfileId=$ProfileId; BotId=$BotId; Effect=$Effect; Cover=$Cover; Seconds=$Seconds
}
$temporary = Join-Path $fixtureRoot "$requestId.pending"
[IO.File]::WriteAllText($temporary, ($request | ConvertTo-Json -Compress))
Move-Item -LiteralPath $temporary -Destination $requestPath
Write-Output $requestId
