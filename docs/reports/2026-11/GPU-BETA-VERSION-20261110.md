# Private home beta panel and Arena raid corner label, 10 November 2026

The candidate is the ArenaMode client with MVID `3da5e113-905d-4662-82e1-bd373aebad3b` and SHA-256 `F6EC86182E8559A791A5C4D33AC50A932E79F0ED7FA6145BF552BB3230C70581`.

The previous build showed the orange warning in the private home menu and the SPT version, local session and mode at the lower left during a Factory raid. The installed EFT scenes identify the targets as `MenuScreen/BetaWarningPanel` and `Preloader UI/BottomPanel/Content/UpperPart/AlphaLabel`. The reconnect panel is a different object. The version label has a single text field, with the notifier beside it. [Visibility and ownership](../../reviews/SESSION-MENU-ART-20261109.md#beta-notice-and-raid-version-line)

On GPU-1 the private 1920 × 1080 home menu showed the training course and its controls with the orange area gone. The corner version remained visible. The inspector found Arena's transparent group on the beta panel with raycasts off. In the Factory Deathmatch the living player's HUD showed the quick slots, score, health, stance and ammunition while the version line was hidden. After a native death and respawn onto a new body the same corner label remained masked. Its native text still held the current SPT version, session ID and mode.

At the result screen the raid mask was released and the current version was visible again. The private home then had only its beta mask. Returning through the session helper to the normal home menu restored the orange panel and the version line with both masks absent. The post-raid profile differed only in the hideout bookkeeping timestamp and all 61 PitFireTeam files matched their original hashes.

GPU-2 loaded the same client into an ordinary SPT process. The native orange banner and corner version appeared in the 1920 × 1080 home menu. A normal Factory PMC raid showed the SPT 4.1.6, LOCAL and PvE line in pixels. The running client reported Arena inactive and unarmed with both masks absent.

Offline verification passed 3,737 tests with three fixture-gated skips. The artwork package check passed. Six additional cases linked the actual visibility code and exercised foreign owners, rejected starts, replaced labels, missed raid ends, destroyed worlds and shutdown. A deliberate listener leak failed the destroyed-world case. The client uses the existing helper and Core 1.0.22. Local screenshots and runtime observations are under `artifacts/beta-version-20261110/gpu1/` and `gpu2/`.
