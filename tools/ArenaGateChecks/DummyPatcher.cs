// Template for the dummy preloader patchers of tools/ArenaGateChecks.
//
// Verify.ps1 compiles this once; GateChecks.cs copies the assembly under many names with Cecil,
// because BepInEx keys its patcher list by assembly name and orders the initializers by it. Every
// callback appends "<callback>:<assembly name>" to the list that the driver publishes in the
// AppDomain data "arena-gate-checks.calls", so a check can tell exactly which callbacks of which
// patcher ran. The patcher targets Target.dll in the fake Managed folder and changes nothing.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Mono.Cecil;

namespace DummyPatcher
{
    public static class Patcher
    {
        public static IEnumerable<string> TargetDLLs
        {
            get
            {
                Record("targets");
                return new[] { "Target.dll" };
            }
        }

        public static void Initialize()
        {
            Record("init");
        }

        public static void Patch(AssemblyDefinition assembly)
        {
            Record("patch");
        }

        public static void Finish()
        {
            Record("finish");
        }

        private static void Record(string callback)
        {
            IList calls = AppDomain.CurrentDomain.GetData("arena-gate-checks.calls") as IList;
            if (calls != null) calls.Add(callback + ":" + Assembly.GetExecutingAssembly().GetName().Name);
        }
    }
}
