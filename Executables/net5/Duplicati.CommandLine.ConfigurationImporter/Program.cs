namespace Duplicati.CommandLine.ConfigurationImporter.Net5
{
    // Wrapper class to keep code independent
    public static class Program
    {
        public static int Main(string[] args)
            => Duplicati.CommandLine.ConfigurationImporter.ConfigurationImporter.Main(args);
    }
}