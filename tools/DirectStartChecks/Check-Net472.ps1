param([Parameter(Mandatory)][string]$Exe)
$ErrorActionPreference = 'Stop'
if ([Runtime.InteropServices.RuntimeInformation]::FrameworkDescription -notlike '.NET Framework*') { throw 'Run with Windows PowerShell.' }
$assembly = [Reflection.Assembly]::LoadFrom($Exe)
$flags = [Reflection.BindingFlags]'Public,NonPublic,Static'
$direct = $assembly.GetType('ArenaMode.SessionHelper.HelperDirectStart', $true)
$ui = $assembly.GetType('ArenaMode.SessionHelper.HelperStartWindow', $true)
$request = $assembly.GetType('ArenaMode.SessionHelper.HelperRequest', $true)
$folder = Join-Path ([IO.Path]::GetTempPath()) ('arena-direct-check-' + [guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($folder) | Out-Null
function Assert([bool]$Value, [string]$Name) { if (-not $Value) { throw "FAIL $Name" }; Write-Output "PASS $Name" }
try {
    $id = '6ab7db1164f04b565cf78be7'
    $profile = '{"info":{"id":"'+$id+'","wipe":true},"characters":{"pmc":{"Info":{"Nickname":"Test","Side":"Usec","Level":4}}}}'
    [IO.File]::WriteAllText((Join-Path $folder ($id+'.json')), $profile)
    $list = $direct.GetMethod('ReadProfiles',$flags).Invoke($null, [object[]]@([string]$folder))
    Assert ($list.Count -eq 0) 'wiped-profile-hidden'
    [IO.File]::WriteAllText((Join-Path $folder ($id+'.json')), $profile.Replace('"wipe":true','"wipe":false'))
    $list = $direct.GetMethod('ReadProfiles',$flags).Invoke($null, [object[]]@([string]$folder))
    Assert ($list.Count -eq 1) 'initialized-profile-visible'
    $arguments = [object[]]@('{"schema":3,"direction":"enter","gamePid":0}', 'enter', $null, $null)
    $accepted = $request.GetMethod('TryParse',$flags).Invoke($null, $arguments)
    Assert (-not $accepted -and $arguments[3] -like '*gamePid*') 'wire-needs-old-game'
    $configDir = Join-Path $folder 'SPT_Data\configs'
    [IO.Directory]::CreateDirectory($configDir) | Out-Null
    [IO.File]::WriteAllText((Join-Path $configDir 'http.json'), '{"ip":"127.0.0.2","port":6969,"backendPort":6969}')
    $refused = $false
    try { $null = $direct.GetMethod('ReadEndpoint',$flags).Invoke($null,[object[]]@([string]$folder)) } catch { $refused = $true }
    Assert $refused 'other-loopback-refused'
    [IO.File]::WriteAllText((Join-Path $configDir 'http.json'), '{"ip":"127.0.0.1","port":6969,"backendPort":6969}')
    $endpoint = $direct.GetMethod('ReadEndpoint',$flags).Invoke($null,[object[]]@([string]$folder))
    Assert ($endpoint.ServerHost -eq '127.0.0.1') 'supported-loopback-accepted'
    $shell = New-Object -ComObject WScript.Shell
    $foreign = $shell.CreateShortcut((Join-Path $folder 'ArenaMode.lnk'))
    $foreign.TargetPath = Join-Path $env:SystemRoot 'notepad.exe'; $foreign.Save()
    $before = (Get-FileHash (Join-Path $folder 'ArenaMode.lnk')).Hash
    $path = $ui.GetMethod('CreateShortcut',$flags).Invoke($null,[object[]]@([string]$Exe, [string]$folder, [string]$folder))
    $link = $shell.CreateShortcut($path)
    Assert ($link.TargetPath -eq $Exe -and $link.Arguments -eq 'start' -and $link.WorkingDirectory -eq $folder) 'shortcut-target-without-token'
    Assert ((Get-FileHash (Join-Path $folder 'ArenaMode.lnk')).Hash -eq $before) 'foreign-shortcut-preserved'
    $again = $ui.GetMethod('CreateShortcut',$flags).Invoke($null,[object[]]@([string]$Exe, [string]$folder, [string]$folder))
    Assert ($again -eq $path) 'own-shortcut-reused'
    foreach ($com in @($link,$foreign,$shell)) { [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($com) }
    Write-Output 'DIRECT_START_FRAMEWORK_PASSED'
} finally {
    # This exact unique temp folder was created above; never follow an external target.
    if ([IO.Path]::GetFullPath($folder).StartsWith([IO.Path]::GetTempPath(),[StringComparison]::OrdinalIgnoreCase)) {
        Remove-Item -LiteralPath $folder -Recurse -Force
    }
}
