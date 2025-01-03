using Duplicati.Library.Crashlog;

namespace Duplicati.Server.Net8
{
    // Wrapper class to keep code independent
    public static class Program
    {
        public static int Main(string[] args)
            => CrashlogHelper.WrapWithCrashLog(() => Duplicati.Server.Program.Main(args));
    }
}