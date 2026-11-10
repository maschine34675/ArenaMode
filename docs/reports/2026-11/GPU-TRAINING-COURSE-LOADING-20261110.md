# Training course and private menu loading, 10 November 2026

The selected Streiflicht preview has SHA-256 `E517BC6C9494CD2C32FF3AA2D8937F2E41C5592EA46292F9CFDB005A47EF51C5`. The picker and boot resources match those bytes. The three decoded menu layers at rest differed from the selected image by a mean of 0.265 on the 0–255 channel scale. The 99th percentile was two levels and the maximum was eight. Movement checks covered all four cursor extremes at 16:9 and 21:9.

GPU-1 used a separate installation and profile copy with EFT's Wood environment selected. A continuous 60 fps desktop recording of the previous client (`d525a4ec…`) captured the original problem. Over three image regions, 2,326 consecutive frames matched the Forest reference while the profile-loading view was visible, about 38.8 seconds.

The candidate client MVID was `ae56810d-8a55-4559-9d62-677ec29ee87d` (SHA-256 `FC435FDC762E55CF5CD3F69B7FB0C47EF9682EAC3B1BC751FDADCE3C7405F132`). The helper MVID was `c2e10b90-4496-40eb-a69a-4eedffbabe11`.

The candidate recording contained 15,549 frames at 60 fps and no matches for the Forest reference with the same threshold. Its highest Forest score was 0.319. A continuous 2,774-frame interval matched the course image at a threshold of 0.90, including the native profile-loading message and spinner. The runtime inspector found the course beneath the loading UI with Wood still selected in EFT. Once the native home menu was ready, exactly one parallax backdrop remained and the loading surface was gone. A modal could be acknowledged, the character screen opened, and returning rebuilt the backdrop. `before-loading.mp4` and `after-loading.mp4` are continuous excerpts.

After a Factory Deathmatch, frame 5246 still showed the native results screen. Frame 5247 showed the course, followed by 1,628 consecutive loading-image matches. Separate templates distinguished the native results art from the old profile-loading background. The loading canvas was gone when the home menu was ready. The whole-profile comparison found only the hideout bookkeeping timestamp, and all 61 PitFireTeam files matched their starting hashes. `after-factory.mp4` records the handoff.

The regular Return action restarted EFT on the normal backend with the same client. At 1920 × 1080 the native Wood scene showed behind the profile-loading text and in the home menu. The capture contained 3,841 Forest matches at the same 0.85 threshold. The ordinary home menu had zero Arena loading surfaces and zero Arena backdrops.

After the isolated runs, GPU-1 matched its three canonical profiles, four original binaries, six helper files, 18 display values and two global settings files to its pre-run baseline. All test processes had exited before deployment.

The offline suite passed 3,737 tests with three expected fixture skips. The package check decoded all four client resources, recomposed the layers and verified the helper's image. The .NET Framework helper passed ten checks and all six mutants were rejected. Local runtime verification artifacts are under `artifacts/session-loading-20261110/gpu1/`; artwork comparisons are under `artifacts/session-menu-art/training-rollout/`.
