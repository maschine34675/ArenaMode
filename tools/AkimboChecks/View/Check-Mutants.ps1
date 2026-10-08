param()
$ErrorActionPreference = 'Stop'
$viewProject = Join-Path $PSScriptRoot 'ViewChecks.csproj'
$viewSource = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../../ArenaMode.Client/Combat/Akimbo/AkimboView.cs'))
$original = [IO.File]::ReadAllText($viewSource)
$scratch = Join-Path $PSScriptRoot 'obj/adversarial-controls'
[IO.Directory]::CreateDirectory($scratch) | Out-Null
$mutations = @(
    @{ Name = 'include-pooled-ammo'; Needle = 'if (child.GetComponent<AmmoPoolObject>() != null) continue;'; Replacement = '/* negative control: include pooled ammo */'; Failure = 'omit dynamic ammunition subtree' },
    @{ Name = 'skip-direct-parent-check'; Needle = 'if (!ReferenceEquals(node.Source.parent, node.Parent)) return false;'; Replacement = '/* negative control: ignore reparenting */'; Failure = 'moved static model rejects stale view' },
    @{ Name = 'skip-renderer-restore'; Needle = 'mesh.Source.forceRenderingOff = mesh.WasHidden;'; Replacement = 'mesh.Source.forceRenderingOff = true;'; Failure = 'hide restores exact previously visible and hidden states' }
)
foreach ($mutation in $mutations) {
    if (($original.Split([string[]]@($mutation.Needle), [StringSplitOptions]::None).Count - 1) -ne 1) {
        throw "Negative control '$($mutation.Name)' requires exactly one source match."
    }
    $path = Join-Path $scratch ($mutation.Name + '.cs')
    [IO.File]::WriteAllText($path, $original.Replace($mutation.Needle, $mutation.Replacement), [Text.UTF8Encoding]::new($false))
    $result = & dotnet run --project $viewProject "-p:ViewSource=$path" 2>&1 | Out-String
    $code = $LASTEXITCODE
    if ($code -eq 0 -or !$result.Contains($mutation.Failure)) {
        throw "Negative control '$($mutation.Name)' did not fail at the intended assertion (exit=$code). $result"
    }
    Write-Output "REJECTED: $($mutation.Name)"
}
# Restore the positive harness binary as well; never write production source.
& dotnet run --project $viewProject
if ($LASTEXITCODE -ne 0) { throw 'Final production view checks failed.' }
