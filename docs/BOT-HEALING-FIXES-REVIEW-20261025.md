# Bot Healing Fixes 1.0.0: late healing callback review

Reviewed on 2026-10-25 against the actual error recorded during the protected
Factory exit on 2026-10-24. This is source and released-binary evidence, not a
native compatibility test. No mod was installed, whitelisted or incorporated.

## Decision

Bot Healing Fixes addresses useful, separate healing defects, but its current
release is not a reliable fix for this late bot-lifetime failure. It replaces
the native callback implicated by our log while retaining unsafe accesses to
the same disposed weapon selector in its asynchronous replacement. Do not add
it to the shipped whitelist solely as a remedy for this exception.

A targeted fix should invalidate outstanding healing completions when their
exact bot is disposed, without disabling normal healing. Check bot/world
lifetime again after asynchronous completion and external callbacks. Disposal
must be distinguishable from an ordinary healing timeout; a retired bot must
not resume weapon, inventory or medicine operations. A null check on the
selector alone is insufficient because subsequent completion logic also uses
the disposed medicine subsystem. This review does not implement that fix.

## Recorded error and native lifetime

Evidence: `artifacts/transits-warmup-ammo-20261024/native/warnings.json`, merged
event at `2026-10-24T19:57:56.247+02:00`, summarized in
[the preceding GPU report](TRANSITS-GUNGAME-AMMO-20261024.md).

- `BotFirstAid+CG_ApplyToSelf.method_1` fails at IL offset `0x00026`, reached
  through `MedsInHandsOperation.Finish` and health-effect removal.
- The installed `Assembly-CSharp.dll` MVID matches the logged module:
  `cc2d80b0-6d5b-4cb1-a581-6d2cc901d4c7`.
- Read-only IL inspection places `BotWeaponSelector.TakePrevWeapon()` at
  `IL_0026`, immediately after loading `WeaponManager.Selector`.
- Native `BotWeaponManager.Dispose()` sets `Selector` to null.
  `BotOwner.Dispose()` disposes the weapon manager before medicine and later
  clears `Medecine`. Native medicine disposal does not invalidate this pending
  hands-completion callback.

This fits a healing completion running after bot teardown. The stack alone
does not identify which caller initiated teardown, and a Harmony wrapper in
the stack does not establish ArenaMode or Redeploy as its cause.

## Upstream implementation and release verification

- [Forge listing](https://sp-mod.com/mod/3140/bot-healing-fixes): ozen,
  version 1.0.0, advertised for SPT 4.1.6.
- [Release v1.0.0](https://github.com/ozen-m/SPT-BotHealingFixes/releases/tag/v1.0.0),
  tag commit `87bd68d14bab24d7d13e1c7b74a214e642fbab07`.
- Plugin identity: `com.ozen.bothealingfixes` / `Bot Healing Fixes`.
- Release ZIP SHA-256:
  `a0886b495e47b48b6da2f812438544a3ebb11b132050a602d770554cc2b670ff`.
- Released DLL SHA-256:
  `479a789147de11848ed890c5b3fe91e4a7c19cafe06147a67d9bbcd52239c230`.

[BotMedKitPatch](https://github.com/ozen-m/SPT-BotHealingFixes/blob/v1.0.0/BotHealingFixes/Patches/BotMedKitPatch.cs)
skips native `BotFirstAid.ApplyToSelf` and starts `ApplyMedicineAsync`, discarding
its task. [MedUtils](https://github.com/ozen-m/SPT-BotHealingFixes/blob/v1.0.0/BotHealingFixes/Utils/MedUtils.cs)
waits for hands setup and medicine completion, then calls
`_owner.WeaponManager.Selector.TakePrevWeapon()` without a lifetime check.
Both timeout and exception handlers also call it without a guard; the general
exception handler does so before logging. Its timeout token is not bound to bot
disposal or raid end. `OnEndApply` subsequently accesses owner settings and
`_owner.Medecine` outside the guarded block.

The downloaded release DLL was decompiled read-only and confirms these same
paths. Under the disposal state above, the replacement can fault too, including
inside its error handler. This is not a claim that the external mod has already
been reproduced failing in a live Arena raid.

Other patches address cancelled healing, medication selection and treatment
costs, very low remaining limb health, and inappropriate surgery body parts.
Those are separate from this callback-lifetime defect. The
[cancellation patch](https://github.com/ozen-m/SPT-BotHealingFixes/blob/v1.0.0/BotHealingFixes/Patches/CancelHealPatch.cs)
finishes an interrupted medicine operation; it does not provide the missing
disposal-aware continuation contract.

## Reuse and scope

The [MIT license](https://github.com/ozen-m/SPT-BotHealingFixes/blob/v1.0.0/LICENSE)
permits redistribution and modification with the copyright and license notice
retained (`Copyright (c) 2026 ozen-m`). Selective reuse is legally possible, but
copying these asynchronous methods would also copy the lifecycle gap.

The current Arena policy has no entry for this plugin. It remains unchanged.
Any future optional-mod admission requires separate native tests, especially
healing during bot retirement, protected exit, respawn and round teardown.
The downloaded package and decompilation are ignored local evidence under
`artifacts/bot-healing-fixes-review-20261025/`; no production binary changed.
