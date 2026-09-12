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
using System.Runtime.Versioning;
using Duplicati.Library.Logging;
using Duplicati.Library.Snapshots;
using Duplicati.Library.Snapshots.Windows;
using Duplicati.Library.SourceProvider.Builtin.HyperV;
using NUnit.Framework;

namespace Duplicati.UnitTest
{
    [TestFixture]
    public class TestHyperVSourceProvider
    {
        private const char DS = '\\'; // Directory separator

        private sealed class MockHyperVUtility : IHyperVUtility
        {
            public bool IsHyperVInstalled { get; set; } = true;
            public Guid HyperVWriterGuid { get; set; } = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
            public bool IsVSSWriterSupported { get; set; } = true;
            public List<HyperVGuest> Guests { get; set; } = [];

            public void QueryHyperVGuestsInfo(WindowsSnapshotProvider provider, bool bIncludePaths = false)
            {
                // Mock implementation, no actual querying
            }
        }

        private sealed class LogSink : ILogDestination
        {
            public List<LogEntry> Entries { get; } = [];

            public void WriteMessage(LogEntry entry)
            {
                Entries.Add(entry);
            }
        }

        private static string MakeHyperVSource(Guid id) => $"%HYPERV%{DS}{id}";

        [SupportedOSPlatform("windows")]
        private static void RunPrepareOptions(Dictionary<string, string?> options, MockHyperVUtility utility)
        {
            using var _ = Log.StartScope(new LogSink());
            new HyperVSourceProvider().PrepareOptions(options, utility);
        }

        [SupportedOSPlatform("windows")]
        private static List<HyperVGuest> RunSelectGuests(MockHyperVUtility utility, params string[] sources)
        {
            using var _ = Log.StartScope(new LogSink());
            return HyperVSourceProvider.SelectGuests(utility, sources);
        }

        #region MatchesSource

        [Test]
        public void MatchesSource_recognizes_hyperv_paths()
        {
            var provider = new HyperVSourceProvider();

            Assert.Multiple(() =>
            {
                Assert.That(provider.MatchesSource("%HYPERV%"), Is.True);
                Assert.That(provider.MatchesSource($"%HYPERV%{DS}{Guid.NewGuid()}"), Is.True);
                Assert.That(provider.MatchesSource($"%HYPERV%{DS}{Guid.NewGuid()}{DS}C:{DS}VMs{DS}disk.vhdx"), Is.True);
                Assert.That(provider.MatchesSource(@$"C:{DS}data"), Is.False);
                Assert.That(provider.MatchesSource("%MSSQL%"), Is.False);
                Assert.That(provider.MatchesSource(""), Is.False);
            });
        }

        #endregion

        #region PrepareOptions

        [Test]
        public void PrepareOptions_does_nothing_when_not_installed()
        {
            if (!OperatingSystem.IsWindows())
                return; // Hyper-V is only available on Windows

            var options = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            var utility = new MockHyperVUtility { IsHyperVInstalled = false };

            RunPrepareOptions(options, utility);

            Assert.That(options, Is.Empty, "No options should be changed when Hyper-V is not installed");
        }

        [Test]
        public void PrepareOptions_forces_snapshot_policy_required()
        {
            if (!OperatingSystem.IsWindows())
                return; // Hyper-V is only available on Windows

            var options = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            var utility = new MockHyperVUtility();

            RunPrepareOptions(options, utility);

            Assert.That(options["snapshot-policy"], Is.EqualTo("required"));
        }

