# Bot effect shader provenance

`bot-effect-outlines.bundle` contains ArenaMode's independently authored
`ArenaMode/VisibleBotEffects` shader. Its source and reproducible Unity editor
build are in `tools/BotEffectShaders/`; `build-manifest.json` records the exact
source and bundle hashes. No BreakoutOutlines source or compiled asset is shipped.

The bundle is embedded in the client DLL, not loaded from another mod. It adds
no plugin dependency or loose installation file. Build with Unity 2022.3.43f1
for StandaloneWindows64. Shader compilation is not native rendering acceptance.
