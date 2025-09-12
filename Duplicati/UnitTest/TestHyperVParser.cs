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

using System;
using System.Collections.Generic;
using System.Linq;
using Duplicati.Library.Logging;
using Duplicati.Library.Snapshots;
using Duplicati.Library.Snapshots.Windows;
using NUnit.Framework;
using NUnit.Framework.Legacy;

namespace Duplicati.UnitTest
{
    [TestFixture]
    public class TestHyperVParser
    {
        private const char PS = ';'; // Windows path separator
        private const char DS = '\\'; // Directory separator
        private sealed record MachineEntry(string ID, string Name, string[] Paths);

        private sealed class MockHyperVUtility : IHyperVUtility
        {
            public bool IsHyperVInstalled { get; set; } = true;
            public Guid HyperVWriterGuid { get; set; } = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

            public bool IsVSSWriterSupported { get; set; } = true;

            public List<HyperVGuest> Guests { get; set; } = null;

            public void QueryHyperVGuestsInfo(SnapshotProvider provider, bool bIncludePaths = false)
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

        private static readonly string HyperVPathTemplate = $"%HYPERV%{DS}{{0}}";
        private static string MakeHyperVSource(Guid id) => string.Format(HyperVPathTemplate, id);

        [Test]
        public void Returns_empty_change_set_when_HyperV_not_installed()
        {
            if (!OperatingSystem.IsWindows())
                return; // Hyper-V is only available on Windows

            var paths = new[] { @"C:\data" };
            var filter = string.Empty;
            var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var utility = new MockHyperVUtility { IsHyperVInstalled = false };

            var module = new Duplicati.Library.Modules.Builtin.HyperVOptions();
            using var log = Duplicati.Library.Logging.Log.StartScope(new LogSink());

            var result = module.RealParseSourcePaths(ref paths, ref filter, options, utility);

            Assert.That(result, Is.Empty, "No cmdline option should be rewritten.");
            Assert.That(paths, Is.EqualTo(new[] { @"C:\data" }), "Source paths must stay untouched.");
        }

        [Test]
        public void Removes_HyperV_writer_from_vss_exclude_list()
        {
            if (!OperatingSystem.IsWindows())
                return; // Hyper-V is only available on Windows

            var hyperVGuid = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
            var paths = new[] { string.Format(HyperVPathTemplate, Guid.NewGuid()) };
            var filter = string.Empty;
            var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["vss-exclude-writers"] = $"{hyperVGuid};bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb",
                ["snapshot-policy"] = "required"
            };

            var utility = new MockHyperVUtility { HyperVWriterGuid = hyperVGuid };
            var module = new Duplicati.Library.Modules.Builtin.HyperVOptions();
            using var log = Duplicati.Library.Logging.Log.StartScope(new LogSink());

            var result = module.RealParseSourcePaths(ref paths, ref filter, options, utility);

            Assert.Multiple(() =>
            {
                Assert.That(result.ContainsKey("vss-exclude-writers"), Is.True,
                            "The change-set must contain the corrected option.");
                Assert.That(result["vss-exclude-writers"],
                            Does.Not.Contain(hyperVGuid.ToString()),
                            "The Hyper-V writer GUID must have been stripped.");
            });
        }

        [Test]
        public void Adds_required_snapshot_policy_when_missing()
        {
            if (!OperatingSystem.IsWindows())
                return; // Hyper-V is only available on Windows

            var guestId = Guid.NewGuid();
            var paths = new[] { MakeHyperVSource(guestId) };
            var filter = string.Empty;
            var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            var utility = new MockHyperVUtility
            {
                Guests = new List<HyperVGuest>
                {
                    new("TestVM", guestId, [@"C:\VMs\TestVM\TestVM.vhdx"])
                }
            };

            using var log = Duplicati.Library.Logging.Log.StartScope(new LogSink());
            var module = new Duplicati.Library.Modules.Builtin.HyperVOptions();
            var result = module.RealParseSourcePaths(ref paths, ref filter, options, utility);

            Assert.Multiple(() =>
            {
                Assert.That(result.ContainsKey("snapshot-policy"), Is.True);
                Assert.That(result["snapshot-policy"], Is.EqualTo("required"),
                            "Snapshot policy must be enforced to 'required'.");
            });
        }

        [Test]
        public void Expands_guest_data_paths_into_source_list()
        {
            if (!OperatingSystem.IsWindows())
                return; // Hyper-V is only available on Windows

            var guestId = Guid.NewGuid();
            var guest = new HyperVGuest("TestVM", guestId, [@"C:\HyperV\TestVM\TestVM.vhdx", @"C:\HyperV\TestVM\State.bin"]);

            var paths = new[] { MakeHyperVSource(guestId) };
            var filter = string.Empty;
            var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["snapshot-policy"] = "required"
            };

