#Requires -Version 7.0
[CmdletBinding()]
param(
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '../../../ArenaMode.Client/Assets/SessionMenu'),
    [string]$PreviewDirectory,
    [string]$PythonExecutable = 'python'
)

$ErrorActionPreference = 'Stop'
$arguments = @((Join-Path $PSScriptRoot 'export_streiflicht.py'), '--output', $OutputDirectory)
if ($PreviewDirectory) { $arguments += @('--previews', $PreviewDirectory) }
& $PythonExecutable @arguments
if ($LASTEXITCODE -ne 0) { throw "Session artwork export failed with exit code $LASTEXITCODE." }
