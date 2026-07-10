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
using System.Collections.Generic;
using System.Linq;
using Duplicati.Server.Database;
using Duplicati.Server.Serialization.Interface;
using NUnit.Framework;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

namespace Duplicati.UnitTest
{
    /// <summary>
    /// https://github.com/duplicati/duplicati/issues/1698
    /// "Show log" and "Database delete" read/deleted the stored <c>Backup.DBPath</c> directly, while
    /// most operations honor a "--dbpath" advanced option. When those disagreed, Show log opened the
    /// wrong/empty database ("no such table: LogData"). <see cref="Duplicati.Server.Runner.GetEffectiveDBPath"/>
    /// reconciles them with the same precedence as the runner; these tests cover that precedence.
    /// </summary>
    [TestFixture]
    public class Issue1698
    {
        private static Backup CreateBackup(string? storedDbPath, params (string Name, string Value)[] settings)
        {
            var backup = new Backup
            {
                ID = null,
                Name = "Test Backup",
                Description = "",
                Tags = new string[0],
                TargetURL = "file:///test",
                Sources = new string[0],
                Settings = settings.Select(s => (ISetting)new Setting { Name = s.Name, Value = s.Value }).ToArray(),
                Filters = new IFilter[0],
                Metadata = new Dictionary<string, string>()
            };
            if (storedDbPath != null)
                backup.SetDBPath(storedDbPath);
            return backup;
        }

        [Test]
        public void UsesStoredDbPathWhenNoAdvancedOption()
        {
            var backup = CreateBackup("/stored/path.sqlite");
            Assert.AreEqual("/stored/path.sqlite", Duplicati.Server.Runner.GetEffectiveDBPath(backup));
        }

        [Test]
        public void AdvancedDbPathOptionOverridesStoredDbPath()
        {
            var backup = CreateBackup("/stored/path.sqlite", ("--dbpath", "/override/path.sqlite"));
            Assert.AreEqual("/override/path.sqlite", Duplicati.Server.Runner.GetEffectiveDBPath(backup));
        }

        [Test]
        public void AdvancedDbPathMatchIsCaseInsensitiveOnName()
        {
            var backup = CreateBackup("/stored/path.sqlite", ("--DBPath", "/override/path.sqlite"));
            Assert.AreEqual("/override/path.sqlite", Duplicati.Server.Runner.GetEffectiveDBPath(backup));
        }

        [Test]
        public void BlankAdvancedDbPathFallsBackToStoredDbPath()
        {
            var backup = CreateBackup("/stored/path.sqlite", ("--dbpath", "   "));
            Assert.AreEqual("/stored/path.sqlite", Duplicati.Server.Runner.GetEffectiveDBPath(backup));
        }
    }
}
