# VisualAssist throw checks

`Verify.ps1 -PublishedDll <official VisualAssist 1.2.0 DLL>` compiles the actual
adapter against the installed game APIs, exercises its scope/owner/operation and
fresh-prediction decisions with controlled doubles, and kills twelve regressions.
The released-DLL check pins the bytes and actual reflected signatures. Its default
fixture is `artifacts/optional-mod-releases/visualassist-1.2.0/VisualAssist.dll`,
registered in the verification runner's content inputs and worker copies.
No foreign code or DLL is distributed. The harness does not simulate Unity or
Harmony patch execution: the native ordinary/quick-throw and respawn tests remain
separate acceptance gates.
