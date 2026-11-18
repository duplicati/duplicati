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
using System;
using System.Collections.Generic;
using System.Linq;
using Duplicati.Library.Main.Operation.Restore;
using NUnit.Framework;

namespace Duplicati.UnitTest;

/// <summary>
/// The restore counts the uses of every block in a <see cref="BlockUseCounter"/>, which replaced a
/// dictionary to use less memory (#7538). It must answer as the dictionary did.
/// </summary>
public class BlockUseCounterTests
{
    [Test]
    [Category("Targeted")]
    public void UsesAreCountedPerBlock()
    {
        var counter = new BlockUseCounter();
        counter.Load([7, 3, 7, 9, 7, 3]);

        Assert.That(counter.Count, Is.EqualTo(3));
        Assert.That(counter[7], Is.EqualTo(3));
        Assert.That(counter[3], Is.EqualTo(2));
        Assert.That(counter[9], Is.EqualTo(1));
        Assert.That(counter.TryGetValue(5, out var missing), Is.False);
        Assert.That(missing, Is.EqualTo(0));
        Assert.That(() => counter[5], Throws.TypeOf<KeyNotFoundException>());
        Assert.That(counter.OrderBy(x => x.Key).Select(x => (x.Key, x.Value)), Is.EqualTo(new[] { (3L, 2L), (7L, 3L), (9L, 1L) }));
    }

    [Test]
    [Category("Targeted")]
    public void ABlockThatIsUsedUpIsGone()
    {
        var counter = new BlockUseCounter();
        counter.Load([1, 1, 2]);

        counter[1] = 1;
        Assert.That(counter.Remove(1), Is.True);
        Assert.That(counter.Remove(1), Is.False, "a block is only used up once");
        Assert.That(counter.TryGetValue(1, out _), Is.False);
        Assert.That(() => counter[1] = 4, Throws.TypeOf<KeyNotFoundException>(), "a used-up block is not counted again");
        Assert.That(() => counter[2] = 0, Throws.TypeOf<ArgumentOutOfRangeException>());
        Assert.That(counter.Count, Is.EqualTo(1));
        Assert.That(counter.Sum(x => x.Value), Is.EqualTo(1));
        Assert.That(counter.Remove(99), Is.False);
    }

    [Test]
    [Category("Targeted")]
    public void TheCounterAnswersAsTheDictionaryDid([Values(1, 2, 3)] int seed)
    {
        // The operations of SleepableDictionary, run on the counter and on a dictionary side by side
        var rnd = new Random(seed);
        var uses = Enumerable.Range(0, 5000).Select(_ => (long)rnd.Next(1, 1500)).ToList();
        var expected = new Dictionary<long, long>();
        foreach (var id in uses)
            expected[id] = expected.TryGetValue(id, out var c) ? c + 1 : 1;
        var counter = new BlockUseCounter();
        counter.Load(uses.ToList());

        for (var step = 0; step < 20000; step++)
        {
            var id = (long)rnd.Next(0, 1600);
            // As CheckCountsAsync: decrement, or remove at zero
            var count = expected.TryGetValue(id, out var c) ? c - 1 : 0;
            var actualFound = counter.TryGetValue(id, out var actual);
            Assert.That((actualFound, actual), Is.EqualTo((expected.ContainsKey(id), expected.GetValueOrDefault(id))), $"lookup of {id} at step {step}");
            if (count > 0)
            {
                expected[id] = count;
                counter[id] = count;
            }
            else
            {
                Assert.That(counter.Remove(id), Is.EqualTo(expected.Remove(id)), $"remove of {id} at step {step}");
            }
            Assert.That(counter.Count, Is.EqualTo(expected.Count));
        }

        Assert.That(counter.OrderBy(x => x.Key), Is.EqualTo(expected.OrderBy(x => x.Key)));
        Assert.That(counter.Sum(x => x.Value), Is.EqualTo(expected.Sum(x => x.Value)));
    }
}
