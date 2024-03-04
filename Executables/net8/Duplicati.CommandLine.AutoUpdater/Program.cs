namespace Duplicati.CommandLine.AutoUpdater.Net8
{
    // Wrapper class to keep code independent
    public static class Program
    {
        public static int Main(string[] args)
            => Duplicati.Library.AutoUpdater.Program.Main(args);
    }
}