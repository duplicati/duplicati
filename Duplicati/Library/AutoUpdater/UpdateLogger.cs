using System;
using System.Globalization;
using System.IO;

namespace Duplicati.Library.AutoUpdater
{
    public static class UpdateLogger
    {
        public static bool LogMessages = true;
        private static string LogFile => "update.log";
        private static string LogPath => System.IO.Path.Combine(UpdaterManager.INSTALLDIR, LogFile);

        public static void Log(string message)
        {
            if (!LogMessages) return;

            string content = string.Empty;
            try
            {
                content = File.ReadAllText(LogPath);
            }
            catch (Exception)
            {
                // ignored
            }

            File.WriteAllText(LogPath, content + $"\n{DateTime.Now.ToString(CultureInfo.InvariantCulture)}: " + message);
        }
    }
}
