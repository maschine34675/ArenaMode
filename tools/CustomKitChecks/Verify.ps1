$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '../Invoke-ArenaSourceChecks.ps1')
# Offline checks of the custom-kit rules (docs/reviews/CUSTOM-KITS-20261110.md): the baseline, then
# one mutant of the production source per rule; each must fail its named assertion.
function Source([string]$relative) { (Resolve-Path (Join-Path $PSScriptRoot ('../../ArenaMode.Client/Kits/' + $relative))).Path }

$policy = Source 'ArenaCustomKitPolicy.cs'
$policyFaults = @(
    @{ Name='id-upper-case'; Find="private static char Hex(int value) => (char)(value < 10 ? '0' + value : 'a' + value - 10);"; Replace="private static char Hex(int value) => (char)(value < 10 ? '0' + value : 'A' + value - 10);"; Failure='a new id is well formed' },
    @{ Name='taken-id-kept'; Find='if (!taken(candidate)) return candidate;'; Replace='return candidate;'; Failure='a taken id is drawn again' },
    @{ Name='cap-first-price'; Find='if (price > cap) cap = price;'; Replace='if (cap == 0) cap = price;'; Failure='the cap is the dearest stock price' },
    @{ Name='cap-equal-refused'; Find='cap > 0 && price > 0 && price <= cap'; Replace='cap > 0 && price > 0 && price < cap'; Failure='a kit at the cap is issued' },
    @{ Name='no-cap-allows'; Find='cap > 0 && price > 0 && price <= cap'; Replace='price > 0 && (cap <= 0 || price <= cap)'; Failure='an unknown cap or price refuses' },
    @{ Name='unready-issued'; Find='=> state == CustomKitState.Ready && cap > 0'; Replace='=> cap > 0'; Failure='only a ready kit is issued' },
    @{ Name='hold-follows-live'; Find="        if (_held && _epoch == armEpoch)`n            return _value;"; Replace=''; Failure='an armed raid keeps its value without reading again' },
    @{ Name='new-arm-keeps-old'; Find='if (_held && _epoch == armEpoch)'; Replace='if (_held)'; Failure='a new arm reads again' },
    @{ Name='hold-outlives-raid'; Find="            _held = false;`n            _value = default;"; Replace='            _value = default;'; Failure='leaving the raid releases the hold' },
    @{ Name='refused-value-held'; Find='if (holdable == null || holdable(value))'; Replace='if (true)'; Failure='a value the hold refuses is answered but not held' },
    @{ Name='sellable-unpriced'; Find='=> state == CustomKitState.Ready && price > 0;'; Replace='=> state == CustomKitState.Ready;'; Failure='solo Tactical sells any ready priced kit, cap or not' },
    @{ Name='picker-above-cap-allowed'; Find='return price > cap ? "above the stock cap: solo Tactical only" : null;'; Replace='return null;'; Failure='the picker says why a kit is blocked' },
    @{ Name='picker-reason-lost'; Find="return string.IsNullOrWhiteSpace(reason) ? `"not usable in this session`" : reason;`n            case CustomKitState.NoPrice:"; Replace="return `"not usable in this session`";`n            case CustomKitState.NoPrice:"; Failure='the picker says why a kit is blocked' },
    @{ Name='shop-blocks-ready'; Find='return Sellable(state, price) ? null : "no price: the game''s handbook is not loaded";'; Replace='return "blocked";'; Failure='the shop blocks only unusable or unpriced kits' }
)
foreach ($fault in $policyFaults) { $fault.Path = $policy; $fault.Property = 'PolicySource' }

