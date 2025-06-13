using System.Collections.Generic;
using NUnit.Framework;
using Duplicati.Library.Main;
using Duplicati.Library.Logging;
using Duplicati.Library.Utility;

namespace Duplicati.UnitTest
{
    [TestFixture]
    public class ControllerMultiLogTargetTests
    {
        private class TestDestination : ILogDestination
        {
            public readonly List<LogEntry> Entries = new();
            public void WriteMessage(LogEntry entry) => Entries.Add(entry);
        }

        private class DenyAllFilter : IFilter
        {
            public bool Empty => false;
            public bool Matches(string entry, out bool result, out IFilter match)
            {
                result = false;
                match = this;
                return true;
            }
            public string GetFilterHash() => string.Empty;
        }

        [Test]
        [Category("ControllerMultiLogTarget")]
        public void WarningIsConvertedToInformationWhenSuppressed()
        {
            var dest = new TestDestination();
            using var logger = new ControllerMultiLogTarget(dest, LogMessageType.Information, null, new HashSet<string> { "ID1" });
            var entry = new LogEntry("msg", System.Array.Empty<object>(), LogMessageType.Warning, "tag", "ID1", null);
            logger.WriteMessage(entry);
            Assert.That(dest.Entries.Count, Is.EqualTo(1));
            Assert.That(dest.Entries[0].Level, Is.EqualTo(LogMessageType.Information));
        }

        [Test]
        [Category("ControllerMultiLogTarget")]
        public void FiltersAreRespectedAndNullTargetIsIgnored()
        {
            var dest1 = new TestDestination();
            using var logger = new ControllerMultiLogTarget(dest1, LogMessageType.Information, null, null);

            var dest2 = new TestDestination();
            logger.AddTarget(dest2, LogMessageType.Information, new DenyAllFilter());
            logger.AddTarget(null, LogMessageType.Error, null); // should be ignored

            var entry = new LogEntry("msg", System.Array.Empty<object>(), LogMessageType.Information, "tag", "id", null);
            logger.WriteMessage(entry);

            Assert.That(dest1.Entries.Count, Is.EqualTo(1));
            Assert.That(dest2.Entries.Count, Is.EqualTo(0));
        }
    }
}
