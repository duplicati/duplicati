using System.Threading.Tasks;

namespace Duplicati.CommandLine.ServerUtil.Net8
{
    // Wrapper class to keep code independent
    public static class Program
    {
        public static Task<int> Main(string[] args)
            => Duplicati.CommandLine.ServerUtil.Program.Main(args);
    }
}