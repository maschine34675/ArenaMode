using ArenaMode.Shared.Session;
using System;

namespace ArenaMode.Session;

/// <summary>
/// Whether this game process is an Arena session (docs/ARENA-SESSION-20261021.md, sections 5 and 7):
/// a process started with the <c>-arena-session</c> command-line flag, in which the preloader gate
/// removed every plugin that the policy does not allow before BepInEx instantiated it. The gate
/// publishes what it did as a JSON report in the AppDomain data; this class only reads it.
///
/// Deliberately free of Core, Unity, EFT and BepInEx types: it is consulted from the rejected-Core
/// notice path as well, and the offline harnesses compile it on its own. Nothing here touches the
/// disk or the network; both inputs are read once and cached for the life of the process.
/// </summary>
internal static class ArenaSessionMode
{
    private static bool _loaded;
    private static bool _isArenaSession;
    private static ArenaGateReport _report;
    private static string _problem;

    /// <summary>True when the process was started with the <c>-arena-session</c> flag.</summary>
    internal static bool IsArenaSession
    {
        get { EnsureLoaded(); return _isArenaSession; }
    }

    /// <summary>
    /// The gate's report, or null when this is no Arena session, the gate published nothing, or
    /// what it published could not be parsed (<see cref="Problem"/> says which).
    /// </summary>
    internal static ArenaGateReport GateReport
    {
        get { EnsureLoaded(); return _report; }
    }

    /// <summary>
    /// True only in an Arena session whose gate ran to completion. False means nothing was
    /// filtered, so nothing proves that only allowed mods are loaded and Arena must refuse to run.
    /// </summary>
    internal static bool GateActive
    {
        get { EnsureLoaded(); return _isArenaSession && _report != null && _report.Active; }
    }

    /// <summary>
    /// Why <see cref="GateActive"/> is false in an Arena session (the gate's own error, a missing
    /// or unreadable report), or null when the gate is active or this is no Arena session.
    /// </summary>
    internal static string Problem
    {
        get { EnsureLoaded(); return _problem; }
    }

    /// <summary>
    /// Reads the real process state: the command line and the AppDomain data key. Called lazily by
    /// every property; a caller that needs the answer earlier may call it explicitly.
    /// </summary>
    internal static void EnsureLoaded()
    {
        if (_loaded) return;
        string[] args = null;
        string json = null;
        try { args = Environment.GetCommandLineArgs(); } catch (Exception) { }
        try { json = AppDomain.CurrentDomain.GetData(ArenaSessionContract.GateReportDataKey) as string; } catch (Exception) { }
        Initialize(args, json);
    }

    /// <summary>
    /// Sets the state from explicit inputs. Production code reaches this only through
    /// <see cref="EnsureLoaded"/>; the offline harness calls it to drive both session kinds.
    /// </summary>
    internal static void Initialize(System.Collections.Generic.IEnumerable<string> args, string gateReportJson)
    {
        _isArenaSession = ArenaSessionContract.HasSessionFlag(args);
        _report = null;
        _problem = null;
        if (_isArenaSession)
        {
            if (string.IsNullOrWhiteSpace(gateReportJson))
            {
                _problem = "the preloader gate did not publish a report";
            }
            else if (!ArenaGateReport.TryParse(gateReportJson, out var parsed, out var parseError))
            {
                _problem = "the preloader gate report is unreadable (" + parseError + ")";
            }
            else
            {
                _report = parsed;
                if (!parsed.Active)
                {
                    _problem = string.IsNullOrWhiteSpace(parsed.Error)
                        ? "the preloader gate is inactive"
                        : "the preloader gate is inactive: " + parsed.Error;
                }
            }
        }

        _loaded = true;
    }
}
