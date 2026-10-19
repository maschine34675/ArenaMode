# Bot effect ownership/renderer checks

`pwsh -NoProfile -File tools/BotEffectOutlineChecks/Verify.ps1` compiles the real
production lease registry, presentation adapters and renderer with explicit
native boundary doubles. The checks execute actual scope, body/health identity,
camera switching, geometry admission, frame expiry, resource recording and
failure/cleanup paths. Nine independently compiled defects must fail their
corresponding assertions. Production is rerun afterwards; source hashes must
remain unchanged.

The doubles record render commands and never pretend to rasterize pixels, run
native skinning/LOD selection or implement Unity's depth/upscaling pipeline.
The real Unity shader build, actual client NoDeploy build and GPU cover tests
are independent evidence. Do not label this suite visual acceptance.
