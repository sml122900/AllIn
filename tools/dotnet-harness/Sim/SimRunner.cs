using System;
using System.IO;

namespace AllIn.Harness.Sim
{
    public static class SimRunner
    {
        /// <summary>sim [loop|holdem] [--runs N]. Exit code 1 if any acceptance criterion fails.</summary>
        public static int Run(string[] args, string repoRoot)
        {
            string which = "all";
            int runs = 1000;
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--runs" && i + 1 < args.Length) runs = int.Parse(args[++i]);
                else which = args[i];
            }

            bool ok = true;
            if (which == "all" || which == "loop")
                ok &= LoopSim.Run(runs, Path.Combine(repoRoot, "md", "SIM_REPORT.md"));
            return ok ? 0 : 1;
        }
    }
}