        [Test]
        public void PrepareOptions_removes_hyperv_writer_from_vss_exclude_list()
        {
            if (!OperatingSystem.IsWindows())
                return; // Hyper-V is only available on Windows

            var hyperVGuid = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
            var options = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
            {
                ["vss-exclude-writers"] = $"{hyperVGuid};bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb",
                ["snapshot-policy"] = "required"
            };

            var utility = new MockHyperVUtility { HyperVWriterGuid = hyperVGuid };

            RunPrepareOptions(options, utility);

            Assert.Multiple(() =>
            {
                Assert.That(options["vss-exclude-writers"], Does.Not.Contain(hyperVGuid.ToString()),
                    "The Hyper-V writer GUID must have been stripped");
                Assert.That(options["vss-exclude-writers"], Does.Contain("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
                    "Other writer GUIDs must be kept");
            });
        }

        [Test]
        public void PrepareOptions_switches_snapshot_provider_from_wmi_to_native()
        {
            if (!OperatingSystem.IsWindows())
                return; // Hyper-V is only available on Windows

            var options = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
            {
                ["snapshot-policy"] = "required",
                ["snapshot-provider"] = "wmi"
            };

            var utility = new MockHyperVUtility();

            RunPrepareOptions(options, utility);

            Assert.That(options["snapshot-provider"], Is.EqualTo("Native"),
                "The Wmi snapshot provider must be replaced with Native");
        }

        #endregion

        #region SelectGuests

#pragma warning disable CA1416 // Windows-only API calls are guarded by OperatingSystem.IsWindows() checks

        [Test]
        public void SelectGuests_all_marker_includes_every_machine()
        {
            if (!OperatingSystem.IsWindows())
                return; // Hyper-V is only available on Windows

            var g1 = new HyperVGuest("Alpha", Guid.NewGuid(), [@$"C:{DS}HyperV{DS}Alpha{DS}Alpha.vhdx"]);
            var g2 = new HyperVGuest("Beta", Guid.NewGuid(), [@$"D:{DS}VMs{DS}Beta{DS}Disk1.vhdx", @$"D:{DS}VMs{DS}Beta{DS}State.bin"]);

            var util = new MockHyperVUtility { Guests = [g1, g2] };

            var result = RunSelectGuests(util, "%HYPERV%");

            Assert.Multiple(() =>
            {
                Assert.That(result, Has.Count.EqualTo(2));
                Assert.That(result.Select(x => x.ID), Does.Contain(g1.ID));
                Assert.That(result.Select(x => x.ID), Does.Contain(g2.ID));
            });
        }

        [Test]
        public void SelectGuests_single_guid_selects_only_that_guest()
        {
            if (!OperatingSystem.IsWindows())
                return; // Hyper-V is only available on Windows

            var gWanted = new HyperVGuest("Wanted", Guid.NewGuid(), [@$"E:{DS}VMs{DS}Wanted{DS}disk.vhdx"]);
            var gOther = new HyperVGuest("Skip", Guid.NewGuid(), [@$"E:{DS}VMs{DS}Skip{DS}disk.vhdx"]);

            var util = new MockHyperVUtility { Guests = [gWanted, gOther] };

            var result = RunSelectGuests(util, MakeHyperVSource(gWanted.ID));

            Assert.Multiple(() =>
            {
                Assert.That(result, Has.Count.EqualTo(1));
                Assert.That(result[0].ID, Is.EqualTo(gWanted.ID));
            });
        }

        [Test]
        public void SelectGuests_multiple_guids_selects_multiple_guests()
        {
            if (!OperatingSystem.IsWindows())
                return; // Hyper-V is only available on Windows

            var g1 = new HyperVGuest("VM1", Guid.NewGuid(), [@$"C:{DS}VMs{DS}VM1{DS}disk.vhdx"]);
            var g2 = new HyperVGuest("VM2", Guid.NewGuid(), [@$"C:{DS}VMs{DS}VM2{DS}disk.vhdx"]);
            var g3 = new HyperVGuest("VM3", Guid.NewGuid(), [@$"C:{DS}VMs{DS}VM3{DS}disk.vhdx"]);

            var util = new MockHyperVUtility { Guests = [g1, g2, g3] };

            var result = RunSelectGuests(util, MakeHyperVSource(g1.ID), MakeHyperVSource(g3.ID));

            Assert.Multiple(() =>
            {
                Assert.That(result, Has.Count.EqualTo(2));
                Assert.That(result.Select(x => x.ID), Does.Contain(g1.ID));
                Assert.That(result.Select(x => x.ID), Does.Contain(g3.ID));
            });
        }

        [Test]
        public void SelectGuests_guid_with_subpath_selects_guest()
        {
            if (!OperatingSystem.IsWindows())
                return; // Hyper-V is only available on Windows

            var guest = new HyperVGuest("TestVM", Guid.NewGuid(), [@$"C:{DS}VMs{DS}TestVM{DS}disk.vhdx", @$"C:{DS}VMs{DS}TestVM{DS}state.bin"]);

            var util = new MockHyperVUtility { Guests = [guest] };

            var subPath = @$"C:{DS}VMs{DS}TestVM{DS}disk.vhdx";
            var result = RunSelectGuests(util, $"{MakeHyperVSource(guest.ID)}{DS}{subPath}");

            Assert.Multiple(() =>
            {
                Assert.That(result, Has.Count.EqualTo(1));
                Assert.That(result[0].ID, Is.EqualTo(guest.ID));
                Assert.That(result[0].DataPaths, Is.EqualTo(new[] { subPath }),
                    "Subpath source should restrict the guest's data paths to just the subpath");
            });
        }

        [Test]
        public void SelectGuests_full_and_subpath_for_same_guest_prefers_full()
        {
            if (!OperatingSystem.IsWindows())
                return; // Hyper-V is only available on Windows

            var guest = new HyperVGuest("TestVM", Guid.NewGuid(), [@$"C:{DS}VMs{DS}TestVM{DS}disk.vhdx", @$"C:{DS}VMs{DS}TestVM{DS}state.bin"]);

            var util = new MockHyperVUtility { Guests = [guest] };

            // Both a full-guest source and a subpath source for the same guest
            var result = RunSelectGuests(util,
                MakeHyperVSource(guest.ID),
                $"{MakeHyperVSource(guest.ID)}{DS}C:{DS}VMs{DS}TestVM{DS}disk.vhdx");

            Assert.Multiple(() =>
            {
                Assert.That(result, Has.Count.EqualTo(1));
                Assert.That(result[0].DataPaths, Is.EqualTo(guest.DataPaths),
                    "Full guest source should include all data paths even when a subpath source is also present");
            });
        }

        [Test]
        public void SelectGuests_unknown_guid_throws()
        {
            if (!OperatingSystem.IsWindows())
                return; // Hyper-V is only available on Windows

            var guest = new HyperVGuest("TestVM", Guid.NewGuid(), [@$"C:{DS}VMs{DS}TestVM{DS}disk.vhdx"]);
            var util = new MockHyperVUtility { Guests = [guest] };

            var ex = Assert.Throws<Library.Interface.UserInformationException>(
                () => RunSelectGuests(util, MakeHyperVSource(Guid.NewGuid())));
            Assert.That(ex!.HelpID, Is.EqualTo("HyperVGuestNotFound"));
        }

        [Test]
        public void SelectGuests_invalid_guid_throws()
        {
            if (!OperatingSystem.IsWindows())
                return; // Hyper-V is only available on Windows

            var util = new MockHyperVUtility { Guests = [] };

            var ex = Assert.Throws<Library.Interface.UserInformationException>(
                () => RunSelectGuests(util, $"%HYPERV%{DS}not-a-guid"));
            Assert.That(ex!.HelpID, Is.EqualTo("HyperVGuestIdInvalid"));
        }

        [Test]
        public void SelectGuests_empty_guests_returns_empty()
        {
            if (!OperatingSystem.IsWindows())
                return; // Hyper-V is only available on Windows

            var util = new MockHyperVUtility { Guests = [] };

            var result = RunSelectGuests(util, "%HYPERV%");

            Assert.That(result, Is.Empty);
        }

#pragma warning restore CA1416

        #endregion
    }
}
