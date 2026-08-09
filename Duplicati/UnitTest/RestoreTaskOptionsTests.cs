// Copyright (C) 2026, The Duplicati Team
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

#nullable enable

using System;
using Duplicati.Server;
using Duplicati.Server.Serialization.Interface;
using NUnit.Framework;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

namespace Duplicati.UnitTest
{
    /// <summary>
    /// The restore task's extra options override the backup's configured settings and
    /// the server's default options (they are applied after <c>ApplyOptions</c>), so an
    /// option the caller did not specify must not be written into them at all —
    /// otherwise a configured <c>--restore-permissions=true</c> is silently clobbered
    /// with <c>False</c> on every restore (issue #4353).
    /// </summary>
    [TestFixture]
    [Category("RestoreTaskOptions")]
    public class RestoreTaskOptionsTests
    {
        private static IBackup MakeBackup()
            => new Duplicati.Server.Database.Backup { Name = "test", TargetURL = "file://test" };

        [Test]
        public void UnspecifiedRestoreOptionsAreNotWritten()
        {
            var task = Runner.CreateRestoreTask(MakeBackup(), [], DateTime.UtcNow, null,
                overwrite: null, restore_permissions: null, skip_metadata: null, passphrase: null);

            Assert.IsNotNull(task.ExtraOptions);
            Assert.IsFalse(task.ExtraOptions!.ContainsKey("overwrite"),
                "An unspecified overwrite must not be written, so configured defaults can apply");
            Assert.IsFalse(task.ExtraOptions.ContainsKey("restore-permissions"),
                "An unspecified restore-permissions must not be written, so configured defaults can apply (issue #4353)");
            Assert.IsFalse(task.ExtraOptions.ContainsKey("skip-metadata"),
                "An unspecified skip-metadata must not be written, so configured defaults can apply");
            Assert.IsFalse(task.ExtraOptions.ContainsKey("passphrase"),
                "An unspecified passphrase must not be written (pre-existing behavior)");
        }

        [Test]
        public void ExplicitRestoreOptionsOverride()
        {
            var task = Runner.CreateRestoreTask(MakeBackup(), [], DateTime.UtcNow, null,
                overwrite: true, restore_permissions: false, skip_metadata: true, passphrase: "secret");

            Assert.AreEqual(bool.TrueString, task.ExtraOptions!["overwrite"]);
            Assert.AreEqual(bool.FalseString, task.ExtraOptions["restore-permissions"],
                "An explicit false is an override and must be written");
            Assert.AreEqual(bool.TrueString, task.ExtraOptions["skip-metadata"]);
            Assert.AreEqual("secret", task.ExtraOptions["passphrase"]);
        }
    }
}
