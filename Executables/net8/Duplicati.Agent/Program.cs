using System.Threading.Tasks;
using Duplicati.Library.Crashlog;

namespace Duplicati.Agent.Net8
{
    // Wrapper class to keep code independent
    public static class Program
    {
        public static Task<int> Main(string[] args)
            => CrashlogHelper.WrapWithCrashLog(() => Duplicati.Agent.Program.Main(args));
    }
}