using System.Threading.Tasks;

namespace Duplicati.CommandLine.SecretTool.Net8
{
    // Wrapper class to keep code independent
    public static class Program
    {
        public static Task<int> Main(string[] args)
            => Duplicati.CommandLine.SecretTool.Program.Main(args);
    }
}