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

using System.Collections.Generic;

namespace Duplicati.Library.Main.Operation.RemoteSynchronization;

/// <summary>
/// Global configuration for the tool. Should be set after parsing the commandline arguments.
/// </summary>
public sealed record RemoteSynchronizationConfig
(
    // Arguments
    string Src,
    string Dst,

    // Options
    bool AutoCreateFolders,
    int BackendRetries,
    int BackendRetryDelay,
    bool BackendRetryWithExponentialBackoff,
    bool Confirm,
    bool DryRun,
    List<string> DstOptions,
    bool Force,
    List<string> GlobalOptions,
    string LogFile,
    string LogLevel,
    bool ParseArgumentsOnly,
    bool Progress,
    bool Retention,
    int Retry,
    List<string> SrcOptions,
    bool VerifyContents,
    bool VerifyGetAfterPut
)
{
    public override string ToString()
    {
        return $"Src: {Library.Utility.Utility.GetUrlWithoutCredentials(Src)}, Dst: {Library.Utility.Utility.GetUrlWithoutCredentials(Dst)}, AutoCreateFolders: {AutoCreateFolders}, BackendRetries: {BackendRetries}, BackendRetryDelay: {BackendRetryDelay}, BackendRetryWithExponentialBackoff: {BackendRetryWithExponentialBackoff}, Confirm: {Confirm}, DryRun: {DryRun}, Force: {Force}, LogFile: {LogFile}, LogLevel: {LogLevel}, ParseArgumentsOnly: {ParseArgumentsOnly}, Progress: {Progress}, Retention: {Retention}, Retry: {Retry}, VerifyContents: {VerifyContents}, VerifyGetAfterPut: {VerifyGetAfterPut}";
    }
};