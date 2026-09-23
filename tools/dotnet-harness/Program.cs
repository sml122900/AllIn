using System;
using System.IO;
using System.Linq;
using NUnitLite;

namespace AllIn.Harness
{
    public static class Program
    {
        public static int Main(string[] args)
        {
            string cmd = args.Length > 0 ? args[0] : "test";
            string[] rest = args.Skip(1).ToArray();
            switch (cmd)
            {
                case "test":
                    return new AutoRun(typeof(Program).Assembly).Execute(rest.Concat(new[] { "--noresult" }).ToArray());
                case "sim":
                    return Sim.SimRunner.Run(rest, RepoRoot());
                default:
                    Console.Error.WriteLine("usage: dotnet run --project tools/dotnet-harness -- (test [nunitlite args] | sim [loop|holdem] [--runs N])");
                    return 2;
            }
        }

        /// <summary>Walks up from the working directory to the folder that holds Assets/ (the Unity project root).</summary>
        public static string RepoRoot()
        {
            var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
            while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "Assets"))) dir = dir.Parent;
            if (dir == null) throw new InvalidOperationException("run from inside the Unity project");
            return dir.FullName;
        }
    }
}