$structure = Source 'CustomKitStructure.cs'
$structureFaults = @(
    @{ Name='second-root-allowed'; Find='if (roots != 1)'; Replace='if (roots < 1)'; Failure='two roots are refused' },
    @{ Name='orphan-allowed'; Find='{ problems.Add("item " + node.Id + " has a missing parent"); continue; }'; Replace='continue;'; Failure='an orphan is refused' },
    @{ Name='cycle-allowed'; Find='if (Cycles(node, byId)) { problems.Add("item " + node.Id + " is its own ancestor"); continue; }'; Replace=''; Failure='a cycle is refused' },
    @{ Name='double-slot-allowed'; Find='!occupied.Add(node.ParentId + "\u0001" + node.SlotId)'; Replace='!occupied.Add(node.Id)'; Failure='a slot with two items is refused' },
    @{ Name='stacks-refused'; Find='!string.Equals(node.SlotId, CartridgesSlot, StringComparison.Ordinal) && !occupied.Add('; Replace='!occupied.Add('; Failure='a magazine holds several stacks' },
    @{ Name='no-size-limit'; Find='if (items.Count > MaxNodes)'; Replace='if (false)'; Failure='an oversized kit is refused' },
    @{ Name='any-kit-id'; Find='if (!ArenaCustomKitId.IsWellFormed(kit.Id)) problems.Add("id is not a custom kit id");'; Replace=''; Failure='a stock id is refused' },
    @{ Name='any-root-slot'; Find='if (!Contains(RootSlots, node.SlotId)) problems.Add('; Replace='if (false) problems.Add('; Failure='a slot no stock kit uses is refused' },
    @{ Name='any-upd'; Find="                default:`n                    return `"carries upd `" + property.Name;"; Replace="                default:`n                    break;"; Failure='an upd key the builder ignores is refused' },
    @{ Name='medkit-allowed'; Find='                case "Foldable":'; Replace="                case `"MedKit`": break;`n                case `"Foldable`":"; Failure='a medkit resource is refused' },
    @{ Name='stack-anywhere'; Find='if (count != 1 && !string.Equals(node.SlotId, CartridgesSlot, StringComparison.Ordinal)) return "is a stack outside a magazine";'; Replace=''; Failure='a stack outside a magazine is refused' },
    @{ Name='stack-any-type'; Find='if (!Whole(property.Value, out long count) || count < 1) return "has a stack count that is not a whole number above 0";'; Replace='Whole(property.Value, out long count);'; Failure='a stack count that is not a whole number is refused' },
    @{ Name='durability-anywhere'; Find='if (!primaryWeapon || !(property.Value is JObject repairable)'; Replace='if (!(property.Value is JObject repairable)'; Failure='a durability on another item is refused' },
    @{ Name='durability-unbounded'; Find='|| durability <= 0 || durability > maximum || maximum > envelope.MaxDurability)'; Replace='|| durability <= 0 || durability > maximum)'; Failure='a durability above the stock weapons is refused' },
    @{ Name='durability-extra-keys'; Find='!Only(repairable, "Durability", "MaxDurability")'; Replace='false'; Failure='a durability that is not two fitting numbers is refused' },
    @{ Name='fire-mode-any'; Find='|| mode.Type != JTokenType.String || !envelope.FireModes.Contains((string)mode))'; Replace='|| mode.Type != JTokenType.String)'; Failure='a fire mode no stock kit sets is refused' },
    @{ Name='fold-any-type'; Find='|| fold["Folded"]?.Type != JTokenType.Boolean)'; Replace=')'; Failure='a fold state that is not true or false is refused' },
    @{ Name='envelope-any-durability'; Find='envelope.MaxDurability = Math.Max(envelope.MaxDurability, max);'; Replace='envelope.MaxDurability = 1e9;'; Failure='the envelope reads the stock upd values' },
    @{ Name='upper-case-template'; Find="if (!(c >= '0' && c <= '9') && !(c >= 'a' && c <= 'f')) return false;`n        return true;`n    }`n`n    /// <summary>`n    /// Why the node"; Replace="if (!(c >= '0' && c <= '9') && !(c >= 'a' && c <= 'f') && !(c >= 'A' && c <= 'F')) return false;`n        return true;`n    }`n`n    /// <summary>`n    /// Why the node"; Failure='an upper-case template id is refused' },
    @{ Name='duplicate-id-allowed'; Find='if (byId.ContainsKey(node.Id)) { problems.Add("item id " + node.Id + " is used twice"); continue; }'; Replace=''; Failure='a node id used twice is refused' },
    @{ Name='blank-slot-allowed'; Find='if (string.IsNullOrWhiteSpace(node.SlotId)) { problems.Add("item " + node.Id + " has no slot"); continue; }'; Replace=''; Failure='a blank slot is refused' },
    @{ Name='filled-container'; Find='problems.Add("the secure container is not empty");'; Replace='{ }'; Failure='a filled secure container is refused' },
    @{ Name='foreign-container'; Find='problems.Add("the secure container is not a stock one");'; Replace='{ }'; Failure='another secure container is refused' },
    @{ Name='foreign-pockets'; Find='problems.Add("the pockets are not stock ones");'; Replace='{ }'; Failure='other pockets are refused' },
    @{ Name='spare-hoard'; Find='fill.SpareMagazines > envelope.MaxSpareMagazines'; Replace='false'; Failure='more spare magazines than stock are refused' },
    @{ Name='rig-any-template'; Find='!envelope.RigTemplates.Contains(entry.Tpl ?? string.Empty)'; Replace='false'; Failure='a supply no stock kit carries is refused' },
    @{ Name='rig-any-count'; Find='entry.Count > envelope.MaxRigCount'; Replace='false'; Failure='a supply above the stock count is refused' },
    @{ Name='rig-many-entries'; Find='if (rig.Count > envelope.MaxRigEntries)'; Replace='if (false)'; Failure='more supply entries than stock are refused' },
    @{ Name='markup-name'; Find="if (char.IsControl(c) || c == '<' || c == '>') continue;"; Replace='if (char.IsControl(c)) continue;'; Failure='a name with markup or none is refused' },
    @{ Name='name-uncut'; Find='return result.Length > MaxNameLength ? result.Substring(0, MaxNameLength).TrimEnd() : result;'; Replace='return result;'; Failure='a name is cut to 32 characters' },
    @{ Name='any-root-template'; Find='problems.Add("the root is not the equipment template");'; Replace='{ }'; Failure='a root that is not the equipment is refused' },
    @{ Name='no-primary-needed'; Find='if (primaries != 1) problems.Add("not exactly one primary weapon");'; Replace=''; Failure='a kit without a weapon is refused' },
    @{ Name='any-template-id'; Find='if (!IsTemplateId(node.Tpl)) problems.Add('; Replace='if (false) problems.Add('; Failure='a malformed template id is refused' }
)
foreach ($fault in $structureFaults) { $fault.Path = $structure; $fault.Property = 'StructureSource' }

$editing = Source 'CustomKitEditing.cs'
$editingFaults = @(
    @{ Name='copy-keeps-ids'; Find='clone.Id = node.Id != null && map.TryGetValue(node.Id, out var fresh) ? fresh : ids();'; Replace='clone.Id = node.Id;'; Failure='a copy of every stock kit is sound' },
    @{ Name='clone-shares-upd'; Find='Upd = node.Upd == null ? null : (JObject)node.Upd.DeepClone()'; Replace='Upd = node.Upd'; Failure='a copy shares no upd with its source' },
    @{ Name='subtree-one-level'; Find='node.ParentId != null && set.Contains(node.ParentId) && set.Add(node.Id)'; Replace='node.ParentId != null && node.ParentId == id && set.Add(node.Id)'; Failure='a subtree reaches the cartridges of the magazine' },
    @{ Name='replace-mutates-input'; Find="KitDefinition copy = Clone(kit);`n        KitItemNode old = RootSlot(copy, slotId);"; Replace="KitDefinition copy = kit;`n        KitItemNode old = RootSlot(copy, slotId);"; Failure='a replace leaves its input untouched' },
    @{ Name='replace-keeps-children'; Find='var gone = Subtree(copy.Items, old.Id);'; Replace='var gone = new HashSet<string>(StringComparer.Ordinal) { old.Id };'; Failure='a replace removes the whole old subtree' },
    @{ Name='replace-reuses-ids'; Find="used twice.`", nameof(subtree));`n            map[node.Id] = ids();"; Replace="used twice.`", nameof(subtree));`n            map[node.Id] = node.Id;"; Failure='a replace grafts the new subtree with fresh ids under the root' },
    @{ Name='replace-loses-root'; Find='                clone.ParentId = root;'; Replace='                clone.ParentId = null;'; Failure='a replace grafts the new subtree with fresh ids under the root' },
    @{ Name='renamed-in-place'; Find="KitDefinition copy = Clone(kit);`n        copy.Name = name;"; Replace="KitDefinition copy = kit;`n        copy.Name = name;"; Failure='a rename is a new kit' },
    @{ Name='unpriced-not-zero'; Find='return price <= 0 ? 0 :'; Replace='return price <= 0 ? 1 :'; Failure='an unpriced kit costs 0, which the policy refuses' },
    @{ Name='unpriced-accepted'; Find='if (!(handbookPrice(node.Tpl ?? string.Empty) is double price) || !(price > 0)) return node;'; Replace=''; Failure='an unpriced item is found' },
    @{ Name='zero-price-accepted'; Find='|| !(price > 0)) return node;'; Replace=') return node;'; Failure='a price of 0 is no price' },
    @{ Name='forced-ignored'; Find='if (forced != null && forced(node.Id)) continue;'; Replace=''; Failure='an insert of a locked slot needs no price' },
    @{ Name='pockets-priced'; Find='if (string.Equals(node.ParentId, root, StringComparison.Ordinal) && string.Equals(node.SlotId, CustomKitStructure.PocketsSlot, StringComparison.Ordinal)) continue;'; Replace=''; Failure='the root, the pockets and the secure container need no price' },
    @{ Name='container-priced'; Find='|| plumbing.Contains(node.Id)) continue;'; Replace=') continue;'; Failure='the root, the pockets and the secure container need no price' },
    @{ Name='root-priced'; Find='if (node?.Id == null || string.Equals(node.Id, root, StringComparison.Ordinal) ||'; Replace='if (node?.Id == null ||'; Failure='the root, the pockets and the secure container need no price' },
    @{ Name='exclusion-ignored'; Find='if (node?.Tpl != null && ArenaTemplateExclusions.IsExcluded(node.Tpl, displayName?.Invoke(node.Tpl), excluded))'; Replace='if (false)'; Failure='a seeded broken item is found' },
    @{ Name='exclusion-no-name'; Find='ArenaTemplateExclusions.IsExcluded(node.Tpl, displayName?.Invoke(node.Tpl), excluded)'; Replace='ArenaTemplateExclusions.IsExcluded(node.Tpl, null, excluded)'; Failure='a placeholder item is found' }
)
foreach ($fault in $editingFaults) { $fault.Path = $editing; $fault.Property = 'EditingSource' }

