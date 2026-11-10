# Private session presentation, 9 November 2026

Two isolated installations ran development clients and the direct-start helper with copied profiles and Core 1.0.22. The final client MVID is `d525a4ec-d857-40c7-9e79-72a781421d0c`, SHA-256 `F48CBCF0B55A5CAA584459ADE3C8556E1E43D87ECFB142E6E1B294BE70BD83AB`. The helper MVID is `5291d039-9de8-4932-8a01-554238ad3d11`.

GPU-1 started the private game through the session helper. A 1920 × 1080 desktop capture caught the Factory splash with EFT's native footer. The next loading panel showed the native profile-loading view. The game's bridge confirmed the private flag and the private backend on port 6970.

At 1920 × 1080 on client `ddd660d7…`, moving the cursor between x=120 and x=1800 changed the barrier position from +4.375 to −4.375 and the gate from +10.5 to −10.5. The character button stayed at (960, 729) and opened the inventory. There were zero backdrop instances in the inventory and a single new instance after returning to the menu. A mod notice remained visible and clickable above the image. The lit doorway crossed the native play button's lettering.

Client `e0856c06…` added the gradient behind the native labels. Captures at 1920 × 1080 and a real 1680 × 720 Unity viewport showed a full menu background and legible buttons. At 21:9 the barrier and gate reached ±4.65 and ±11.17 on the horizontal axis. The trading button stayed fixed. Opening Settings and returning rebuilt the artwork. The helper's own window displayed the same hall above its profile selector.

On that client a Factory Deathmatch and its native pause screen both had zero menu backdrops. Returning through the results restored one backdrop on the home menu. The post-raid profile differed only in `Hideout.sptUpdateLastRunTimestamp` and all 61 PitFireTeam files remained byte-identical.

The 21:9 startup capture exposed a separate problem. EFT centers one sharp 1280 × 720 image between two blurred margins in a 1680 × 720 window. Inspection found five stock image slots in the full-width fade group and a separate footer. The final client draws its own full-width image in that group. GPU-1's final 21:9 boot rendered internally at 1680 × 720 within a 1920 × 824 region of the desktop capture. After aligning that region with the source JPG, the sampled mean RGB difference was below 0.4 on a 0–255 scale at both edges and in the center. The full footer was visible.

GPU-2 started EFT normally with each client. The final client showed the stock soldier splash and forest main menu at 1920 × 1080. The running process reported both session flags false, neither art hook installed, and zero Arena boot or backdrop objects.

On GPU-1 a second private start at 1920 × 1080 confirmed the final centered 16:9 crop and complete native footer. The sampled difference from the expected crop was about 0.2 per RGB channel. Once the home menu opened, the private boot surface was gone and the native panel was inactive. The normal return path opened the ordinary game on port 6969 with zero Arena backdrops.

Offline, the full suite passed 3,737 tests with three expected skips. The artwork check verified the hashes of all four client resources and the helper image, decoded their common dimensions and checked transparency inside the 16:9 crop. The shipping .NET Framework helper passed all ten checks, including decoding its header, and all six mutants were rejected.

Both test games and servers exited. GPU-1 matched its three canonical profiles, four installed binaries, six pre-existing helper files and the original display settings to the baseline. GPU-2 removed its temporary installation and retained the running synchronization processes. Captures, pixel comparisons and restoration records are under the local `artifacts/session-menu-art/` directory.
