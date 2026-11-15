"""Read installed donor animation metadata. No game process or asset writes."""
import hashlib
import json
import os
from pathlib import Path
import UnityPy

root = Path(os.environ.get("SPT_GAME_ROOT", "D:/SPT41"))
bundle = root / "EscapeFromTarkov_Data/StreamingAssets/Windows/assets/content/weapons/usable_items/item_ranger_1500/item_ranger_1500_container.bundle"
env = UnityPy.load(str(bundle))
controllers = [obj.read_typetree() for obj in env.objects if obj.type.name == "AnimatorController"]
assert len(controllers) == 1, "one native donor animator required"
data = controllers[0]
names = dict(data["m_TOS"])
states = [entry["data"] for entry in data["m_Controller"]["m_StateMachineArray"][1]["data"]["m_StateConstantArray"]]
phases = {names[state["m_FullPathID"]]: state for state in states}

def transitions(state):
    return [
        {"to": names[states[entry["data"]["m_DestinationState"]]["m_FullPathID"]],
         "conditions": [(names[c["data"]["m_EventID"]], c["data"]["m_ConditionMode"])
                        for c in entry["data"]["m_ConditionConstantArray"]]}
        for entry in state["m_TransitionConstantArray"]
    ]

required = ("Hands.IDLE", "Hands.AIMING", "Hands.AIM_TO_IDLE", "Hands.LOOK", "Hands.CLOSE INVENTORY", "Hands.OUT TO IDLE")
assert all(name in phases for name in required), "native known ready and nonready phases required"
idle, aiming = transitions(phases["Hands.IDLE"]), transitions(phases["Hands.AIMING"])
assert any(t["to"] == "Hands.AIMING" and ("Fire", 1) in t["conditions"] for t in idle), "Fire=true must enter AIMING"
assert any(t["to"] == "Hands.AIM_TO_IDLE" and ("Fire", 2) in t["conditions"] for t in aiming), "Fire=false must lower"
assert any(t["to"] == "Hands.AIM_TO_IDLE" and ("Active", 2) in t["conditions"] for t in aiming), "native hide must lower"
assert any(t["to"] == "Hands.LOOK" and ("Look", 1) in t["conditions"] for t in idle), "inspect must be a distinct phase"
print(json.dumps({"controller": data["m_Name"], "bundleSha256": hashlib.sha256(bundle.read_bytes()).hexdigest(),
                  "phases": {name: {"fullPathHash": phases[name]["m_FullPathID"], "shortNameHash": phases[name]["m_NameID"]} for name in required},
                  "idleTransitions": idle, "aimingTransitions": aiming}))
print("TRANSLOCATOR_NATIVE_GRAPH_PASSED 6 contracts")
