using System.Linq;
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

        [Test]
        [Category("ControllerMultiLogTarget")]
        public void NestedResultCollectsInPlaceOfThePrimaryTarget()
        {
            var primary = new TestDestination();
            var sink = new TestDestination();
            var nested = new TestDestination();
            using var logger = new ControllerMultiLogTarget(primary, LogMessageType.Information, null, null);
            logger.AddTarget(sink, LogMessageType.Information, null);

            LogEntry Entry(string id) => new LogEntry("msg", System.Array.Empty<object>(), LogMessageType.Information, "tag", id, null);

            using (logger.CollectInNestedResult(nested))
            {
                logger.WriteMessage(Entry("inner"));
                Assert.Throws<System.InvalidOperationException>(() => logger.CollectInNestedResult(new TestDestination()), "Only one nested result at a time");
            }
            logger.WriteMessage(Entry("outer"));

            Assert.That(nested.Entries.Select(x => x.Id), Is.EqualTo(new[] { "inner" }), "The nested result gets the messages while active");
            Assert.That(primary.Entries.Select(x => x.Id), Is.EqualTo(new[] { "outer" }), "The primary target is skipped while a nested result collects");
            Assert.That(sink.Entries.Select(x => x.Id), Is.EqualTo(new[] { "inner", "outer" }), "The other destinations get every message");
        }
    }
}
