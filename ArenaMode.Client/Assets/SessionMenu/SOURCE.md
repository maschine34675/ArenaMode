# Streiflicht training course

The Arena session uses the **Streiflicht** lighting study selected on 2026-11-10. The original course and its low morning light were generated specifically for ArenaMode with the built-in OpenAI image generator. The artwork ships under the project's MIT license. The [generation record](../../../tools/ArenaUiArtwork/SessionMenu/PROMPTS.json) contains the source prompts and master hashes.

The [approved 1680 × 720 crop](../../../tools/ArenaUiArtwork/SessionMenu/masters/streiflicht-approved-21x9.jpg) ships byte-for-byte as `splash.jpg` and supplies the visible colors for the menu. The three menu textures share that canvas. `hall.jpg` shows the course, `barrier.png` isolates the foreground concrete wall and both lifting eyes, and `fence.png` carries the three sandbags and portable target stand. The last filename retains its existing resource identity. Two matching repair plates supply the material revealed by mouse movement.

[Export script](../../../tools/ArenaUiArtwork/SessionMenu/Export-SessionMenuArt.ps1) · [Layer mattes and blending](../../../tools/ArenaUiArtwork/SessionMenu/export_streiflicht.py)

Rebuild with PowerShell 7 and Python with Pillow and NumPy. The recorded export used Python 3.12.14, Pillow 13.0.0 and NumPy 2.3.5.

```powershell
pwsh -File tools/ArenaUiArtwork/SessionMenu/Export-SessionMenuArt.ps1 -PreviewDirectory artifacts/session-menu-art/training-rollout
```

Use `-PythonExecutable` for a specific interpreter. The exporter compares the decoded neutral composite against the selected JPEG and produces optional centered 16:9 and 21:9 previews for all four movement extremes. The recorded mean absolute RGB error is 0.265 levels on a 0–255 scale. The 99th percentile is 2 and the maximum is 8.
