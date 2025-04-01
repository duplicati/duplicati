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

namespace Duplicati.Backend.Tests.Google_Drive;

/// <summary>
/// Google Drive tests
/// </summary>
[TestClass]
public sealed class GoogleDriveTests : BaseTest
{

    static readonly string[] AdditionalArguments = ["--wait-after-upload=10s", "--wait-after-delete=10s"];
    [TestMethod]
    public Task TestGoogleDriveSimple()
    {
        CheckRequiredEnvironment(["TESTCREDENTIAL_GOOGLEDRIVE_TOKEN", "TESTCREDENTIAL_GOOGLEDRIVE_FOLDER"]);

        var exitCode = CommandLine.BackendTester.Program.Main(
            new[]
            {
                $"googledrive:///{Environment.GetEnvironmentVariable("TESTCREDENTIAL_GOOGLEDRIVE_FOLDER")}?authid={Environment.GetEnvironmentVariable("TESTCREDENTIAL_GOOGLEDRIVE_TOKEN")}",

            }.Concat(Parameters.GlobalTestParameters).Concat(AdditionalArguments).ToArray());

        if (exitCode != 0) Assert.Fail("BackendTester is returning non-zero exit code, check logs for details");

        return Task.CompletedTask;

    }


}