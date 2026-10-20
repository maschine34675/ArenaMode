using Mono.Cecil;
using Mono.Cecil.Cil;
using System.Security.Cryptography;

internal static class PublishedMetadata
{
    internal static void Verify(string managerPath, string brokenPath, string nativePath, Action<bool, string> need)
    {
        using var manager = AssemblyDefinition.ReadAssembly(managerPath);
        need(manager.Name.Name == "EFTEffectManager" && manager.Name.Version == new Version(1, 0, 0, 0),
            "published manager assembly identity differs from release version");
        need(manager.MainModule.Mvid.ToString() == ArenaMode.Compat.ArenaEffectsContract.ReviewedMvid, "published manager reviewed MVID");
        var all = Types(manager.MainModule.Types).ToArray();
        need(!all.SelectMany(t => t.CustomAttributes).Any(a => a.AttributeType.FullName == "BepInEx.BepInPlugin"), "published manager is a library");
        var type = all.Single(t => t.FullName == "EFTEffectManager.EffectsManager");
        foreach (var name in new[] { "NotifyRaidStarted", "NotifyRaidEnded", "RefreshPenalties", "NotifyEffectStrengthChanged", "ApplyEffectSkillBonuses", "FindSkill" })
            need(type.Methods.Count(m => m.Name == name && m.IsStatic && m.HasBody) == 1, "published manager method " + name);
        var start = type.Methods.Single(m => m.Name == "NotifyRaidStarted");
        need(Calls(start).Any(m => m.Name == "NotifyRaidEnded"), "published start calls global cleanup and must not be replayed");
        need(Calls(type.Methods.Single(m => m.Name == "NotifyRaidEnded")).Any(m => m.Name == "ClearAllSkillBonuses"),
            "published end globally clears temporary skills");
        var lifecycle = all.Single(t => t.FullName == "EFTEffectManager.Patches.PlayerLifecyclePatch");
        var attached = Calls(lifecycle.Methods.Single(m => m.Name == "Attach")).Where(m => m.Name.StartsWith("add_")).Select(m => m.Name).Order().ToArray();
        var detached = Calls(lifecycle.Methods.Single(m => m.Name == "Detach")).Where(m => m.Name.StartsWith("remove_"))
            .Select(m => m.Name.Replace("remove_", "add_")).Order().ToArray();
        var expected = new[] { "add_EffectAddedEvent", "add_EffectStartedEvent", "add_EffectUpdatedEvent", "add_EffectResidualEvent",
            "add_EffectRemovedEvent", "add_BodyPartDestroyedEvent", "add_OnDamageReceived" }.Order().ToArray();
        need(attached.SequenceEqual(expected) && detached.SequenceEqual(expected), "published exact seven attach/detach callbacks");
        var cache = lifecycle.NestedTypes.Single(t => t.Name == "<>O");
        need(cache.CustomAttributes.Any(a => a.AttributeType.FullName == "System.Runtime.CompilerServices.CompilerGeneratedAttribute")
            && cache.Fields.Count == 7, "published compiler-generated seven delegate caches");
        need(cache.Fields.Single(f => f.Name.EndsWith("__Damaged")).FieldType.FullName == "EFT.Player/DamageDelegate"
            && cache.Fields.Single(f => f.Name.EndsWith("__Destroyed")).FieldType.FullName == "System.Action`2<EBodyPart,EFT.EDamageType>"
            && cache.Fields.Where(f => !f.Name.EndsWith("__Damaged") && !f.Name.EndsWith("__Destroyed"))
                .All(f => f.FieldType.FullName == "System.Action`1<EFT.HealthSystem.IHealthEffect>"),
            "published delegate cache types include native DamageDelegate");
        need(lifecycle.Fields.Single(f => f.Name == "_player").FieldType.FullName == "EFT.Player"
            && lifecycle.Fields.Single(f => f.Name == "_controller").FieldType.FullName == "EFT.HealthSystem.ActiveHealthController",
            "published callback owners are physical body and controller");
        need(type.Fields.Single(f => f.Name == "SkillBonusPool").FieldType.FullName
            == "System.Collections.Generic.Dictionary`2<EFT.HealthSystem.IHealthController,System.Collections.Generic.Dictionary`2<EFT.ESkillId,System.Int32>>",
            "published skill pool shape");
        var bonus = type.NestedTypes.Single(t => t.Name == "EffectSkillBonus");
        need(bonus.Fields.Single(f => f.Name == "Controller").FieldType.FullName == "EFT.HealthSystem.ActiveHealthController", "published effect ledger owner");
        var speed = all.Single(t => t.FullName == "EFTEffectManager.SpeedPenaltyRegistry");
        need(speed.Fields.Single(f => f.Name == "_localContext").FieldType.FullName == "EFT.MovementContext", "published speed context");
        need(!Calls(speed.Methods.Single(m => m.Name == "Reset")).Any(m => m.Name == "Clear" || m.Name == "Remove"),
            "published speed reset does not erase providers");
        need(type.Properties.Single(p => p.Name == "RaidPlayer").SetMethod?.IsPrivate == true,
            "published raid player private setter");
        using var broken = AssemblyDefinition.ReadAssembly(brokenPath);
        var plugin = broken.MainModule.Types.Single(t => t.FullName == "BrokenLimbsDontHeal.Plugin");
        var attr = plugin.CustomAttributes.Single(a => a.AttributeType.FullName == "BepInEx.BepInPlugin");
        need((string)attr.ConstructorArguments[0].Value == ArenaMode.Compat.ArenaEffectsContract.BrokenLimbsGuid
            && (string)attr.ConstructorArguments[2].Value == "1.1.0", "published Broken Limbs plugin identity");
        need(broken.Name.Version == new Version(1, 7, 0, 0), "Broken Limbs assembly version is not plugin version");
        var fracture = broken.MainModule.Types.Single(t => t.Name == "HealedFracture");
        need(fracture.BaseType.FullName == "EFT.HealthSystem.ActiveHealthController/Effect"
            && !fracture.Interfaces.Any(i => i.InterfaceType.FullName.EndsWith("IRestorable")), "healed fracture not a persistent effect");
        need(Calls(plugin.Methods.Single(m => m.Name == "Awake")).Any(m => m.Name.StartsWith("RegisterEffect")), "Broken Limbs initializes manager at startup");
        var rebreak = Types(broken.MainModule.Types).Single(t => t.Name == "HealedFractureReBreakPatch");
        need(Calls(rebreak.Methods.Single(m => m.Name == "Prefix")).Any(m => m.Name == "IsRaidController")
            && Calls(rebreak.Methods.Single(m => m.Name == "Postfix")).Any(m => m.Name == "IsRaidController"),
            "Broken Limbs rebreak uses manager physical-controller ownership");
        using var native = AssemblyDefinition.ReadAssembly(nativePath);
        var nativeTypes = Types(native.MainModule.Types).ToArray();
        var nativeWorld = nativeTypes.Single(t => t.FullName == "EFT.GameWorld");
        need(nativeWorld.Methods.Count(m => m.Name == "OnGameStarted" && m.Parameters.Count == 0) == 1
            && nativeWorld.Methods.Count(m => m.Name == "Dispose" && m.Parameters.Count == 0) == 1, "native lifecycle targets exist");
        var health = nativeTypes.Single(t => t.FullName == "EFT.HealthSystem.BaseHealthController`1");
        need(health.Events.Count(e => expected.Contains("add_" + e.Name)) == 6, "native six health events exist");
        var nativePlayer = nativeTypes.Single(t => t.FullName == "EFT.Player");
        need(nativePlayer.Events.Any(e => e.Name == "OnDamageReceived"), "native body damage event exists");
        need(Calls(nativePlayer.Properties.Single(p => p.Name == "Skills").GetMethod).Any(m => m.Name == "get_Profile"),
            "native body skill manager is profile derived");
        var nativeSkill = nativeTypes.Single(t => t.FullName == "EFT.Skill");
        need(nativeSkill.Properties.Single(p => p.Name == "Buff").SetMethod?.IsPublic == true, "native skill bonus setter exists");
        foreach (var path in new[] { managerPath, brokenPath, nativePath })
            Console.WriteLine(Path.GetFileName(path) + " SHA256 " + Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))));
    }
    private static IEnumerable<MethodReference> Calls(MethodDefinition method)
        => method.Body.Instructions.Where(i => i.Operand is MethodReference).Select(i => (MethodReference)i.Operand);
    private static IEnumerable<TypeDefinition> Types(IEnumerable<TypeDefinition> types)
    {
        foreach (var type in types)
        {
            yield return type;
            foreach (var nested in Types(type.NestedTypes)) yield return nested;
        }
    }
}
