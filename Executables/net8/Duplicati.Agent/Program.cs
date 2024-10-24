using System.Threading.Tasks;

namespace Duplicati.Agent.Net8
{
    // Wrapper class to keep code independent
    public static class Program
    {
        public static Task<int> Main(string[] args)
            => Duplicati.Agent.Program.Main(args);
    }
}