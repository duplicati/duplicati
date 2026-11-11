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


using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Duplicati.Library.Utility;
using NUnit.Framework;

namespace Duplicati.UnitTest;

public class FileBackedListTests
{
    [Test]
    [Category("Utility")]
    public void EnumerationSeesTheItemsPresentWhenItStarted()
    {
        using var list = new FileBackedStringList(System.Text.Encoding.UTF8);
        list.Add("a");
        list.Add("b");

        using var enumerator = list.GetEnumerator();
        Assert.That(enumerator.MoveNext(), Is.True);
        list.Add("c");
        Assert.That(enumerator.Current, Is.EqualTo("a"));
        Assert.That(enumerator.MoveNext(), Is.True);
        Assert.That(enumerator.Current, Is.EqualTo("b"));
        Assert.That(enumerator.MoveNext(), Is.False, "An item added while enumerating is left out");

        Assert.That(list.ToList(), Is.EqualTo(new[] { "a", "b", "c" }));
        Assert.That(list.Count, Is.EqualTo(3));
    }

    [Test]
    [Category("Utility")]
    public void ItemsCanBeAddedWhileTheListIsEnumerated()
    {
        const int WRITERS = 4;
        const int PER_WRITER = 5000;

        using var list = new FileBackedStringList(System.Text.Encoding.UTF8);
        // A small limit, so the list moves from memory to a file while it is in use
        list.SwitchToFileLimit = 64 * 1024;

        using var stop = new CancellationTokenSource();
        var reader = Task.Run(() =>
        {
            while (!stop.IsCancellationRequested)
                foreach (var item in list)
                    Assert.That(item, Does.StartWith("item-"));
        });

        var writers = Enumerable.Range(0, WRITERS)
            .Select(w => Task.Run(() =>
            {
                for (var i = 0; i < PER_WRITER; i++)
                    list.Add($"item-{w}-{i}");
            }))
            .ToArray();

        Task.WaitAll(writers);
        stop.Cancel();
        reader.Wait();

        Assert.That(list.IsFileBacked, Is.True);
        Assert.That(list.Count, Is.EqualTo(WRITERS * PER_WRITER));
        var expected = Enumerable.Range(0, WRITERS).SelectMany(w => Enumerable.Range(0, PER_WRITER).Select(i => $"item-{w}-{i}"));
        Assert.That(list.ToHashSet(), Is.EquivalentTo(expected));
    }
}
