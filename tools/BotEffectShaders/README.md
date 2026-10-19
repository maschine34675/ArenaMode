# Independent Arena bot effect shader

Run `pwsh -NoProfile -File tools/BotEffectShaders/Build.ps1` from the repo root.
The script uses Unity 2022.3.43f1 in batch mode, compiles one private Windows64
shader bundle and copies only that bundle into `ArenaMode.Client/Assets/BotEffects`.
Unity's project/cache stays within this folder. Logs and raw bundle manifests
stay in ignored `artifacts/bot-effects-20261019/shader-build`.

The client embeds the bundle. Ordinary client builds need no Unity editor and
must not independently download a replacement shader. If shader sources change,
rebuild this bundle before building the client. The build script refreshes
`Assets/BotEffects/build-manifest.json` with source/binary hashes automatically;
the hashed source files are pinned to LF for stable Windows checkouts.

Pass 0 records only depth-visible existing body geometry. Pass 1 colors a narrow
inner contour, with a second depth rejection and no positive depth tolerance.
Multiple active effects use fixed color segments in the same contour. There is
no outside halo, hardware stencil ownership, x-ray mode or cloned native body.
The full-float mask preserves depth precision rather than quantizing it to half
precision between the two tests. Native camera/DLSS and cover acceptance remain
separate GPU gates; source/build checks cannot certify viewport alignment.
