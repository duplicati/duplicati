namespace Duplicati.CommandLine.SharpAESCrypt.Net8
{
    // Wrapper class to keep code independent
    public static class Program
    {
        public static void Main(string[] args)
            => global::SharpAESCrypt.SharpAESCrypt.CommandLineMain(args);
    }
}