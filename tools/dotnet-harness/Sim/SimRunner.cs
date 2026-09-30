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
            if (which == "all" || which == "holdem")
                ok &= Holdem.LadderSim.Run(runs, Path.Combine(repoRoot, "md", "SIM_HOLDEM.md"));
            if (which == "holdem-v1") // the old 9-player table sim (no longer on the game path); record only
                ok &= Holdem.HoldemSim.Run(runs, Path.Combine(repoRoot, "md", "SIM_HOLDEM_V1.md"));
            if (which == "luck")
                ok &= LuckSim.Run(runs, Path.Combine(repoRoot, "md", "SIM_LUCK.md"));
            if (which == "ladder")
                ok &= Holdem.LadderEconSim.Run(runs, Path.Combine(repoRoot, "md", "SIM_LADDER_ECON.md"));
            if (which == "coinnet")
                ok &= CoinNetSim.Run(runs, Path.Combine(repoRoot, "md", "SIM_COIN_NET.md"));
            if (which == "drift")
                ok &= DriftSim.Run(runs, Path.Combine(repoRoot, "md", "SIM_DRIFT.md"));
            if (which == "mental")
                ok &= MentalSim.Run(runs, Path.Combine(repoRoot, "md", "SIM_MENTAL.md"));
            return ok ? 0 : 1;
        }
    }
}
