[CmdletBinding()]
param([string]$RuntimeRoot, [string]$ItemDatabase)
$ErrorActionPreference = 'Stop'
$repo = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
if (-not $RuntimeRoot) { $RuntimeRoot = Join-Path (Split-Path (Split-Path $repo -Parent) -Parent) 'SPT_Runtime' }
if (-not $ItemDatabase) { $ItemDatabase = Join-Path $RuntimeRoot 'SPT_Data/database/templates/items.json' }
$RuntimeRoot = [IO.Path]::GetFullPath($RuntimeRoot)
$ItemDatabase = [IO.Path]::GetFullPath($ItemDatabase)
. (Join-Path $repo 'tools/Invoke-ArenaSourceChecks.ps1')
. (Join-Path $PSScriptRoot 'Extract-Confirmation.ps1')

# This standalone project links production sources and the actual xUnit cases;
# it has no mod ProjectReference, deployment target, profile or live writes.
# Native service calls below operate only on isolated DTOs. The installed game
# database and DLL are read to prevent a synthetic donor masking incompatibility.
$translocatorClient = Join-Path $repo 'ArenaMode.Client/Session/ArenaServerClient.cs'
$translocatorRouter = Join-Path $repo 'ArenaMode.Server/Routers/ArenaModeRouter.cs'
$registration = Join-Path $repo 'ArenaMode.Server/Services/ArenaTranslocatorRegistration.cs'
$stage = Join-Path $repo ('artifacts/translocator-20261115/item/' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $stage -Force | Out-Null
$fixture = Join-Path $stage 'Confirmation.cs'
Write-TranslocatorConfirmation $translocatorClient $translocatorRouter $fixture

$clientFault = {
    param($FaultCopy, $FaultDirectory, $FaultMetadata)
    $generated = Join-Path $FaultDirectory 'Confirmation.cs'
    Write-TranslocatorConfirmation $FaultCopy $translocatorRouter $generated
    '-p:ClientContractSource=' + $generated
}
$routerFault = {
    param($FaultCopy, $FaultDirectory, $FaultMetadata)
    $generated = Join-Path $FaultDirectory 'Confirmation.cs'
    Write-TranslocatorConfirmation $translocatorClient $FaultCopy $generated
    '-p:ClientContractSource=' + $generated
}
$faults = @(
    @{ Name='late-native-registration'; Path=$registration; Property='RegistrationSource';
       Find='OnLoadOrder.Preload'; Replace='int.MaxValue'; Failure='registration precedes native profile snapshot' },
    @{ Name='ordinary-handbook-entry'; Path=$registration; Property='RegistrationSource';
       Find='AddToHandbook = false'; Replace='AddToHandbook = true'; Failure='DefinitionHasNoOrdinaryCommerceOrLootEntry' },
    @{ Name='ordinary-flea-price-entry'; Path=$registration; Property='RegistrationSource';
       Find='AddToFleaPriceDb = false'; Replace='AddToFleaPriceDb = true'; Failure='DefinitionHasNoOrdinaryCommerceOrLootEntry' },
    @{ Name='foreign-template-adoption'; Path=$registration; Property='RegistrationSource';
       Find='ReferenceEquals(occupied, RegisteredTemplate) && IsDevice(occupied)'; Replace='IsDevice(occupied)'; Failure='ForeignIdIsNeverAdopted' },
    @{ Name='availability-replaced-object'; Path=$registration; Property='RegistrationSource';
       Find='ReferenceEquals(current, RegisteredTemplate) && IsDevice(current)'; Replace='IsDevice(current)'; Failure='ReplacedOrChangedRegisteredObjectRevokesAvailability' },
    @{ Name='availability-changed-object'; Path=$registration; Property='RegistrationSource';
       Find='ReferenceEquals(current, RegisteredTemplate) && IsDevice(current)'; Replace='ReferenceEquals(current, RegisteredTemplate)'; Failure='ReplacedOrChangedRegisteredObjectRevokesAvailability' },
    @{ Name='skip-publication-readback'; Path=$registration; Property='RegistrationSource';
       Find='|| !items.TryGetValue(id, out var registered) || !IsDevice(registered)'; Replace='|| !items.TryGetValue(id, out var registered)'; Failure='EveryDeviceInvariantIsReadBackAfterPublication' },
    @{ Name='share-donor-properties'; Path=$registration; Property='RegistrationSource';
       Find=' || ReferenceEquals(registered.Properties, donor.Properties)'; Replace=''; Failure='SharedPropertiesWithDonorAreNeverAccepted' },
    @{ Name='leave-partial-publication'; Path=$registration; Property='RegistrationSource';
       Find='items.Remove(id);'; Replace='// Fault: partial publication survives.'; Failure='EveryDeviceInvariantIsReadBackAfterPublication' },
    @{ Name='native-flea-protection-omitted'; Path=$registration; Property='RegistrationSource';
       Find='customFleaBlacklist.Add(new MongoId(ArenaTranslocatorItem.TemplateId));'; Replace='// Fault: native flea pass can change the device.'; Failure='SuccessfulRegistrationPreservesDonorAndExistingExclusions' },
    @{ Name='missing-native-blacklist-admitted'; Path=$registration; Property='RegistrationSource';
       Find='if (customFleaBlacklist == null)'; Replace='if (false)'; Failure='MissingBlacklistRefusesBeforeNativePublication' },
    @{ Name='wrong-donor-parent-admitted'; Path=$registration; Property='RegistrationSource';
       Find='|| donor.Type != "Item" || donor.Parent.ToString() != ArenaTranslocatorItem.ParentTemplateId'; Replace='|| donor.Type != "Item"'; Failure='IncompatibleDonorsAreRejected' },
    @{ Name='wrong-native-rig-admitted'; Path=$registration; Property='RegistrationSource';
       Find='&& properties.Prefab?.Path == ArenaTranslocatorItem.PrefabPath'; Replace='&& properties.Prefab != null'; Failure='EveryDeviceInvariantIsReadBackAfterPublication' },
    @{ Name='ordinary-equipment-pool-admission'; Path=(Join-Path $repo 'ArenaMode.Shared/ArenaTemplateExclusions.cs'); Property='ExclusionsSource';
       Find='"a12e00000000000000000002"'; Replace='"a12e00000000000000000077"'; Failure='PrivateSupplyCannotEnterRandomEquipmentSelection' },
    @{ Name='client-coerces-capability'; Path=$translocatorClient; Prepare=$clientFault;
       Find='response["translocatorAvailable"]?.Type == JTokenType.Boolean'; Replace='response["translocatorAvailable"] != null'; Failure='capability requires explicit boolean true "true"' },
    @{ Name='client-retains-refused-capability'; Path=$translocatorClient; Prepare=$clientFault;
       Find="_molotovArmToken = null;`n        _translocatorArmToken = null;"; Replace='_molotovArmToken = null;'; Failure='failed confirmation revokes stale capability server' },
    @{ Name='client-capability-crosses-arms'; Path=$translocatorClient; Prepare=$clientFault;
       Find='!string.IsNullOrEmpty(_activeArmToken) && _translocatorArmToken == _activeArmToken;'; Replace='!string.IsNullOrEmpty(_activeArmToken) && _translocatorArmToken != null;'; Failure='capability cannot follow another active arm' },
    @{ Name='server-capability-before-confirmation'; Path=$translocatorRouter; Prepare=$routerFault;
       Find='translocatorAvailable = confirmed && Services.ArenaTranslocatorRegistration.IsAvailable'; Replace='translocatorAvailable = Services.ArenaTranslocatorRegistration.IsAvailable'; Failure='unconfirmed server cannot grant capability' }
)
Push-Location $repo
try {
    Test-ArenaSourceFaults -Project (Join-Path $PSScriptRoot 'TranslocatorItemChecks.csproj') `
        -Token 'TRANSLOCATOR_ITEM_PRODUCTION_PASSED' -Properties @('-p:ClientContractSource=' + $fixture) `
        -Faults $faults -RequireSingleSite -Arguments @($RuntimeRoot, $ItemDatabase)
    'TRANSLOCATOR_ITEM_CHECKS_PASSED'
}
finally { Pop-Location }
