r"""Read-only SPT 4.1 scene contract check. Requires UnityPy 1.25.3 (not bundled).

Usage: python Inspect-InstalledScene.py D:\SPT41\EscapeFromTarkov_Data
Never extracts or changes audio, game assets, or bundles.
"""
import hashlib
import struct
import sys
from pathlib import Path

import UnityPy
from UnityPy.classes import PPtr

data = Path(sys.argv[1])
scene_path = "Assets/Content/Locations/Labyrinth/Labyrinth_DesignStuff.unity"
build = UnityPy.load(str(data / "globalgamemanagers"))
settings = next(o.parse_as_dict() for o in build.objects if o.type.name == "BuildSettings")
index = settings["scenes"].index(scene_path)
scene_file = data / f"level{index}"
env = UnityPy.load(str(scene_file))
objects = list(env.objects)
scene = objects[0].assets_file


def script_name(obj):
    return obj.parse_monobehaviour_head().m_Script.deref().parse_as_dict()["m_ClassName"]


def head(obj):
    h = obj.parse_monobehaviour_head()
    assert h.m_Name == "", "Unexpected MonoBehaviour header"
    return h


def string_at(raw, offset):
    size = struct.unpack_from("<i", raw, offset)[0]
    offset += 4
    return raw[offset:offset + size].decode("utf-8"), (offset + size + 3) & ~3


players = [o for o in objects if o.type.name == "MonoBehaviour"
           and script_name(o) == "SyncLoopSoundPlayer"
           and head(o).m_GameObject.deref().parse_as_dict()["m_Name"] == "alarm_sound_root"]
assert len(players) == 1, "Expected one exact center siren player"
player = players[0]
# SPT 4.1 serialized MonoBehaviour header: GameObject PPtr (12), enabled
# with padding (4), script PPtr (12), empty name length (4). Native serialized
# field order comes from Audio.AudioCulling/SyncLoopSoundPlayer.cs.
raw = player.get_raw_data()
count = struct.unpack_from("<i", raw, 32)[0]
assert count == 7, "Alarm source layout changed"
refs = [struct.unpack_from("<iq", raw, 36 + i * 12) for i in range(count)]
assert all(file_id == 0 for file_id, _ in refs)
offset = 36 + count * 12
clip_file, clip_id = struct.unpack_from("<iq", raw, offset)
clip = PPtr(m_FileID=clip_file, m_PathID=clip_id, assetsfile=scene).deref().parse_as_dict()
assert clip["m_Name"] == "alarm_oneshot" and clip["m_Length"] == 4.0
offset += 24  # clip and output-mixer PPtrs
assert len(raw) == offset + 8
assert struct.unpack_from("<ii", raw, offset) == (0, 1), "Autoplay/delay contract changed"
root = head(player).m_GameObject.deref().parse_as_object()
root_transform = next(c.component.deref().path_id for c in root.m_Component
                      if c.component.deref().type.name == "Transform")
for _, source_id in refs:
    source = scene.objects[source_id].parse_as_object()
    assert not source.m_PlayOnAwake
    go = source.m_GameObject.deref().parse_as_object()
    transform = next(c.component.deref().parse_as_object() for c in go.m_Component
                     if c.component.deref().type.name == "Transform")
    visited = set()
    while transform.m_Father.path_id != root_transform:
        parent = transform.m_Father
        assert parent.m_FileID == 0 and parent.path_id and parent.path_id not in visited
        visited.add(parent.path_id)
        transform = parent.deref().parse_as_object()

ids = set()
handler_count = 0
for obj in objects:
    if obj.type.name != "MonoBehaviour" or script_name(obj) != "SyncableLoopSoundPlayer":
        continue
    if head(obj).m_GameObject.deref().parse_as_dict()["m_Name"] != "INTERACTIVE_Labyrinth_Boss_Alarm":
        continue
    handler_count += 1
    raw = obj.get_raw_data()
    trigger, pos = string_at(raw, 32)
    stop, pos = string_at(raw, pos)
    assert stop == "" and len(raw) == pos + 12
    assert struct.unpack_from("<iq", raw, pos) == (0, player.path_id)
    ids.add(trigger)
assert handler_count == 4 and ids == {
    "alarm_enter_3508252692", "alarm_enter_4274982985",
    "alarm_enter_336915922", "alarm_enter_3861355289",
}
print(f"Scene: {scene_path} ({scene_file.name})")
print(f"SHA256: {hashlib.sha256(scene_file.read_bytes()).hexdigest()}")
print(f"Player {player.path_id}; clip {clip['m_Name']}, {clip['m_Length']} seconds; "
      f"{handler_count} triggers, {count} owned sources; UnityPy {UnityPy.__version__}")
print("ARENA_LABYRINTH_SCENE_CONTRACT_VERIFIED")
