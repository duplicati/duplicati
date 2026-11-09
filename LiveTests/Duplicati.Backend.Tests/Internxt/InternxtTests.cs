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

namespace Duplicati.Backend.Tests.Internxt;

/// <summary>
/// Internxt Drive tests. The account must be on a plan that allows CLI access,
/// otherwise the login is refused with HTTP 402.
/// </summary>
[TestClass]
public sealed class InternxtTests : BaseTest
{
    [TestMethod]
    public Task TestInternxt()
    {
        CheckRequiredEnvironment(["TESTCREDENTIAL_INTERNXT_FOLDER", "TESTCREDENTIAL_INTERNXT_USERNAME", "TESTCREDENTIAL_INTERNXT_PASSWORD"]);

        var url = $"internxt://{Environment.GetEnvironmentVariable("TESTCREDENTIAL_INTERNXT_FOLDER")}?auth-username={Uri.EscapeDataString(Environment.GetEnvironmentVariable("TESTCREDENTIAL_INTERNXT_USERNAME")!)}&auth-password={Uri.EscapeDataString(Environment.GetEnvironmentVariable("TESTCREDENTIAL_INTERNXT_PASSWORD")!)}";
        var twoFactor = Environment.GetEnvironmentVariable("TESTCREDENTIAL_INTERNXT_TWOFACTOR");
        if (!string.IsNullOrEmpty(twoFactor))
            url += $"&two-factor-code={Uri.EscapeDataString(twoFactor)}";

        var exitCode = CommandLine.BackendTester.Program.Main(
            new[]
            {
                url,
            }.Concat(Parameters.GlobalTestParameters).ToArray());

        if (exitCode != 0) Assert.Fail("BackendTester is returning non-zero exit code, check logs for details");

        return Task.CompletedTask;
    }
}
