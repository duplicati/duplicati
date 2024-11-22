using Duplicati.Library.Crashlog;

namespace Duplicati.GUI.TrayIcon.Net8
{
    // Wrapper class to keep code independent
    public static class Program
    {
        public static int Main(string[] args)
            => CrashlogHelper.WrapWithCrashLog(() => Duplicati.GUI.TrayIcon.Program.Main(args));
    }
}