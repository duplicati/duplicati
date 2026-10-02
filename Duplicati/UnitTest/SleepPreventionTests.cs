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

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Duplicati.Library.Main;
using NUnit.Framework;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

#nullable enable

namespace Duplicati.UnitTest
{
    /// <summary>
    /// An operation keeps Windows awake while it runs, and must let it sleep again when it ends
    /// (#6837). The request is made per thread, so it has to be withdrawn on the thread that made
    /// it.
    /// </summary>
    [Category("Targeted")]
    public class SleepPreventionTests
    {
        [DllImport("powrprof.dll")]
        private static extern uint CallNtPowerInformation(int informationLevel, IntPtr inputBuffer, uint inputBufferLength, out uint outputBuffer, uint outputBufferLength);

        /// <summary>
        /// <c>SystemExecutionState</c>: the requests the system is honouring, from every process.
        /// Reading it needs no administrator rights, unlike <c>powercfg /requests</c>.
        /// </summary>
        private const int SystemExecutionState = 16;

        /// <summary>
        /// <c>ES_SYSTEM_REQUIRED</c>: the system is kept from sleeping.
        /// </summary>
        private const uint SystemRequired = 0x1;

        private static bool IsSystemKeptAwake()
        {
            CallNtPowerInformation(SystemExecutionState, IntPtr.Zero, 0, out var state, sizeof(uint));
            return (state & SystemRequired) != 0;
        }

        /// <summary>
        /// Waits up to a few seconds for the system to be allowed to sleep again.
        /// </summary>
        private static async Task<bool> WaitUntilAllowedToSleepAsync()
        {
            for (var i = 0; i < 20; i++)
            {
                if (!IsSystemKeptAwake())
                    return true;
                await Task.Delay(250);
            }

            return false;
        }

        [Test]
        public async Task TheSystemMaySleepAgainAfterAnOperation()
        {
            if (!OperatingSystem.IsWindows())
                Assert.Ignore("Sleep prevention through SetThreadExecutionState is Windows only");
            if (IsSystemKeptAwake())
                Assert.Ignore("Something else keeps the system awake, so the test cannot tell whether the operation does");

            var options = new Options(new Dictionary<string, string?> { ["allow-sleep"] = "false" });

            // Twice, as the second operation must not pick up anything the first one left
            for (var i = 0; i < 2; i++)
            {
                using (new ProcessController(options))
                {
                    // Long enough for the request to be renewed from another thread, if it is renewed
                    await Task.Delay(TimeSpan.FromSeconds(12));
                    Assert.IsTrue(IsSystemKeptAwake(), "The operation did not keep the system awake while it ran");
                }

                Assert.IsTrue(await WaitUntilAllowedToSleepAsync(), "The system is still kept awake after the operation ended");
            }
        }
    }
}
