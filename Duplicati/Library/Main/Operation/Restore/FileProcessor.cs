using System;
using System.Threading.Tasks;
using CoCoL;

namespace Duplicati.Library.Main.Operation.Restore
{
    internal class FileProcessor
    {
        public static Task Run()
        {
            return AutomationExtensions.RunTask(
            new
            {
                Input = Channels.filesToRestore.ForRead,
            },
            async self =>
            {
                //while (true)
                {
                    var file = await self.Input.ReadAsync();
                    if (file == null)
                    {
                        //break;
                    }
                    Console.WriteLine($"Got file to restore: '{file.Path}', {file.Length} bytes, {file.Hash}");
                }
            });
        }
    }
}