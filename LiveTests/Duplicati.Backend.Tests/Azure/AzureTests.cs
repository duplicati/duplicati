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

namespace Duplicati.Backend.Tests.Azure;

/// <summary>
/// Tests for Dropbox backend
/// </summary>
[TestClass]
public sealed class AzureTests : BaseTest
{
    static readonly string[] AdditionalArguments = ["--wait-after-upload=10s", "--wait-after-delete=10s"];

    /// <summary>
    /// Basic Azure test.
    /// </summary>
    [TestMethod]
    public Task TestAzureBlob()
    {
        CheckRequiredEnvironment(["TESTCREDENTIAL_AZURE_ACCOUNTNAME","TESTCREDENTIAL_AZURE_ACCESSKEY","TESTCREDENTIAL_AZURE_CONTAINERNAME"
        ]);

        var exitCode = CommandLine.BackendTester.Program.Main(
            new[]
            {
                $"azure://{Environment.GetEnvironmentVariable("TESTCREDENTIAL_AZURE_CONTAINERNAME")}?auth-username={Environment.GetEnvironmentVariable("TESTCREDENTIAL_AZURE_ACCOUNTNAME")}&auth-password={Uri.EscapeDataString(Environment.GetEnvironmentVariable("TESTCREDENTIAL_AZURE_ACCESSKEY")!)}"
            }.Concat(Parameters.GlobalTestParameters).Concat(AdditionalArguments).ToArray());

        if (exitCode != 0) Assert.Fail("BackendTester is returning non-zero exit code, check logs for details");

        return Task.CompletedTask;
    }

}