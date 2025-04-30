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

namespace Duplicati.Backend.Tests.S3;

/// <summary>
/// S3 Tests
/// </summary>
[TestClass]
public sealed class S3Tests : BaseTest
{
    /// <summary>
    /// Perform tests using AWS Client
    /// </summary>
    /// <returns></returns>
    [TestMethod]
    public Task TestS3WithAwsClient()
    {
        return TestS3WithClient("aws");
    }
    
    /// <summary>
    /// Perform tests using Minio Client
    /// </summary>
    /// <returns></returns>
    [TestMethod]
    public Task TestS3WithMinioClient()
    {
        return TestS3WithClient("minio");
    }
    
    /// <summary>
    /// Performs tests with selected client. The S3 tests don't have any additional parameters to be set or tested.
    /// </summary>
    /// <param name="client">client to be used, either aws or minio</param>
    /// <returns></returns>
    private Task TestS3WithClient(string client)
    {
        CheckRequiredEnvironment([
            "TESTCREDENTIAL_S3_KEY", 
            "TESTCREDENTIAL_S3_SECRET",
            "TESTCREDENTIAL_S3_BUCKETNAME",
            "TESTCREDENTIAL_S3_REGION"
        ]); 
        
        Console.WriteLine( $"s3://{Environment.GetEnvironmentVariable("TESTCREDENTIAL_S3_BUCKETNAME")}/?s3-location-constraint={Environment.GetEnvironmentVariable("TESTCREDENTIAL_S3_REGION")}&s3-storage-class=&s3-client={client}&auth-username={Environment.GetEnvironmentVariable("TESTCREDENTIAL_S3_KEY")}&auth-password={Environment.GetEnvironmentVariable("TESTCREDENTIAL_S3_SECRET")}");
        var exitCode = CommandLine.BackendTester.Program.Main(
            new[]
            {
                $"s3://{Environment.GetEnvironmentVariable("TESTCREDENTIAL_S3_BUCKETNAME")}/?s3-location-constraint={Environment.GetEnvironmentVariable("TESTCREDENTIAL_S3_REGION")}&s3-storage-class=&s3-client={client}&auth-username={Environment.GetEnvironmentVariable("TESTCREDENTIAL_S3_KEY")}&auth-password={Uri.EscapeDataString(Environment.GetEnvironmentVariable("TESTCREDENTIAL_S3_SECRET")!)}"
            }.Concat(Parameters.GlobalTestParameters).ToArray());

        if (exitCode != 0) Assert.Fail("BackendTester is returning non-zero exit code, check logs for details");

        return Task.CompletedTask;
    }
    
}