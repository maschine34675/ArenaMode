param([switch]$SkipFaults)
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$project=Join-Path $PSScriptRoot 'PftKitEntryChecks.csproj'
$artifact=Join-Path $root ('artifacts/pft-entry-checks/'+[Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($artifact)|Out-Null
$entry=Join-Path $root 'ArenaMode.Client/Kits/TeamArenaEntryKit.cs'
$builder=Join-Path $root 'ArenaMode.Client/Kits/KitBuilder.cs'
$snapshot=Join-Path $root 'ArenaMode.Client/Kits/TeamArenaLoadoutSnapshot.cs'
$transaction=Join-Path $artifact 'Transaction.cs'
& (Join-Path $PSScriptRoot 'Extract-Transaction.ps1') -Source $snapshot -Destination $transaction
function Run([string]$name,[string]$entryPath,[string]$builderPath,[string]$transactionPath,[bool]$mustPass) {
    $out=Join-Path $artifact ($name+'.log')
    $result=& dotnet run --project $project -c Release --nologo "-p:EntrySource=$entryPath" "-p:BuilderSource=$builderPath" "-p:TransactionSource=$transactionPath" 2>&1
    $code=$LASTEXITCODE
    $result|Set-Content -LiteralPath $out
    $text=$result -join "`n"
    if($mustPass) {
        if($code -ne 0 -or $text -notmatch 'PFT_ENTRY_CHECKS_PASS assertions=\d+') { throw "Positive checks failed: $out`n$text" }
        Write-Output ($text -split "`n" | Where-Object {$_ -match 'PFT_ENTRY_CHECKS_PASS'})
    } else {
        if($text -match '(error CS\d+|error MSB\d+)' -or $text -notmatch 'CHECK FAILED:' -or $code -eq 0) { throw "Fault $name did not compile and fail a behavioral assertion: $out`n$text" }
        Write-Output "FAULT_REJECTED $name"
    }
}
Run 'baseline' $entry $builder $transaction $true
if(-not $SkipFaults) {
    $faults=@(
        @{Name='reuse-pocket-template';File='entry';Old='factory.NextId, oldPockets.TemplateId, null';New='factory.NextId, "kit-pockets", null'},
        @{Name='omit-protected-filter';File='entry';Old='node != root && !skipped.Contains(node.Id)';New='node != root'},
        @{Name='ignore-vital-parts';File='entry';Old='part.MissingVitalParts.Any()';New='false'},
        @{Name='ignore-graph-edit';File='entry';Old='if (!owner.Unchanged(preparedEquipment))';New='if (false)'},
        @{Name='ignore-profile-owner';File='entry';Old='|| owner.Attempted || !ReferenceEquals(owner.Profile, profile)';New='|| owner.Attempted'},
        @{Name='retain-consumed-owner';File='entry';Old='Prepared.Remove(preparedEquipment);';New='// retain source graph'},
        @{Name='retry-failed-transaction';File='entry';Old='owner.Attempted = true;';New='owner.Attempted = false;';Old2='Prepared.Remove(preparedEquipment);';New2='// retain consumed preparation'},
        @{Name='allow-partial-component';File='builder';Old='?? throw new InvalidOperationException($"Entry kit could not attach ''{node.Tpl}'' to ''{node.SlotId}''")';New=''},
        @{Name='drop-required-supply';File='builder';Old='if (strict && !entry.OptionalQuickUseOnly)';New='if (false)'},
        @{Name='omit-reverse-transfers';File='transaction';Old='RollBackTransfers(completedTransfers, rollbackErrors);';New='// omitted reverse transfers'},
        @{Name='omit-metadata-restore';File='transaction';Old='sanitizerState.Restore(profile, FastAccessField);';New='// omitted metadata restore'},
        @{Name='omit-magazine-register';File='transaction';Old='KitBuilder.RegisterAsChecked(profile, issuedItems);';New='// omitted issued magazine registration'},
        @{Name='skip-template-learning';File='transaction';Old='profile.Encyclopedia == null || profile.Examined(item)';New='true'},
        @{Name='learn-personal-items';File='transaction';Old='foreach (Item item in issuedItems)';New='foreach (Item item in newEquipment.GetAllItems())'},
        @{Name='omit-special-transfer';File='transaction';Old='slot => slot.IsSpecial && slot.ContainedItem != null';New='slot => false'}
    )
    $paths=@{entry=$entry;builder=$builder;transaction=$transaction}
    foreach($fault in $faults) {
        $source=[IO.File]::ReadAllText($paths[$fault.File])
        if(-not $source.Contains($fault.Old)) { throw "Fault anchor missing: $($fault.Name)" }
        $mutated=Join-Path $artifact ($fault.Name+'.cs')
        $text=$source.Replace($fault.Old,$fault.New)
        if($fault.Old2) { if(-not $text.Contains($fault.Old2)){throw 'Second fault anchor missing'}; $text=$text.Replace($fault.Old2,$fault.New2) }
        [IO.File]::WriteAllText($mutated,$text)
        $e=$entry;$b=$builder;$t=$transaction
        switch($fault.File) { entry {$e=$mutated} builder {$b=$mutated} transaction {$t=$mutated} }
        Run $fault.Name $e $b $t $false
    }
    # Leave the output assembly compiled from actual production, never the last fault.
    Run 'final-baseline' $entry $builder $transaction $true
    Write-Output "PFT_ENTRY_FAULTS_PASS count=$($faults.Count)"
}
$manifest=[ordered]@{source=@{};artifact=$artifact;timeUtc=[DateTime]::UtcNow.ToString('o');faults=(-not $SkipFaults)}
foreach($path in @($entry,$builder,$snapshot)){$manifest.source[$path]=(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash}
$manifest|ConvertTo-Json -Depth 5|Set-Content -LiteralPath (Join-Path $artifact 'manifest.json')
Write-Output "PFT_ENTRY_VERIFY_PASS manifest=$artifact/manifest.json"
