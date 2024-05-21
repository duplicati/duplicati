namespace Duplicati.Server.Net8
{
    // Wrapper class to keep code independent
    public static class Program
    {
        public static void Main(string[] args)
            => SharpAESCrypt.SharpAESCrypt.CommandLineMain(args);
    }
}