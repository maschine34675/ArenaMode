r"""Read-only entrance trap and toxic-pool contract verification; UnityPy 1.25.x, not bundled.
Usage: python Inspect-InstalledScene.py D:\SPT41\EscapeFromTarkov_Data
"""
import hashlib
import struct
import sys
from pathlib import Path
import UnityPy

data = Path(sys.argv[1])
scene_path = "Assets/Content/Locations/Labyrinth/Labyrinth_DesignStuff.unity"
build = UnityPy.load(str(data / "globalgamemanagers"))
settings = next(o.parse_as_dict() for o in build.objects if o.type.name == "BuildSettings")
scene_file = data / f"level{settings['scenes'].index(scene_path)}"
env = UnityPy.load(str(scene_file))
objects = list(env.objects)
scene = objects[0].assets_file
rows = []
for obj in objects:
    if obj.type.name != "MonoBehaviour":
        continue
    h = obj.parse_monobehaviour_head()
    script = h.m_Script.deref().parse_as_dict()["m_ClassName"]
    assert h.m_Name == "", "Unexpected serialized MonoBehaviour header"
    rows.append((script, h.m_GameObject.deref().parse_as_dict()["m_Name"], obj))


def text(raw, pos):
    length = struct.unpack_from("<i", raw, pos)[0]
    pos += 4
    return raw[pos:pos + length].decode(), (pos + length + 3) & ~3


entrance_rules = [
    ("Fire_zone_01", "flame", "493925125", "Flametrap", "soft"),
    ("Fire_zone_02", "flame", "1349880479", "Flametrap", "deadly"),
    ("Luquid_damage_zone_01", "Luquid", "3486811782", "Lifting_grate", "soft"),
    ("Luquid_damage_zone_02", "Luquid", "2773347541", "Lifting_grate", "deadly"),
    ("Steam_damage_zone_01", "steam", "115583983", "Valve", "soft"),
    ("Steam_damage_zone_02", "steam", "3704346085", "Valve", "deadly"),
]
damage_rules = [
    (name, f"{prefix}_enter_zone_{suffix}", f"{prefix}_exit_zone_{suffix}",
     f"INTERACTIVE_{config}_00001_{severity}")
    for name, prefix, suffix, config, severity in entrance_rules
] + [
    (f"DamageZone{i}", f"toxicZoneEnter{i}_2612876931", f"toxicZoneExit{i}_2612876931",
     f"INTERACTIVE_Labyrinth_water_01_{severity}")
    for i, severity in ((1, "soft"), (2, "deadly"))
]
starts = []
for name, expected_start, expected_finish, expected_config in damage_rules:
    matches = [o for s, n, o in rows if s == "HandlerDamage" and n == name]
    assert len(matches) == 1
    raw = matches[0].get_raw_data()
    start, pos = text(raw, 32)
    finish, pos = text(raw, pos)
    assert start == expected_start and finish == expected_finish
    count = struct.unpack_from("<i", raw, pos)[0]
    # DamageData = float damage + body-part enum (8 bytes), then damageCount/interval.
    configuration, end = text(raw, pos + 4 + count * 8 + 8)
    assert end == len(raw) and configuration == expected_config
    if name in ("DamageZone1", "DamageZone2"):
        assert count == 7 and struct.unpack_from("<if", raw, pos + 4 + count * 8) == (-1, 0.5)
    starts.append(start)

shoot_ids = {"guntrap_trigger_" + n for n in ("1628276106", "2131128863", "1446364582", "2505672369")}
found_shots = set()
for script, name, obj in rows:
    if script != "HandlerShoot":
        continue
    assert name == "INTERACTIVE_Guntrap_01"
    trigger, pos = text(obj.get_raw_data(), 32)
    configuration, _ = text(obj.get_raw_data(), pos)
    assert configuration == "INTERACTIVE_Guntrap_00001_" + ("deadly" if trigger.endswith("1446364582") else "soft")
    found_shots.add(trigger)
assert found_shots == shoot_ids

effect_ids = set()
for script, name, obj in rows:
    if script != "HandlerEffect" or name not in ("INTERACTIVE_Guntrap_01", "Luquid_damage_zone_01", "Luquid_damage_zone_02"):
        continue
    # HandlerEffect serializes _chance before its trigger ID.
    trigger, _ = text(obj.get_raw_data(), 36)
    assert trigger not in effect_ids
    assert trigger in (shoot_ids if name == "INTERACTIVE_Guntrap_01" else set(starts[2:4]))
    effect_ids.add(trigger)
assert effect_ids == shoot_ids | set(starts[2:4])
assert not any(s == "HandlerEffect" and n in ("DamageZone1", "DamageZone2") for s, n, _ in rows)

controls = {
    ("disable_traps_01", "INTERACTIVE_Toggle_Trap_Switch"): shoot_ids,
    ("disable_traps_02", "Interactive_button_12"): set(starts[:2]),
    ("disable_traps_04", "INTERACTIVE_Toggle_Trap_Switch"): set(starts[2:4]),
    ("set_valve_01", "Valve_PlaceHolder"): set(starts[4:6]),
    ("WaterRemove_2612876931", "DamageZone1"): {starts[6]},
    ("WaterRemove_2612876931", "DamageZone2"): {starts[7]},
}
seen = set()
for script, name, obj in rows:
    if script != "HandlerTriggerState":
        continue
    raw = obj.get_raw_data()
    trigger, pos = text(raw, 32)
    key = (trigger, name)
    if key not in controls:
        continue
    assert key not in seen
    seen.add(key)
    count = struct.unpack_from("<i", raw, pos)[0]
    pos += 4
    targets = []
    for _ in range(count):
        value, pos = text(raw, pos)
        targets.append(value)
    assert len(targets) == len(set(targets)) and set(targets) == controls[key]
    assert len(raw) == pos + 4 and struct.unpack_from("<i", raw, pos)[0] == 0
