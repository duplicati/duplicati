// Copyright (C) 2025, The Duplicati Team
// https://duplicati.com, hello@duplicati.com
// 
// Permission is hereby granted, free of charge, to any person obtaining a 
// copy of this software and associated documentation files (the "Software"), 
// to deal in the Software without restriction, including without limitation 
// the rights to use, copy, modify, merge, publish, distribute, sublicense, 
// and/or sell copies of the Software, and to permit persons to whom the 
// Software is furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in 
// all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS 
// OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, 
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE 
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER 
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING 
// FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER 
// DEALINGS IN THE SOFTWARE.

using System;
using System.IO;

namespace Duplicati.WindowsModulesLoader.Net8;

public static class Program
{
    public static void Main(string[] args)
    {
        Console.WriteLine("This executable is not used, it is just for pulling in dependencies");

        try
        {
            Console.WriteLine("Create AlphaVSS snapshot reflected");
            using var _ = Library.Snapshots.Windows.WindowsShimLoader.GetSnapshotProvider(Library.Snapshots.WindowsSnapshotProvider.AlphaVSS);
            Console.WriteLine("Created AlphaVSS snapshot reflected");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to create AlphaVSS snapshot reflected: {ex}");
        }

        try
        {
            Console.WriteLine("Create vanara snapshot reflected");
            using var _ = Library.Snapshots.Windows.WindowsShimLoader.GetSnapshotProvider(Library.Snapshots.WindowsSnapshotProvider.Vanara);
            Console.WriteLine("Created vanara snapshot reflected");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to create vanara snapshot reflected: {ex}");
        }

        try
        {
            Console.WriteLine("Create WMIC snapshot reflected");
            using var _ = Library.Snapshots.Windows.WindowsShimLoader.GetSnapshotProvider(Library.Snapshots.WindowsSnapshotProvider.Wmic);
            Console.WriteLine("Created WMIC snapshot reflected");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to create WMIC snapshot reflected: {ex}");
        }

        try
        {
            Console.WriteLine("Create SE backup privilege");
            using var _ = Library.Snapshots.Windows.WindowsShimLoader.NewSeBackupPrivilegeScope();
            Console.WriteLine("Created SE backup Privilege");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to create privilege: {ex}");
        }

        try
        {
            if (!File.Exists("log.txt"))
                using (File.Create("log.txt"))
                { }

            Console.WriteLine("Create BackupStream");
            using var _ = Library.Snapshots.Windows.WindowsShimLoader.NewBackupDataStream("log.txt");
            Console.WriteLine("Created BackupStream");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to create BackupStream: {ex}");
        }
    }
}
