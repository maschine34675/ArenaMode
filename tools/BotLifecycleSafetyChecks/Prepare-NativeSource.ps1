param([string]$SourceDirectory, [string]$OutputPath)
$ErrorActionPreference = 'Stop'
$source = [IO.File]::ReadAllText((Join-Path $SourceDirectory 'BotEnemiesController.cs'))
# The decompiler emits a generated field with its enclosing type's name, which
# C# rejects. Only rename that unused generated field, never a native algorithm.
$anchor = 'public static readonly CG_Remove CG_Remove = new CG_Remove();'
if ([regex]::Matches($source, [regex]::Escape($anchor)).Count -ne 1) { throw 'Native compiler-generated name anchor changed' }
$source = $source.Replace($anchor, 'public static readonly CG_Remove Instance = new CG_Remove();')
$destination = [IO.Path]::GetFullPath($OutputPath)
[void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($destination))
[IO.File]::WriteAllText($destination, $source)
