"""Read installed metadata only; no game assets are exported or modified."""
import pathlib
import sys
import UnityPy

root = pathlib.Path(sys.argv[1]) / "EscapeFromTarkov_Data/StreamingAssets/Windows"
shaders = {}
for obj in UnityPy.load(str(root / "shaders")).objects:
    if obj.type.name == "Shader" and obj.path_id in (-7805929693649049470, 5952114829660144477):
        form = obj.read_typetree()["m_ParsedForm"]
        shaders[obj.path_id] = form["m_Name"]
        assert {"_Color", "_Factor", "_IkFactor"} <= {p["m_Name"] for p in form["m_PropInfo"]["m_Props"]}
assert set(shaders.values()) == {"Custom/LaserBeam", "Custom/LaserPoint"}, shaders

visible = 0
for obj in UnityPy.load(str(root / "assets/systems/effects/laserbeam/laser.bundle")).objects:
    if obj.type.name != "Material":
        continue
    data = obj.read_typetree()
    if data["m_Name"].startswith(("LaserBeam", "LaserPoint")):
        assert dict(data["m_SavedProperties"]["m_Floats"])["_Factor"] > 0
        assert data["m_Shader"]["m_PathID"] in shaders
        visible += 1
assert visible >= 6

infrared = 0
for name in ("tactical_all_holosun_ls321.bundle", "tactical_all_insight_anpeq15.bundle", "tactical_all_zenit_2irs_kleh_lam.bundle"):
    for obj in UnityPy.load(str(root / "assets/content/items/mods/tactical" / name)).objects:
        if obj.type.name != "Material":
            continue
        data = obj.read_typetree()
        if data["m_Name"] in ("LaserBeamIk", "LaserPointIk"):
            floats = dict(data["m_SavedProperties"]["m_Floats"])
            assert floats["_Factor"] == 0 and floats["_IkFactor"] > 0
            assert data["m_Shader"]["m_PathID"] in shaders
            infrared += 1
assert infrared == 6
print(f"ARENA_TEAM_LASER_BUNDLES_PASSED ({visible} visible and {infrared} IR materials; native shader metadata)")