assert seen == controls.keys()

animations = {
    "Fire_zone_Animaton": ("Fire_zone_switch_01", "isGoing", 0),
    "Valve_Animation_Sync": ("set_valve_01", "HasValve", 1),
    "INTERACTIVE_Lifting_grate_01": ("LiftGate_843566315", "IsClosed", 1),
    "INTERACTIVE_Labyrinth_water_02": ("WaterRemove_2612876931", "IsRemoved", 1),
}
seen = set()
for script, name, obj in rows:
    if script != "HandlerAnimator" or name not in animations:
        continue
    raw = obj.get_raw_data()
    assert struct.unpack_from("<i", raw, 32)[0] == 1 and name not in seen
    seen.add(name)
    trigger, pos = text(raw, 36)
    parameter, pos = text(raw, pos)
    value = struct.unpack_from("<i", raw, pos)[0]
    assert (trigger, parameter, value) == animations[name]
    int_name, pos = text(raw, pos + 4)
    float_name, pos = text(raw, pos + 4)
    trigger_name, pos = text(raw, pos + 4)
    assert int_name == float_name == trigger_name == "" and len(raw) == pos + 12
    file_id, target_id = struct.unpack_from("<iq", raw, pos)
    assert file_id == 0
    assert scene.objects[target_id].parse_as_object().m_GameObject.path_id == obj.parse_monobehaviour_head().m_GameObject.path_id
    if name == "INTERACTIVE_Labyrinth_water_02":
        pool_animator = scene.objects[target_id].parse_as_object()
assert seen == animations.keys()

loops = []
pool_one_shots = 0
for script, name, obj in rows:
    if script != "HandlerPlaySoundSequenced":
        continue
    raw = obj.get_raw_data()
    start, pos = text(raw, 32)
    stop, pos = text(raw, pos)
    if name not in ("Fire_zone_01", "Fire_zone_02", "Gas_sound_01", "INTERACTIVE_Labyrinth_water_02"):
        continue
    if name == "INTERACTIVE_Labyrinth_water_02" and start:
        assert start == "WaterRemove_2612876931" and stop == ""
        assert struct.unpack_from("<ii", raw, pos) == (0, 1)
        pool_one_shots += 1
        continue
    expected_stop = "disable_traps_02" if name.startswith("Fire") else "set_valve_01" if name == "Gas_sound_01" else "WaterRemove_2612876931"
    assert start == "" and stop == expected_stop
    autoplay, once, start_fade, end_fade = struct.unpack_from("<iiff", raw, pos)
    assert autoplay == once == 1 and 0 <= end_fade <= 0.11
    loops.append(name)
assert sorted(loops) == ["Fire_zone_01", "Fire_zone_02", "Gas_sound_01", "Gas_sound_01", "INTERACTIVE_Labyrinth_water_02"]
assert pool_one_shots == 2
assert not any(s == "FlameDamageTrigger" for s, _, _ in rows)

# Follow the installed pool animator's actual reference rather than merely
# checking a parameter name: its true condition enters the removal clip.
controller = pool_animator.m_Controller.deref().parse_as_dict()
assert controller["m_Name"] == "Labyrinth_water_02"
names = dict(controller["m_TOS"])
machine = controller["m_Controller"]["m_StateMachineArray"][0]["data"]
states = machine["m_StateConstantArray"]
initial = states[machine["m_DefaultState"]]["data"]
assert names[initial["m_NameID"]] == "still"
assert len(initial["m_TransitionConstantArray"]) == 1
transition = initial["m_TransitionConstantArray"][0]["data"]
conditions = transition["m_ConditionConstantArray"]
assert len(conditions) == 1 and conditions[0]["data"]["m_ConditionMode"] == 1
assert names[conditions[0]["data"]["m_EventID"]] == "IsRemoved"
removed = states[transition["m_DestinationState"]]["data"]
assert names[removed["m_NameID"]] == "REMOVED" and not removed["m_TransitionConstantArray"]
assert len(controller["m_AnimationClips"]) == 1
trees = removed["m_BlendTreeConstantArray"]
assert len(trees) == 1
nodes = trees[0]["data"]["m_NodeArray"]
assert len(nodes) == 1 and nodes[0]["data"]["m_ClipID"] == 0 and not nodes[0]["data"]["m_ChildIndices"]
clip_ref = pool_animator.m_Controller.deref().parse_as_object().m_AnimationClips[0]
clip = clip_ref.deref().parse_as_dict()
assert clip["m_Name"] == "Labyrinth_water_02_REMOVED"
assert clip["m_Events"] and all(event["functionName"] == "StopSound" for event in clip["m_Events"])
assert clip["m_ClipBindingConstant"]["genericBindings"], "Removal clip has no animated scene state"
print(f"Scene: {scene_file.name}; SHA256 {hashlib.sha256(scene_file.read_bytes()).hexdigest()}")
print("12 trap/pool hazards, 6 associated health-effect handlers (none on pool), 6 native disable controls, 4 boolean animator actions, 5 continuous audio loops; no FlameDamageTrigger in this scene.")
print("Toxic pool: exact shallow/deep handlers; WaterRemove disables both; IsRemoved enters native REMOVED clip; only StopSound animation events; 2 drain one-shots are not emitted.")
print("ARENA_LABYRINTH_TRAP_SCENE_VERIFIED")
