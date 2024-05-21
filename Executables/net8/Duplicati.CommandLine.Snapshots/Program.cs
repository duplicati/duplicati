namespace Duplicati.Server.Net8
{
    // Wrapper class to keep code independent
    public static class Program
    {
        public static void Main(string[] args)
            => Duplicati.Library.Snapshots.Program.Main(args);
    }
}