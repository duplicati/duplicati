using System.Runtime.Versioning;
using Duplicati.Library.Crashlog;

namespace Duplicati.WindowsService.Net8
{
    // Wrapper class to keep code independent
    public static class Program
    {
        public static int Main(string[] args)
            => CrashlogHelper.WrapWithCrashLog(() => Duplicati.WindowsService.Program.Main(args));
    }
}