using Duplicati.Library.Crashlog;

namespace Duplicati.Service.Net8
{
    // Wrapper class to keep code independent
    public static class Program
    {
        public static int Main(string[] args)
            => CrashlogHelper.WrapWithCrashLog(() => Duplicati.Service.Program.Main(args));
    }
}