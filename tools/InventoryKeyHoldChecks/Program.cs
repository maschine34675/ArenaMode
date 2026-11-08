using ArenaMode.Hud;

int checks = 0;
void Need(bool ok, string reason) { checks++; if (!ok) throw new Exception("FAIL: " + reason); }

// The threshold is the live Arena feel: a quarter second separates a tap from a hold.
Need(Math.Abs(InventoryKeyHold.HoldSeconds - 0.25f) < 0.0001f, "hold threshold is a quarter second");

var hold = new InventoryKeyHold();
Need(!hold.Pressed && !hold.Held, "fresh state has no press and no table");
Need(!hold.Update(1f, true) && !hold.Update(1.5f, false), "key activity without a seen press forwards nothing");

// A hold: the table appears once the threshold passes and the release forwards nothing.
hold.Press(10f);
Need(hold.Pressed && !hold.Held, "a press is pending and shows no table yet");
Need(!hold.Update(10.1f, true) && !hold.Held, "early frames of a press show no table");
Need(!hold.Update(10.24f, true) && !hold.Held, "the table waits for the full threshold");
Need(!hold.Update(10.25f, true) && hold.Held, "held once the threshold passes");
Need(!hold.Update(10.9f, true) && hold.Held, "the table stays while the key stays down");
Need(!hold.Update(11f, false), "releasing a hold forwards no tap");
Need(!hold.Pressed && !hold.Held, "release clears the pending press and hides the table");
Need(!hold.Update(11.1f, false), "tap forwarded at most once");

// A tap: released before the threshold, forwarded exactly once.
hold.Press(20f);
Need(!hold.Update(20.1f, true) && !hold.Held, "a short press shows no table");
Need(hold.Update(20.2f, false), "short release is a tap");
Need(!hold.Pressed && !hold.Held, "a tap leaves nothing pending");
Need(!hold.Update(20.3f, false), "tap forwarded at most once");

// Release exactly at the threshold counts as a hold, so the inventory never pops after the table showed.
hold.Press(30f);
Need(!hold.Update(30.25f, false), "release at the threshold is a hold, not a tap");
hold.Press(40f);
Need(!hold.Update(41f, false), "a long press released between frames is still a hold");

// Cancel (screen change, teardown) hides the table and forwards nothing.
hold.Press(50f);
Need(!hold.Update(50.3f, true) && hold.Held, "held before the cancel");
hold.Cancel();
Need(!hold.Pressed && !hold.Held, "cancel hides the table");
Need(!hold.Update(50.4f, false), "cancel forwards nothing on the following release");

// A second press restarts the clock.
hold.Press(60f);
Need(!hold.Update(60.2f, true), "first press pending");
hold.Press(60.2f);
Need(!hold.Update(60.3f, true) && !hold.Held, "a re-press restarts the hold clock");
Need(!hold.Update(60.5f, true) && hold.Held, "the restarted clock reaches the threshold");
Need(!hold.Update(60.6f, false), "release after the restarted hold forwards nothing");

Console.WriteLine($"PASS: {checks} inventory key hold checks.");
Console.WriteLine("INVENTORY_KEY_HOLD_CHECKS_PASSED");
