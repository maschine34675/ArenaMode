# Metadata-only inspection; never load the game or BigBrain into this process.
function Assert-BigBrainLibrary([string]$ClientPath, [string]$LibraryPath, [string]$CecilPath) {
    Add-Type -Path $CecilPath
    $resolver = [Mono.Cecil.DefaultAssemblyResolver]::new()
    $resolver.AddSearchDirectory((Split-Path $CecilPath))
    $reader = [Mono.Cecil.ReaderParameters]::new()
    $reader.AssemblyResolver = $resolver
    $client = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($ClientPath, $reader)
    $library = [Mono.Cecil.AssemblyDefinition]::ReadAssembly($LibraryPath, $reader)
    try {
        $plugins = @($client.MainModule.GetTypes() | Where-Object {
            @($_.CustomAttributes | Where-Object { $_.AttributeType.FullName -eq 'BepInEx.BepInPlugin' }).Count -gt 0
        })
        # Positive control for the same metadata absence oracle used below.
        if ($plugins.Count -ne 1 -or $plugins[0].FullName -ne 'ArenaMode.Plugin') { throw 'Client plugin metadata control failed.' }
        foreach ($type in $library.MainModule.GetTypes()) {
            if (@($type.CustomAttributes | Where-Object { $_.AttributeType.FullName -eq 'BepInEx.BepInPlugin' }).Count -gt 0 `
                -or ($null -ne $type.BaseType -and $type.BaseType.FullName -eq 'BepInEx.BaseUnityPlugin')) {
                throw "Companion still contains a BepInEx plugin: $($type.FullName)"
            }
        }
        if (@($client.MainModule.AssemblyReferences | Where-Object { $_.Name -match 'BigBrain|SAIN|pitFireTeam' }).Count -gt 0) {
            throw 'Client contains a hard optional AI assembly reference.'
        }
        $dependency = @($plugins[0].CustomAttributes | Where-Object {
            $_.AttributeType.FullName -eq 'BepInEx.BepInDependency' -and
            $_.ConstructorArguments[0].Value -eq 'xyz.drakia.bigbrain' -and
            $_.ConstructorArguments[1].Value -eq 2
        })
        if ($dependency.Count -ne 1) { throw 'Main client lacks the optional BigBrain load-order dependency.' }
        foreach ($hook in @(@('Start','StartRuntime','Initialize'), @('Update','UpdateRuntime','Tick'), @('OnDestroy','ShutdownRuntime','Shutdown'))) {
            $method = @($plugins[0].Methods | Where-Object Name -eq $hook[0])
            if ($method.Count -ne 1 -or -not $method[0].HasBody) { throw "Missing owner lifecycle: $($hook[0])" }
            $calls = ($method[0].Body.Instructions | ForEach-Object { [string]$_.Operand }) -join "`n"
            if (-not $calls.Contains("ArenaMode.Plugin::$($hook[1])(")) { throw "Unwired guarded lifecycle: $($hook[0])" }
            if (-not $calls.Contains('ArenaMode.Plugin::_runtimeStarted')) { throw "Missing lifecycle admission guard: $($hook[0])" }
            $runtime = @($plugins[0].Methods | Where-Object Name -eq $hook[1])
            if ($runtime.Count -ne 1 -or ($runtime[0].ImplAttributes -band [Mono.Cecil.MethodImplAttributes]::NoInlining) -eq 0) {
                throw "Missing lifecycle JIT boundary: $($hook[1])"
            }
            $runtimeCalls = ($runtime[0].Body.Instructions | ForEach-Object { [string]$_.Operand }) -join "`n"
            if (-not $runtimeCalls.Contains("ArenaMode.Compat.ArenaBigBrainLibrary::$($hook[2])(")) { throw "Unwired owner lifecycle: $($hook[0])" }
        }
        $entry = $library.MainModule.GetType('ArenaMode.BigBrainCompat.Library')
        if (-not $entry -or -not $entry.IsAbstract -or -not $entry.IsSealed) { throw 'Missing static library entry.' }
        foreach ($name in @('Initialize','Tick','Shutdown')) {
            $method = @($entry.Methods | Where-Object { $_.Name -eq $name -and $_.IsPublic -and $_.IsStatic })
            if ($method.Count -ne 1) { throw "Missing public library API: $name" }
        }
        $version = @($entry.Fields | Where-Object Name -eq 'ApiVersion')
        if ($version.Count -ne 1 -or $version[0].Constant -ne 1) { throw 'Unexpected library API version.' }
        $boundary = @($library.MainModule.GetType('ArenaMode.BigBrainCompat.BigBrainRuntime').Methods | Where-Object Name -eq 'Register')
        if ($boundary.Count -ne 1 -or ($boundary[0].ImplAttributes -band [Mono.Cecil.MethodImplAttributes]::NoInlining) -eq 0) {
            throw 'Missing optional typed JIT boundary.'
        }
        if ($client.Name.Version -ne $library.Name.Version) { throw 'Client/library versions differ.' }
    } finally { $client.Dispose(); $library.Dispose(); $resolver.Dispose() }
}
