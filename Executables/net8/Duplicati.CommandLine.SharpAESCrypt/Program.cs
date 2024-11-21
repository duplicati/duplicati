using System;
using Duplicati.Library.Crashlog;

namespace Duplicati.CommandLine.SharpAESCrypt.Net8
{
    // Wrapper class to keep code independent
    public static class Program
    {
        public static int Main(string[] args)
            => CrashlogHelper.WrapWithCrashLog(() =>
            {
                global::SharpAESCrypt.Program.CommandLineMain(args);
                return Environment.ExitCode;
            });
    }
}