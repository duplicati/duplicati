namespace Duplicati.CommandLine.CLI.Net5
{
    // Wrapper class to keep code independent
    public static class Program
    {
        public static int Main(string[] args)
            => Duplicati.CommandLine.Program.Main(args);
    }
}