$store = Source 'CustomKitStore.cs'
$storeFaults = @(
    @{ Name='not-atomic'; Find='                    Commit(temp, path);'; Replace='                    File.Copy(temp, path, true); TryDelete(temp);'; Failure='a save keeps the previous file as the backup' },
    @{ Name='stale-overwrite'; Find='                    if (!Stamp.Of(path).Equals(before))'; Replace='                    if (false)'; Failure='a file changed since the read is not written over' },
    @{ Name='bad-record-dropped'; Find='var records = file.Records.Select(record => record.Raw.DeepClone()).ToList();'; Replace='var records = file.Records.Where(record => record.Kit != null).Select(record => record.Raw.DeepClone()).ToList();'; Failure='bad records and unknown fields are written back as they were' },
    @{ Name='corrupt-overwritten'; Find='        string kept = Quarantine(path);'; Replace='        string kept = path; File.Delete(path);'; Failure='an unreadable file is kept aside as it was' },
    @{ Name='backup-ignored'; Find='if (File.Exists(backup) && Read(backup, restored, out _) == ReadOutcome.Read && !restored.ReadOnly)'; Replace='if (false)'; Failure='the last good copy is restored and the window is told' },
    @{ Name='held-file-quarantined'; Find="        if (outcome == ReadOutcome.Inaccessible)`n        {"; Replace="        if (false)`n        {"; Failure='a file held by another program is left alone' },
    @{ Name='missing-ignores-backup'; Find='if (!File.Exists(path)) return RestoreMissing(path) ?? file;'; Replace='if (!File.Exists(path)) return file;'; Failure='a missing file is restored from its backup' },
    @{ Name='index-any-record'; Find="if (records[index] is JObject record && record[`"id`"] is JValue value && value.Type == JTokenType.String`n                && string.Equals((string)value, id, StringComparison.OrdinalIgnoreCase))"; Replace='if (records[index] is JObject record)'; Failure='the index finds the record by id past records whose id is not text' },
    @{ Name='index-raw-cast'; Find="record[`"id`"] is JValue value && value.Type == JTokenType.String`n                && string.Equals((string)value, id, StringComparison.OrdinalIgnoreCase)"; Replace="string.Equals((string)record[`"id`"], id, StringComparison.OrdinalIgnoreCase)"; Failure='the index finds the record by id past records whose id is not text' },
    @{ Name='huge-version-throws'; Find='try { version = Convert.ToInt64(number.Value, CultureInfo.InvariantCulture); }'; Replace='version = (long)number; try { }'; Failure='a version past the range of a number loads read-only instead of throwing' },
    @{ Name='quarantine-unbounded'; Find='.Skip(KeptQuarantines)'; Replace='.Skip(int.MaxValue)'; Failure='only the newest three unreadable files are kept' },
    @{ Name='newer-schema-writable'; Find='if (version > SchemaVersion)'; Replace='if (false)'; Failure='a newer schema loads read-only' },
    @{ Name='crowded-writable'; Find='else if (kits.Count > MaxKits)'; Replace='else if (false)'; Failure='more kits than the limit load read-only' },
    @{ Name='grows-unbounded'; Find='if (records.Count > MaxKits) return "at most " + MaxKits + " kits";'; Replace=''; Failure='a save cannot grow past the limit' },
    @{ Name='oversize-read'; Find='if (info.Length > MaxFileBytes) { problem = "larger than 1 MiB"; return ReadOutcome.Unreadable; }'; Replace=''; Failure='an oversized file is set aside' },
    @{ Name='price-trusted'; Find='            kit.Price = 0;'; Replace=''; Failure='bad records load as problems, a stored price is ignored' },
    @{ Name='record-loses-fields'; Find='var record = existing is JObject old ? (JObject)old.DeepClone() : new JObject();'; Replace='var record = new JObject();'; Failure='an edited record keeps its unknown fields' },
    @{ Name='price-written'; Find='        record.Remove("price");'; Replace=''; Failure='an edited record keeps its unknown fields' },
    @{ Name='refused-still-writes'; Find='if (refused != null) return refused;'; Replace=''; Failure='a refused change writes nothing' },
    @{ Name='stale-temp-blocks'; Find='if (File.Exists(temp)) File.Delete(temp);'; Replace=''; Failure='a stale temp file does not block a save' }
)
foreach ($fault in $storeFaults) { $fault.Path = $store; $fault.Property = 'StoreSource' }

$candidates = Source 'CustomKitCandidates.cs'
$candidateFaults = @(
    @{ Name='placeholder-offered'; Find='if (ArenaTemplateExclusions.IsExcluded(facts.Tpl, facts.DisplayName, excluded)) return "excluded for the arena";'; Replace=''; Failure='a placeholder is not offered' },
    @{ Name='modelless-offered'; Find='if (!facts.HasPrefab) return "no model";'; Replace=''; Failure='an item without a model is not offered' },
    @{ Name='stationary-offered'; Find='if (facts.IsStationary) return "a stationary weapon";'; Replace=''; Failure='a stationary weapon is not offered' },
    @{ Name='test-weapon-offered'; Find='if (facts.InternalName != null && facts.InternalName.StartsWith("weapon_test", StringComparison.OrdinalIgnoreCase))'; Replace='if (false)'; Failure='the test weapon is not offered' },
    @{ Name='unpriced-offered'; Find='if (facts.HandbookPrice == null || facts.HandbookPrice <= 0) return "no handbook price";'; Replace=''; Failure='an unpriced item is not offered' },
    @{ Name='zero-price-offered'; Find='facts.HandbookPrice == null || facts.HandbookPrice <= 0'; Replace='facts.HandbookPrice == null'; Failure='an unpriced item is not offered' },
    @{ Name='presetless-weapon-offered'; Find='if (facts.IsWeapon && !facts.HasPreset) return "no default build";'; Replace=''; Failure='a weapon without a default build is not offered' },
    @{ Name='presetless-gear-refused'; Find='if (facts.IsWeapon && !facts.HasPreset)'; Replace='if (!facts.HasPreset)'; Failure='gear without a preset is offered' }
)
foreach ($fault in $candidateFaults) { $fault.Path = $candidates; $fault.Property = 'CandidatesSource' }

Test-ArenaSourceFaults -Project (Join-Path $PSScriptRoot 'CustomKitChecks.csproj') -Token 'CUSTOM_KIT_CHECKS_PASSED' -Properties @() `
    -Faults ($policyFaults + $structureFaults + $editingFaults + $storeFaults + $candidateFaults)
