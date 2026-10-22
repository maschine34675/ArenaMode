# Runs inside Windows PowerShell 5.1 (the CLR of .NET Framework 4.x, the runtime of the Arena session helper) and
# calls the helper's socket code through reflection on the built exe. Started by Verify.ps1, which says why.
#
#   powershell.exe -NoProfile -File Check-HelperNet472.ps1 -Exe <path to maschine-ArenaMode.Session.exe>
#
# One line per check ("PASS <name>" or "FAIL <name>: ..."), then "RESULT: PASS" or "RESULT: FAIL". Exit code 0 only on PASS.
param([Parameter(Mandatory = $true)][string]$Exe)

$ErrorActionPreference = 'Stop'
$framework = [Runtime.InteropServices.RuntimeInformation]::FrameworkDescription
if ($framework -notlike '.NET Framework*') { Write-Output "RESULT: FAIL (this is '$framework', not .NET Framework: run it with powershell.exe, not pwsh)"; exit 2 }
if (-not [Net.Sockets.Socket]::OSSupportsIPv6) { Write-Output 'RESULT: FAIL (no IPv6 on this machine, the IPv6 checks cannot run)'; exit 2 }

$assembly = [Reflection.Assembly]::LoadFrom($Exe)
$type = $assembly.GetType('ArenaMode.SessionHelper.HelperProcesses', $true)
$flags = [Reflection.BindingFlags]'Public,NonPublic,Static'
$canConnect = $type.GetMethod('CanConnect', $flags, $null, [Type[]]@([string], [int], [int], [Net.IPAddress].MakeByRefType()), $null)
$lookUp = $type.GetMethod('GetListenerProcessIds', $flags, $null, [Type[]]@([int], [Net.IPAddress]), $null)
if (-not $canConnect -or -not $lookUp) { Write-Output 'RESULT: FAIL (HelperProcesses.CanConnect / GetListenerProcessIds not found)'; exit 2 }

$failed = 0
function Report([string]$Name, [bool]$Ok, [string]$Detail) {
    if ($Ok) { Write-Output "PASS $Name" } else { $script:failed++; Write-Output "FAIL ${Name}: $Detail" }
}

# [connected, reached address as text]
function Connect([string]$HostName, [int]$Port) {
    $arguments = [object[]]@($HostName, $Port, 2000, $null)
    $connected = [bool]$canConnect.Invoke($null, $arguments)
    $reached = if ($arguments[3]) { ([Net.IPAddress]$arguments[3]).ToString() } else { '<none>' }
    return @($connected, $reached)
}

function Owns([int]$Port, [Net.IPAddress]$Address) {
    $owners = $lookUp.Invoke($null, [object[]]@($Port, $Address))
    return ($null -ne $owners) -and ($owners -contains $PID)
}

function Listen([Net.IPAddress]$Address, [bool]$DualMode) {
    $listener = New-Object Net.Sockets.TcpListener($Address, 0)
    if ($DualMode) { $listener.Server.DualMode = $true }
    $listener.Start()
    return $listener
}

$ipv4 = Listen ([Net.IPAddress]::Loopback) $false
try {
    $port = $ipv4.LocalEndpoint.Port
    $result = Connect '127.0.0.1' $port
    Report 'ipv4-loopback-connect' ($result[0] -and $result[1] -eq '127.0.0.1') "connected=$($result[0]) reached=$($result[1])"
    $result = Connect 'localhost' $port
    Report 'host-name-connect' $result[0] "connected=$($result[0]) reached=$($result[1])"
} finally { $ipv4.Stop() }

$closed = Connect '127.0.0.1' $port
Report 'closed-port' (-not $closed[0] -and $closed[1] -eq '<none>') "connected=$($closed[0]) reached=$($closed[1])"

$ipv6 = Listen ([Net.IPAddress]::IPv6Loopback) $false
try {
    $port = $ipv6.LocalEndpoint.Port
    $result = Connect '::1' $port
    Report 'ipv6-literal-connect' ($result[0] -and $result[1] -eq '::1') "connected=$($result[0]) reached=$($result[1])"
    Report 'ipv6-owner' (Owns $port ([Net.IPAddress]::IPv6Loopback)) 'the IPv6 listener of this process is not found for ::1'
} finally { $ipv6.Stop() }

$wildcard = Listen ([Net.IPAddress]::Any) $false
try {
    $port = $wildcard.LocalEndpoint.Port
    $result = Connect '127.0.0.1' $port
    Report 'ipv4-wildcard-owner' ($result[0] -and (Owns $port ([Net.IPAddress]$result[1]))) "connected=$($result[0]) reached=$($result[1]), listener of 0.0.0.0 not found"
} finally { $wildcard.Stop() }

$dual = Listen ([Net.IPAddress]::IPv6Any) $true
try {
    $port = $dual.LocalEndpoint.Port
    $result = Connect '127.0.0.1' $port
    Report 'dual-stack-wildcard-owner' ($result[0] -and (Owns $port ([Net.IPAddress]$result[1]))) "connected=$($result[0]) reached=$($result[1]), listener of [::] not found for an IPv4 connection"
} finally { $dual.Stop() }

if ($failed -eq 0) { Write-Output 'RESULT: PASS'; exit 0 }
Write-Output 'RESULT: FAIL'
exit 1
