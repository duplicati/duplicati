using System;
using System.Collections.Generic;
using System.Text;

namespace Duplicati.Library.Snapshots
{
    static class Program
    {
        public static void Main(string[] args)
        {
            try
            {
                Dictionary<string, string> options = new Dictionary<string, string>();
                if (args.Length == 0)
                    args = new string[] { System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location) };

                if (args.Length == 2)
                {
                    string vssstring = null;
                    if (args[0].StartsWith("--vss-exclude-writers="))
                    {
                        vssstring = args[0].Substring("--vss-exclude-writers=".Length);
                        args = new string[] { args[1] };
                    }
                    else if (args[1].StartsWith("--vss-exclude-writers="))
                    {
                        vssstring = args[1].Substring("--vss-exclude-writers=".Length);
                        args = new string[] { args[0] };
                    }

                    if (vssstring != null)
                    {
                        if (vssstring.StartsWith("\""))
                            vssstring = vssstring.Substring(1);
                        if (vssstring.EndsWith("\""))
                            vssstring = vssstring.Substring(vssstring.Length - 1);
                        options["vss-exclude-writers"] = vssstring;
                    }
                }

                if (args.Length != 1)
                {
                    Console.WriteLine(@"Usage:
Duplicati.Library.Snapshots.exe [test-folder]

Where <test-folder> is the folder where files will be locked/created etc");
                    return;
                }

                if (!System.IO.Directory.Exists(args[0]))
                    System.IO.Directory.CreateDirectory(args[0]);

                string filename = System.IO.Path.Combine(args[0], "testfile.bin");

                Console.WriteLine(string.Format("Creating file {0}", filename));

                using (System.IO.FileStream fs = new System.IO.FileStream(filename, System.IO.FileMode.Create, System.IO.FileAccess.ReadWrite, System.IO.FileShare.None))
                {
                    Console.WriteLine(string.Format("Attempting to read locked file {0}", filename));

                    try
                    {
                        using (System.IO.FileStream fs2 = new System.IO.FileStream(filename, System.IO.FileMode.Create, System.IO.FileAccess.ReadWrite, System.IO.FileShare.None))
                        { }

                        Console.WriteLine(string.Format("Could open locked file {0}, cannot test", filename));
                        Console.WriteLine("* Test failed");
                        return;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine(string.Format("The file {0} was correctly locked, message: {1}", filename, ex.Message));
                    }

                    Console.WriteLine("Creating snapshot for folder: {0}", args[0]);
                    Console.WriteLine("If this fails, try to run as " + (Core.Utility.IsClientLinux ? "root" : "Administrator"));
                    using (ISnapshotService snapshot = SnapshotUtility.CreateSnapshot(new string[] { args[0] }, options))
                    {
                        Console.WriteLine("Attempting to read locked file via snapshot");
                        try
                        {
                            using (System.IO.Stream s = snapshot.OpenRead(filename))
                            { }

                            Console.WriteLine(string.Format("Could open locked file {0}, through snapshot", filename));
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine(string.Format("The file {0} was locked even through snapshot, message: {1}", filename, ex.ToString()));
                            Console.WriteLine("* Test failed");
                            return;
                        }
                    }
                }

                Console.WriteLine("* Test passed");

            }
            catch (Exception ex)
            {
                Console.WriteLine(string.Format("The snapshot tester failed: {0}", ex.ToString()));
                Console.WriteLine("* Test failed");
            }

        }
    }
}