            var utility = new MockHyperVUtility
            {
                Guests = new List<HyperVGuest> { guest }
            };

            using var log = Duplicati.Library.Logging.Log.StartScope(new LogSink());
            var module = new Duplicati.Library.Modules.Builtin.HyperVOptions();
            module.RealParseSourcePaths(ref paths, ref filter, options, utility);

            Assert.That(paths, Does.Contain(guest.DataPaths[0]));
            Assert.That(paths, Does.Contain(guest.DataPaths[1]));
        }

        [Test]
        public void Respects_exclude_filter_expressions()
        {
            if (!OperatingSystem.IsWindows())
                return; // Hyper-V is only available on Windows

            var guestId = Guid.NewGuid();
            var data = @"C:\HyperV\TestVM\TestVM.vhdx";

            var guest = new HyperVGuest("TestVM", guestId, [data]);

            var paths = new[] { MakeHyperVSource(guestId) };
            var filter = $"-{data}";                       // exclude the only path
            var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["snapshot-policy"] = "required"
            };

            var utility = new MockHyperVUtility
            {
                Guests = new List<HyperVGuest> { guest }
            };

            using var log = Duplicati.Library.Logging.Log.StartScope(new LogSink());
            var module = new Duplicati.Library.Modules.Builtin.HyperVOptions();
            module.RealParseSourcePaths(ref paths, ref filter, options, utility);

