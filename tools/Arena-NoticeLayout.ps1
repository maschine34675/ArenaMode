# Exact mod-owned paths only; shared by payload, migration and inventory checks.
function Get-ArenaNoticeMoves {
    $moves = [ordered]@{}
    foreach ($name in @('ANNOUNCER-ADDITIONS-20260917.json', 'ANNOUNCER-PACK.md', 'ANNOUNCER-SOURCES.json',
        'AUDIO-LICENSES.md', 'CC-BY-3.0.txt', 'HUD-ICON-LICENSES.md')) { $moves[$name] = "docs/$name" }
    foreach ($name in @('NOTICE.md', 'SOURCES.json', 'MIT-LICENSE.txt', 'CC-BY-3.0.txt')) { $moves["Ui/$name"] = "docs/licenses/ui/$name" }
    foreach ($name in @('CC0-1.0.txt', 'SOURCES.json')) { $moves["hitmarkers/$name"] = "docs/licenses/hitmarkers/$name" }
    foreach ($name in @('CC0-1.0.txt', 'README.md', 'SOURCES.json')) { $moves["pickups/$name"] = "docs/licenses/pickups/$name" }
    foreach ($name in @('ATTRIBUTION.md', 'MAP-SOURCES.json', 'customs-ground.png',
        'Source/Customs-Ground_Level.svg', 'Source/Shebuka-LICENSE-MODIFIED BY ACIDPHANTASM.md')) {
        $moves["MapPreviews/$name"] = "docs/licenses/map-previews/$name"
    }
    return $moves
}
function Get-ArenaRetiredNotices {
    return @('hitmarkers/KENNEY-IMPACT-LICENSE.txt', 'hitmarkers/KENNEY-INTERFACE-LICENSE.txt',
        'hitmarkers/.gitattributes', 'pickups/.gitattributes')
}
