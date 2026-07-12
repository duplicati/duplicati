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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using Duplicati.Library.Utility;
using NUnit.Framework;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

namespace Duplicati.UnitTest
{
    /// <summary>
    /// Regression tests for <see cref="TempFile"/> unique-name generation. In DEBUG builds
    /// the name is built from the caller, a second-precision timestamp and a truncated Guid,
    /// which could collide when many temp files are created in the same second by the same
    /// caller — producing duplicate paths and throwing from the internal tracking dictionary.
    /// The generator now appends a process-wide counter to guarantee uniqueness.
    /// </summary>
    public class TempFileUniqueNameTests
    {
        [Test]
        [Category("Utility")]
        public void GeneratedNamesAreUniqueUnderConcurrentLoad()
        {
            const int threads = 8;
            const int perThread = 12500;
            const int expected = threads * perThread;

            var names = new ConcurrentBag<string>();
            var errors = new ConcurrentQueue<Exception>();

            var work = new Task[threads];
            for (var t = 0; t < threads; t++)
            {
                work[t] = Task.Run(() =>
                {
                    try
                    {
                        for (var i = 0; i < perThread; i++)
                            names.Add(TempFile.GenerateUniqueName());
                    }
                    catch (Exception ex)
                    {
                        // Before the fix, a name collision throws ArgumentException from the
                        // tracking dictionary's Add; capture it rather than faulting the task.
                        errors.Enqueue(ex);
                    }
                });
            }

            Task.WaitAll(work);

            Assert.IsEmpty(errors, "Generating unique temp names must not throw: " + (errors.IsEmpty ? "" : errors.ToArray()[0].ToString()));

            var all = names.ToArray();
            Assert.AreEqual(expected, all.Length, "Every generation should have produced a name");

            var distinct = new HashSet<string>(all, StringComparer.Ordinal);
            Assert.AreEqual(all.Length, distinct.Count, "Generated temp file names must all be unique");
        }
    }
}
