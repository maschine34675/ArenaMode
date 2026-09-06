"""Inspect installed shaders in memory only. No shader code/assets are exported.

Uses Windows' D3D disassembler without creating a device or requiring a GPU.
Pinned bytecode hashes make a different shader require a fresh manual review.
"""
import ctypes
import hashlib
import os
import pathlib
import struct
import sys

import lz4.block
import UnityPy


REVIEWED = {
    "Custom/LaserBeam": (
        -7805929693649049470,
        "08afd29b01fc28e6b0c90199e5657d74fee2a538e019a5b23b62a57bf81e5b1f",
        "b49673673613d59ad3a554eac41bd24ddbd68938c1a68f7e8f88acbc3959870a",
    ),
    "Custom/LaserPoint": (
        5952114829660144477,
        "d53a65b7f5f02fd689f0f17790add5685472368c6100c75111df58877e21d18e",
        "15c7240a40b11e31f6f140067f7503aea7242d8819970c207e8b39c7b1b80e5c",
    ),
}


def disassemble(code):
    library = ctypes.WinDLL(str(pathlib.Path(os.environ["SystemRoot"]) / "System32/d3dcompiler_47.dll"))
    method = library.D3DDisassemble
    method.argtypes = [ctypes.c_void_p, ctypes.c_size_t, ctypes.c_uint, ctypes.c_char_p, ctypes.POINTER(ctypes.c_void_p)]
    method.restype = ctypes.c_long
    source, result = ctypes.create_string_buffer(code), ctypes.c_void_p()
    if method(source, len(code), 0, None, ctypes.byref(result)) != 0:
        raise AssertionError("Installed DXBC could not be disassembled")
    table = ctypes.cast(result, ctypes.POINTER(ctypes.POINTER(ctypes.c_void_p))).contents
    pointer = ctypes.WINFUNCTYPE(ctypes.c_void_p, ctypes.c_void_p)(table[3])
    size = ctypes.WINFUNCTYPE(ctypes.c_size_t, ctypes.c_void_p)(table[4])
    release = ctypes.WINFUNCTYPE(ctypes.c_ulong, ctypes.c_void_p)(table[2])
    try:
        return ctypes.string_at(pointer(result), size(result)).decode("utf-8").rstrip("\0")
    finally:
        release(result)


def offsets(shader_pass, stage):
    names = {index: name for name, index in shader_pass["m_NameIndices"]}
    globals_buffer = next(b for b in shader_pass[stage]["m_CommonParameters"]["m_ConstantBuffers"] if names[b["m_NameIndex"]] == "$Globals")
    return {names[p["m_NameIndex"]]: p["m_Index"] for p in globals_buffer["m_VectorParams"]}


seen = set()
root = pathlib.Path(sys.argv[1]) / "EscapeFromTarkov_Data/StreamingAssets/Windows/shaders"
for obj in UnityPy.load(str(root)).objects:
    if obj.type.name != "Shader" or obj.path_id not in {s[0] for s in REVIEWED.values()}:
        continue
    data = obj.read_typetree()
    name = data["m_ParsedForm"]["m_Name"]
    expected = REVIEWED[name]
    assert obj.path_id == expected[0]
    assert data["platforms"] == [4] and data["offsets"] == [[0]], "DX11 shader platform layout drift"
    raw = lz4.block.decompress(bytes(data["compressedBlob"]), uncompressed_size=data["decompressedLengths"][0][0])
    programs, cursor = [], 0
    while (offset := raw.find(b"DXBC", cursor)) >= 0:
        length = struct.unpack_from("<I", raw, offset + 24)[0]
        assert length > 32 and offset + length <= len(raw)
        programs.append(raw[offset:offset + length])
        cursor = offset + length
    assert len(programs) == 2
    assert [hashlib.sha256(code).hexdigest() for code in programs] == list(expected[1:]), "Native shader changed; review its instructions before changing the oracle"
    vertex, fragment = [disassemble(code) for code in programs]
    shader_pass = data["m_ParsedForm"]["m_SubShaders"][0]["m_Passes"][0]
    state = shader_pass["m_State"]
    assert state["zWrite"]["val"] == 0 and state["zTest"]["val"] == 4
    assert state["rtBlend0"]["srcBlend"]["val"] == 1 and state["rtBlend0"]["destBlend"]["val"] == 1
    if name == "Custom/LaserBeam":
        assert offsets(shader_pass, "progVertex")["_Intensity"] == 48
        assert {k: offsets(shader_pass, "progFragment")[k] for k in ("_Color", "_Factor", "_IkFactor", "_NightVisionOn", "_ThermalVisionOn")} == {
            "_Color": 32, "_Factor": 52, "_IkFactor": 56, "_NightVisionOn": 88, "_ThermalVisionOn": 92,
        }
        assert "add r0.w, cb0[3].x, l(-4.000000)" in vertex
        assert "mad o1.xyzw, v0.yyyy, r0.wwww, l(4.000000" in vertex
        assert "movc r0.y, r0.y, cb0[3].z, cb0[3].y" in fragment
        assert "mul r0.x, r0.y, r0.x" in fragment
        assert "mul r2.xyzw, v1.xyzw, cb0[2].xyzw" in fragment
        assert "mul o0.xyzw, r0.xxxx, r1.xyzw" in fragment
    else:
        assert offsets(shader_pass, "progFragment")["_Intensity"] == 48
        assert "mul r0.xyzw, r0.xyzw, cb0[3].xxxx" in fragment
    print(name + ": reviewed DX11 vertex/fragment hashes and intensity/factor contracts match")
    seen.add(name)
assert seen == set(REVIEWED)
print("ARENA_LASER_BEAM_SHADER_PASSED")
