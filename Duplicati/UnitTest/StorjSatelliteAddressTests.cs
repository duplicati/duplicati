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

using Duplicati.Library.Backend.Storj;
using NUnit.Framework;

#nullable enable

namespace Duplicati.UnitTest;

/// <summary>
/// The satellites in the list have no node ID, and the bundled uplink library can only look one
/// up for the old tardigrade.io names. Creating an access from an API key then failed for every
/// satellite with "node id is required in satelliteNodeURL" (issue #4878).
/// </summary>
[TestFixture]
public class StorjSatelliteAddressTests
{
    [TestCase("us1.storj.io:7777", "12EayRS2V1kEsWESU9QMRseFhdxYxKicsiFmxrsLZHeLUtdps3S@us1.storj.io:7777")]
    [TestCase("ap1.storj.io:7777", "121RTSDpyNZVcEU84Ticf2L1ntiuUimbWgfATz21tuvgk3vzoA6@ap1.storj.io:7777")]
    [TestCase("EU1.storj.io:7777", "12L9ZFwhzVpuEKMUNUqkaTLGzwY9G24tbiigLiXpmZWKwmcNDDs@EU1.storj.io:7777")]
    public void AKnownSatelliteGetsItsNodeId(string satellite, string expected)
        => Assert.That(Storj.ResolveSatelliteAddress(satellite), Is.EqualTo(expected));

    [TestCase("12EayRS2V1kEsWESU9QMRseFhdxYxKicsiFmxrsLZHeLUtdps3S@us1.storj.io:7777")]
    [TestCase("us-central-1.tardigrade.io:7777")]
    [TestCase("satellite.example.com:7777")]
    public void OtherAddressesAreKept(string satellite)
        => Assert.That(Storj.ResolveSatelliteAddress(satellite), Is.EqualTo(satellite));

    [Test]
    public void EverySatelliteInTheListGetsANodeId()
    {
        foreach (var satellite in Storj.KNOWN_STORJ_SATELLITES.Values)
            Assert.That(Storj.ResolveSatelliteAddress(satellite!), Does.Contain("@"), satellite);
    }
}
