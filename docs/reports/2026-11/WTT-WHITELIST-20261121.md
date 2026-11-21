# Installed WTT versions and Arena admission

21 November 2026. Source/installed-binary review, with narrowly reused native evidence from 20 November; no new native test is claimed here.

## Content Backport and CommonLib

The default client and server reviewed versions are updated to Content Backport **2.0.7** and CommonLib **3.0.8**, matching both installed runtime metadata and the current Forge listings. Backport's server requires CommonLib `~3.0.7`, so the installed 3.0.8 satisfies it. The existing Backport patcher entry stays unchanged. The MainMenu DLL is a Unity support assembly, not a separate BepInEx plugin.

The installed Backport client patches the menu environment, Hideout mannequin animator and per-instance firearm animation selection. It does not cache the local raid body. Its prepatcher adds environment enum values; its server registers content/quests and profile customization support. No new shared body-replacement repair was identified in those paths.

CommonLib has a cached player field used by its developer statistics/object-edit/export commands. Those developer tools are not covered by this review's gameplay acceptance; their retained-body behavior remains a separate follow-up. Normal gameplay paths inspected use their player instance or the current world/player owner. The optional Fika helper is not loaded in the solo test installation.

The exact installed client hashes below match `Redeploy/artifacts/body-compat-20261120/native/arena-b-loaded.json`, whose running process reported matching loaded/disk MVIDs and successful Awake for both mods. That run exercised Arena Factory respawns, walking/shooting and map rebinding, with profile/config/PFT preservation verified. It was **not** a feature-by-feature WTT test; no new gameplay or asset compatibility guarantee is implied by refreshing the reviewed versions.

| DLL | Runtime version | SHA-256 |
| --- | --- | --- |
| WTT-ContentBackportClient | 2.0.7 | `6b886cd3d293ee02f212887cfe8e51f6eca3dffc7804fc1af683e0b20cf6f679` |
| WTT-ContentBackport server | 2.0.7 | `ff3ea2bb3e9651213381601cd5744629958678311e8cebd802bb062f52eeae8b` |
| WTT-ClientCommonLib | 3.0.8 | `94c15284316880bd1552fce4d064e416ec40dd549b7fb597c0f5748dbf901a09` |
| WTT-ServerCommonLib | 3.0.8 | `5ef4ca27d442fcd15d7ff70128346ad6e23053115b973e6b07dbe30fbb5a8e20` |

## Armory: do not treat it as content-only

Forge names the package **3.0.0**, but both installed assemblies identify themselves as **2.0.5**, GUID `com.wtt.armory`. The client is `78563135fb3c280e87c4e2b6e3b270811a685912f85a2a3940ca8583db3383e9`; the server is `a323ee985d72dd2f9a185e88ba1dc4cf7e24cd3df3211867bca8b61a3b38653f`. Admission must use actual runtime metadata, not the package title.

The client contains five Hyperburst patches. `Plugin.Update` acquires `You` only when the cached Unity reference becomes null. Core can retain the previous physical body, so `UpdateHyperburst` continues using the old `HandsController/autoFireOn` while the patches count shots on the new firearm controller. The global shot timer, firing state, recoil and rate-of-fire counters can therefore fail to reset, or be reset by the old controller. This affects the AN-94 and AN-94-K. There is no existing Armory/Hyperburst adapter in Core.

Armory is **not newly whitelisted** by this version-only change. Recommended prerequisite: an exact-owner Core player rebind and transient burst-state cleanup, followed by AN-94 firing before/after a real respawn while retaining the old body. This is a code-level finding, not a reproduced native failure.

The server registers content and a chatbot at startup. Its welcome gift is triggered through `/client/friend/list` or an explicit `welcomegift` chat command, not Core respawn/raid end. It writes mail/dialog data and separate `user/profileData/<id>/WTT-Armory.json` through SPT's ProfileDataService; a later native test must include that file in the preservation audit. No concrete additional server respawn repair was identified.

## Sources and evidence

The independent read-only review found no further issue in this version-only change. `tools/Run-ArenaVerification.ps1 -ChangedSince 35775371` passed all **51 steps**, zero failures and zero cached results; xunit passed **4,422** tests with the three known environment skips. The no-deploy runner confirmed that installed binaries were unchanged. No new native session was started for this metadata update.

Only the default policy JSON was deployed afterward. VM/GPU1/GPU2 all match SHA-256 `41cd97c37174093365a3f27c901e6d244cbdd719b4f3eb2e404c7c55849ac8a0`; user policy files remain absent as before. Receipts: `artifacts/wtt-whitelist-20261121/deployment.json`. The Armory Core implementation/native test was then explicitly authorized as the next separate change.

- [Armory on Forge](https://sp-mod.com/mod/2246/wtt-armory)
- [Content Backport on Forge](https://sp-mod.com/mod/2512/wtt-content-backport)
- [CommonLib on Forge](https://sp-mod.com/mod/2310/wtt-commonlib)
- Installed assembly decompilation and offline check logs: ignored `artifacts/wtt-whitelist-20261121/`.
- [Prior native body-compatibility run](DYNAMICMAPS-BODY-COMPAT-20261120.md).
