using Duplicati.Library.Crashlog;

namespace Duplicati.CommandLine.BackendTool.Net8
{
    // Wrapper class to keep code independent
    public static class Program
    {
        public static int Main(string[] args)
            => CrashlogHelper.WrapWithCrashLog(() => Duplicati.CommandLine.BackendTool.Program.Main(args));
    }
}