            Assert.That(paths, Does.Not.Contain(data),
                        "The excluded VHDX must not make it into the final path list.");
        }

        [Test]
        public void Respects_exclude_filter_expressions_hyperv()
        {
            if (!OperatingSystem.IsWindows())
                return; // Hyper-V is only available on Windows

            var guestId = Guid.NewGuid();
            var data = @$"C:{DS}HyperV{DS}TestVM{DS}TestVM.vhdx";

            var guest = new HyperVGuest("TestVM", guestId, [data]);

            var paths = new[] { MakeHyperVSource(guestId) };
            var filter = $"-{MakeHyperVSource(guestId)}{DS}{data}";                       // exclude the only path
            var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["snapshot-policy"] = "required"
            };

            var utility = new MockHyperVUtility
            {
                Guests = new List<HyperVGuest> { guest }
            };

            using var log = Duplicati.Library.Logging.Log.StartScope(new LogSink());
            var module = new Duplicati.Library.Modules.Builtin.HyperVOptions();
            module.RealParseSourcePaths(ref paths, ref filter, options, utility);

            Assert.That(paths, Does.Not.Contain(data),
                        "The excluded VHDX must not make it into the final path list.");
        }

        [Test]
        public void HyperV_all_marker_includes_every_machine()
        {
            if (!OperatingSystem.IsWindows())
                return; // Hyper-V is only available on Windows

            // arrange
            var g1 = new HyperVGuest("Alpha", Guid.NewGuid(),
                                     [@$"C:{DS}HyperV{DS}Alpha{DS}Alpha.vhdx"]);
            var g2 = new HyperVGuest("Beta", Guid.NewGuid(),
                                     [@$"D:{DS}VMs{DS}Beta{DS}Disk1.vhdx", @$"D:{DS}VMs{DS}Beta{DS}State.bin"]);

            var paths = new[] { "%HYPERV%" };              // <-- only the ALL-marker
            var filter = string.Empty;
            var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            { ["snapshot-policy"] = "required" };

            var util = new MockHyperVUtility { Guests = new List<HyperVGuest> { g1, g2 } };

            using var _ = Duplicati.Library.Logging.Log.StartScope(new LogSink());
            new Duplicati.Library.Modules.Builtin.HyperVOptions()
                .RealParseSourcePaths(ref paths, ref filter, options, util);

            // assert – every data-path from every guest must be present
            var expected = g1.DataPaths.Concat(g2.DataPaths);
            CollectionAssert.IsSubsetOf(expected, paths);
        }

        [Test]
        public void All_marker_but_single_guest_excluded()
        {
            if (!OperatingSystem.IsWindows())
                return; // Hyper-V is only available on Windows

            var gKeep = new HyperVGuest("KeepMe", Guid.NewGuid(),
                                        [@$"C:{DS}VMs{DS}KeepMe{DS}KeepMe.vhdx"]);
            var gDrop = new HyperVGuest("DropMe", Guid.NewGuid(),
                                        [@$"C:{DS}VMs{DS}DropMe{DS}DropMe.vhdx"]);

            var paths = new[] { "%HYPERV%" };
            var filter = $"-{MakeHyperVSource(gDrop.ID)}";
            var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            { ["snapshot-policy"] = "required" };

            var util = new MockHyperVUtility { Guests = new List<HyperVGuest> { gKeep, gDrop } };

            using var _ = Duplicati.Library.Logging.Log.StartScope(new LogSink());
            new Duplicati.Library.Modules.Builtin.HyperVOptions()
                .RealParseSourcePaths(ref paths, ref filter, options, util);

            Assert.That(paths, Does.Contain(gKeep.DataPaths.First()), "kept VM missing");
            Assert.That(paths, Does.Not.Contain(gDrop.DataPaths.First()), "excluded VM leaked in");
        }

        [Test]
        public void All_marker_but_single_file_excluded()
        {
            if (!OperatingSystem.IsWindows())
                return; // Hyper-V is only available on Windows

            var g = new HyperVGuest("VM", Guid.NewGuid(),
                                    [@$"C:{DS}VMs{DS}VM{DS}Disk.vhdx",
                                     @$"C:{DS}VMs{DS}VM{DS}State.bin"]);

            var cutFile = g.DataPaths[1];
            var paths = new[] { "%HYPERV%" };
            var filter = $"-{MakeHyperVSource(g.ID)}{DS}{cutFile}";
            var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            { ["snapshot-policy"] = "required" };

            var util = new MockHyperVUtility { Guests = new List<HyperVGuest> { g } };

            using var _ = Duplicati.Library.Logging.Log.StartScope(new LogSink());
            new Duplicati.Library.Modules.Builtin.HyperVOptions()
                .RealParseSourcePaths(ref paths, ref filter, options, util);

            Assert.That(paths, Does.Contain(g.DataPaths[0]), "unaffected file missing");
            Assert.That(paths, Does.Not.Contain(cutFile), "excluded file leaked in");
        }

        [Test]
        public void Include_then_exclude_results_in_inclusion()
        {
            if (!OperatingSystem.IsWindows())
                return; // Hyper-V is only available on Windows

            var g = new HyperVGuest("VM", Guid.NewGuid(),
                                    [@$"C:{DS}VMs{DS}VM{DS}disk.vhdx"]);

            var include = MakeHyperVSource(g.ID);
            var filter = $"{include}{PS}-{include}";
            var paths = new[] { "%HYPERV%" };
            var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            { ["snapshot-policy"] = "required" };

            var util = new MockHyperVUtility { Guests = new List<HyperVGuest> { g } };

            using var _ = Duplicati.Library.Logging.Log.StartScope(new LogSink());
            new Duplicati.Library.Modules.Builtin.HyperVOptions()
                .RealParseSourcePaths(ref paths, ref filter, options, util);

            Assert.That(paths, Does.Contain(g.DataPaths.First()),
                        "first-seen INCLUDE should win over later EXCLUDE");
        }

        [Test]
        public void Exclude_then_include_results_in_exclusion()
        {
            if (!OperatingSystem.IsWindows())
                return; // Hyper-V is only available on Windows

            var g = new HyperVGuest("VM", Guid.NewGuid(),
                                    [@$"C:{DS}VMs{DS}VM{DS}disk.vhdx"]);

            var include = MakeHyperVSource(g.ID);
            var filter = $"-{include}{PS}{include}";
            var paths = new[] { "%HYPERV%" };
            var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            { ["snapshot-policy"] = "required" };

            var util = new MockHyperVUtility { Guests = new List<HyperVGuest> { g } };

            using var _ = Duplicati.Library.Logging.Log.StartScope(new LogSink());
            new Duplicati.Library.Modules.Builtin.HyperVOptions()
                .RealParseSourcePaths(ref paths, ref filter, options, util);

            Assert.That(paths, Does.Not.Contain(g.DataPaths.First()),
                        "first-seen EXCLUDE should win over later INCLUDE");
        }

        [Test]
        public void Direct_guest_source_path_includes_that_guest_only()
        {
            if (!OperatingSystem.IsWindows())
                return; // Hyper-V is only available on Windows

            var gWanted = new HyperVGuest("Wanted", Guid.NewGuid(),
                                          [@$"E:{DS}VMs{DS}Wanted{DS}disk.vhdx"]);
            var gOther = new HyperVGuest("Skip", Guid.NewGuid(),
                                          [@$"E:{DS}VMs{DS}Skip{DS}disk.vhdx"]);

            var paths = new[] { MakeHyperVSource(gWanted.ID) };  // ← only that guest
            var filter = string.Empty;
            var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            { ["snapshot-policy"] = "required" };

            var util = new MockHyperVUtility { Guests = new List<HyperVGuest> { gWanted, gOther } };

            using var _ = Duplicati.Library.Logging.Log.StartScope(new LogSink());
            new Duplicati.Library.Modules.Builtin.HyperVOptions()
                .RealParseSourcePaths(ref paths, ref filter, options, util);

            Assert.That(paths, Does.Contain(gWanted.DataPaths.First()), "wanted VM missing");
            Assert.That(paths, Does.Not.Contain(gOther.DataPaths.First()), "unexpected VM leaked in");
        }
    }
}