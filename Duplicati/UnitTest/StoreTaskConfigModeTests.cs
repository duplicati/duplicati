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

using Duplicati.Server;
using NUnit.Framework;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

namespace Duplicati.UnitTest;

[TestFixture]
public class StoreTaskConfigModeTests
{
    [Category("Utility")]
    [TestCase(null, StoreTaskConfigMode.Auto)]
    [TestCase("", StoreTaskConfigMode.Auto)]
    [TestCase("   ", StoreTaskConfigMode.Auto)]
    [TestCase("true", StoreTaskConfigMode.Self)]
    [TestCase("True", StoreTaskConfigMode.Self)]
    [TestCase("false", StoreTaskConfigMode.None)]
    [TestCase("False", StoreTaskConfigMode.None)]
    [TestCase("Auto", StoreTaskConfigMode.Auto)]
    [TestCase("Self", StoreTaskConfigMode.Self)]
    [TestCase("All", StoreTaskConfigMode.All)]
    [TestCase("None", StoreTaskConfigMode.None)]
    [TestCase("SelfWithSecrets", StoreTaskConfigMode.SelfWithSecrets)]
    [TestCase("AllWithSecrets", StoreTaskConfigMode.AllWithSecrets)]
    [TestCase("SelfWithUnencryptedSecrets", StoreTaskConfigMode.SelfWithUnencryptedSecrets)]
    [TestCase("AllWithUnencryptedSecrets", StoreTaskConfigMode.AllWithUnencryptedSecrets)]
    public void ParseStoreTaskConfigMode_ReturnsExpectedMode(string? rawValue, StoreTaskConfigMode expected)
    {
        Assert.AreEqual(expected, Runner.ParseStoreTaskConfigMode(rawValue));
    }

    [Test]
    [Category("Utility")]
    public void ParseStoreTaskConfigMode_UnknownValue_FallsBackToAuto()
    {
        Assert.AreEqual(StoreTaskConfigMode.Auto, Runner.ParseStoreTaskConfigMode("not-a-real-mode"));
    }

    // Modes that resolve to "store nothing" produce a null resolved mode.
    [Category("Utility")]
    [TestCase(StoreTaskConfigMode.None, true)]
    [TestCase(StoreTaskConfigMode.None, false)]
    [TestCase(StoreTaskConfigMode.Auto, false)]
    public void ResolvedTaskConfigMode_NoStorageModes_ReturnNull(StoreTaskConfigMode mode, bool encryptionEnabled)
    {
        Assert.IsNull(mode.ResolvedTaskConfigMode(encryptionEnabled));
    }

    // Full behavior matrix: mode x encryption -> (IncludeAllTasks, RemoveSecrets)
    // Secrets are only kept (RemoveSecrets == false) when explicitly requested or the mode forces secrets.

    // Auto with encryption maps to Self which removes secrets.
    [Category("Utility")]
    [TestCase(StoreTaskConfigMode.Auto, true, false, true)]
    // Self: never includes all tasks, always removes secrets.
    [TestCase(StoreTaskConfigMode.Self, false, false, true)]
    [TestCase(StoreTaskConfigMode.Self, true, false, true)]
    // All: includes all tasks, always removes secrets.
    [TestCase(StoreTaskConfigMode.All, false, true, true)]
    [TestCase(StoreTaskConfigMode.All, true, true, true)]
    // SelfWithSecrets without encryption reverts to Self (removes secrets).
    [TestCase(StoreTaskConfigMode.SelfWithSecrets, false, false, true)]
    // SelfWithSecrets with encryption keeps secrets.
    [TestCase(StoreTaskConfigMode.SelfWithSecrets, true, false, false)]
    // AllWithSecrets without encryption reverts to All (removes secrets).
    [TestCase(StoreTaskConfigMode.AllWithSecrets, false, true, true)]
    // AllWithSecrets with encryption keeps secrets.
    [TestCase(StoreTaskConfigMode.AllWithSecrets, true, true, false)]
    // Explicit forced-secret modes always keep secrets regardless of encryption.
    [TestCase(StoreTaskConfigMode.SelfWithUnencryptedSecrets, false, false, false)]
    [TestCase(StoreTaskConfigMode.SelfWithUnencryptedSecrets, true, false, false)]
    [TestCase(StoreTaskConfigMode.AllWithUnencryptedSecrets, false, true, false)]
    [TestCase(StoreTaskConfigMode.AllWithUnencryptedSecrets, true, true, false)]
    public void ResolvedTaskConfigMode_ReturnsExpectedFlags(
        StoreTaskConfigMode mode,
        bool encryptionEnabled,
        bool expectedIncludeAllTasks,
        bool expectedRemoveSecrets)
    {
        var resolved = mode.ResolvedTaskConfigMode(encryptionEnabled);

        Assert.IsNotNull(resolved);
        Assert.AreEqual(expectedIncludeAllTasks, resolved!.IncludeAllTasks, nameof(resolved.IncludeAllTasks));
        Assert.AreEqual(expectedRemoveSecrets, resolved.RemoveSecrets, nameof(resolved.RemoveSecrets));
    }

    [Category("Utility")]
    [Test]
    public void AutoWithoutEncryption_ReturnsNull()
    {
        var mode = StoreTaskConfigMode.Auto;
        var resolved = mode.ResolvedTaskConfigMode(false);

        Assert.IsNull(resolved);
    }
}
