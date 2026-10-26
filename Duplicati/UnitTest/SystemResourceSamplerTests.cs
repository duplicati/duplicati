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
using System.Diagnostics;
using System.Linq;
using System.Threading;
using Duplicati.WebserverCore.Services;
using NUnit.Framework;

#nullable enable

namespace Duplicati.UnitTest;

/// <summary>
/// The sampler reports resource usage for the system status page. CPU usage needs two samples,
/// so the first sample of a process has no CPU usage and later ones do.
/// </summary>
[TestFixture]
[Category("SystemStatus")]
public class SystemResourceSamplerTests
{
    [Test]
    public void SampleProcesses_ReportsServerProcessWithCpuUsageOnSecondSample()
    {
        var sampler = new SystemResourceSampler();

        var first = sampler.SampleProcesses().Single(x => x.Role == "server");
        Assert.That(first.ProcessId, Is.EqualTo(Environment.ProcessId));
        Assert.That(first.CpuUsage, Is.Null, "The first sample has nothing to compare with.");
        Assert.That(first.WorkingSet, Is.GreaterThan(0));
        Assert.That(first.ManagedHeapSize, Is.GreaterThan(0));
        Assert.That(first.ThreadCount, Is.GreaterThan(0));

        // Burn some CPU so the second sample has something to report
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < 400) { Thread.SpinWait(1000); }

        var second = sampler.SampleProcesses().Single(x => x.Role == "server");
        Assert.That(second.CpuUsage, Is.Not.Null);
        Assert.That(second.CpuUsage, Is.InRange(0.0, 1.0));
    }

    [Test]
    public void SampleProcesses_SamplesCloseTogetherReuseThePreviousCpuUsage()
    {
        var sampler = new SystemResourceSampler();
        sampler.SampleProcesses();
        Thread.Sleep(400);
        var second = sampler.SampleProcesses().Single(x => x.Role == "server");
        var third = sampler.SampleProcesses().Single(x => x.Role == "server");

        Assert.That(third.CpuUsage, Is.EqualTo(second.CpuUsage));
    }

    [Test]
    public void SampleMachine_ReportsProcessorsAndMemory()
    {
        var sampler = new SystemResourceSampler();

        var first = sampler.SampleMachine();
        Assert.That(first.ProcessorCount, Is.EqualTo(Environment.ProcessorCount));
        Assert.That(first.TotalMemory, Is.GreaterThan(0));
        Assert.That(first.CpuUsage, Is.Null, "The first sample has nothing to compare with.");

        // The macOS tick counters are only updated about once a second
        Thread.Sleep(1100);
        var second = sampler.SampleMachine();
        if (OperatingSystem.IsLinux() || OperatingSystem.IsWindows() || OperatingSystem.IsMacOS())
            Assert.That(second.CpuUsage, Is.InRange(0.0, 1.0));
        if (OperatingSystem.IsLinux() || OperatingSystem.IsWindows() || OperatingSystem.IsMacOS())
            Assert.That(second.AvailableMemory, Is.InRange(1L, second.TotalMemory!.Value));
    }

    /// <summary>
    /// Several clients can sample at the same time, the websocket loop and polling clients;
    /// interleaved samples must not lose the machine CPU usage once it is known.
    /// </summary>
    [Test]
    public void SampleMachine_InterleavedSamplesKeepReportingCpuUsage()
    {
        if (!(OperatingSystem.IsLinux() || OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()))
            Assert.Ignore("Machine CPU usage is not available on this platform");

        var sampler = new SystemResourceSampler();
        sampler.SampleMachine();
        Thread.Sleep(1100);
        Assert.That(sampler.SampleMachine().CpuUsage, Is.Not.Null);

        for (var i = 0; i < 10; i++)
        {
            Thread.Sleep(130);
            Assert.That(sampler.SampleMachine().CpuUsage, Is.Not.Null, $"Sample {i} lost the CPU usage");
        }
    }
}
