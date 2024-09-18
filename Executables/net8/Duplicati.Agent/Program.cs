using System.Threading.Tasks;

namespace Duplicati.Server.Net8
{
    // Wrapper class to keep code independent
    public static class Program
    {
        public static Task<int> Main(string[] args)
            => Duplicati.Agent.Program.Main(args);
    }
}