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

using Duplicati.WebserverCore.Services;
using NUnit.Framework;

#nullable enable

namespace Duplicati.UnitTest
{
    /// <summary>
    /// The server's shutdown stops the web server and then terminates the queue runner. Stopping
    /// the web server ends <c>App.RunAsync</c>, which disposes the service container and the queue
    /// runner with it, so the two can come in either order.
    /// </summary>
    [Category("Targeted")]
    public class QueueRunnerServiceShutdownTests
    {
        /// <summary>
        /// A queue runner. Terminating and disposing it use none of its dependencies.
        /// </summary>
        private static QueueRunnerService CreateQueueRunner()
            => new QueueRunnerService(null!, null!, null!, null!, null!, null!);

        [Test]
        public void TerminatingAfterTheContainerDisposedTheQueueRunnerDoesNotThrow()
        {
            var queueRunner = CreateQueueRunner();

            // The order the shutdown can run in
            queueRunner.Dispose();

            Assert.DoesNotThrow(() => queueRunner.Terminate(true));
        }

        [Test]
        public void DisposingAfterTerminatingDoesNotThrow()
        {
            var queueRunner = CreateQueueRunner();

            // The other order
            queueRunner.Terminate(true);

            Assert.DoesNotThrow(() => queueRunner.Dispose());
        }
    }
}
