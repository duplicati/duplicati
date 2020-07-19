using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Duplicati.Library.Common.IO;
using Duplicati.Library.Interface;
using Duplicati.Library.Main;
using NUnit.Framework;
using Utility = Duplicati.Library.Utility.Utility;

namespace Duplicati.UnitTest
{
    [TestFixture]
    public class ProblematicPathTests : BasicSetupHelper
    {
        /// <summary>
        ///     This is a helper class that removes problematic paths that the built-in classes
        ///     have trouble with (e.g., paths that end with a dot or space on Windows).
        /// </summary>
        private class DisposablePath : IDisposable
        {
            private readonly string path;

            public DisposablePath(string path)
            {
                this.path = path;
            }

            public void Dispose()
            {
                if (SystemIO.IO_OS.FileExists(this.path))
                {
                    SystemIO.IO_OS.FileDelete(this.path);
                }

                if (SystemIO.IO_OS.DirectoryExists(this.path))
                {
                    SystemIO.IO_OS.DirectoryDelete(this.path);
                }
            }
        }
    }
}