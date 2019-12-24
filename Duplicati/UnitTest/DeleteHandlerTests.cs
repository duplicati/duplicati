using System;
using System.Collections.Generic;
using System.Linq;
using Duplicati.Library.Interface;
using Duplicati.Library.Main;
using Duplicati.Library.Main.Database;
using Duplicati.Library.Main.Operation;
using NUnit.Framework;

namespace Duplicati.UnitTest
{
    public class DeleteHandlerTests
    {
        private class Fileset : IListResultFileset, IEquatable<Fileset>
        {
            public long Version { get; }
            public int IsFullBackup { get; }
            public DateTime Time { get; }
            public long FileCount { get; } = 0;
            public long FileSizes { get; } = 0;

            public Fileset(int version, int backupType, DateTime time)
            {
                this.Version = version;
                this.IsFullBackup = backupType;
                this.Time = time;
            }

            public bool Equals(Fileset other)
            {
                if (ReferenceEquals(null, other)) return false;
                if (ReferenceEquals(this, other)) return true;
                return this.Version == other.Version && this.IsFullBackup == other.IsFullBackup && this.Time.Equals(other.Time) && this.FileCount == other.FileCount && this.FileSizes == other.FileSizes;
            }
        }

        [Test]
        [Category("DeleteHandler")]
        public void KeepTimeRemover()
        {
            DateTime today = DateTime.Today;
            List<IListResultFileset> filesets = new List<IListResultFileset>
            {
                new Fileset(0, BackupType.FULL_BACKUP, today.AddDays(-2)),
                new Fileset(1, BackupType.FULL_BACKUP, today.AddDays(-1)),
                new Fileset(2, BackupType.PARTIAL_BACKUP, today),
                new Fileset(3, BackupType.PARTIAL_BACKUP, today.AddDays(1)),
            };

            // Although version 1 is older than the specified TimeSpan, we do not delete
            // it because it is the most recent full backup.
            TimeSpan timeSpan = new TimeSpan(1, 0, 0, 0);
            IListResultFileset[] expectedFilesetsToRemove = {filesets[0]};

            Random random = new Random();
            Options options = new Options(new Dictionary<string, string> {{"keep-time", $"{(int) timeSpan.TotalSeconds}s"}});
            KeepTimeRemover remover = new KeepTimeRemover(options);
            IListResultFileset[] filesetsToRemove = remover.GetFilesetsToDelete(filesets.OrderBy(x => random.Next())).ToArray();
            CollectionAssert.AreEquivalent(expectedFilesetsToRemove, filesetsToRemove);

            // If there is a full backup within the specified TimeSpan, we can respect
            // the TimeSpan strictly.
            filesets.Add(new Fileset(4, BackupType.FULL_BACKUP, today.AddDays(2)));
            CollectionAssert.AreEquivalent(expectedFilesetsToRemove, filesetsToRemove);
            expectedFilesetsToRemove = filesets.Where(x => x.Time < options.KeepTime).ToArray();
            filesetsToRemove = remover.GetFilesetsToDelete(filesets).ToArray();

            CollectionAssert.AreEquivalent(expectedFilesetsToRemove, filesetsToRemove);
        }

        [Test]
        [Category("DeleteHandler")]
        public void KeepVersionsRemover()
        {
            IListResultFileset[] filesets =
            {
                new Fileset(0, BackupType.FULL_BACKUP, new DateTime(2000, 1, 1)),
                new Fileset(1, BackupType.PARTIAL_BACKUP, new DateTime(2000, 1, 2)),
                new Fileset(2, BackupType.FULL_BACKUP, new DateTime(2000, 1, 3)),
                new Fileset(3, BackupType.PARTIAL_BACKUP, new DateTime(2000, 1, 4)),
                new Fileset(4, BackupType.FULL_BACKUP, new DateTime(2000, 1, 5)),
                new Fileset(5, BackupType.PARTIAL_BACKUP, new DateTime(2000, 1, 6)),
            };

            Options options = new Options(new Dictionary<string, string> {{"keep-versions", "2"}});
            IListResultFileset[] expectedFilesetsToRemove =
            {
                filesets[0], // Delete; third oldest full backup.
                filesets[1], // Delete; intermediate partial backup.
                filesets[3] // Delete; intermediate partial backup.
            };

            Random random = new Random();
            KeepVersionsRemover remover = new KeepVersionsRemover(options);
            IListResultFileset[] filesetsToRemove = remover.GetFilesetsToDelete(filesets.OrderBy(x => random.Next())).ToArray();

            CollectionAssert.AreEquivalent(expectedFilesetsToRemove, filesetsToRemove);
        }

        [Test]
        [Category("DeleteHandler")]
        public void RetentionPolicyRemover()
        {
            Options options = new Options(new Dictionary<string, string> {{"retention-policy", "1W:U,3M:1D,1Y:1W,U:1M"}});

            DateTime now = DateTime.Now;
            IListResultFileset[] filesets =
            {
                // Past week.  These should all be retained.
                new Fileset(0, BackupType.PARTIAL_BACKUP, now),
                new Fileset(1, BackupType.FULL_BACKUP, now.AddMilliseconds(-1)),
                new Fileset(2, BackupType.FULL_BACKUP, now.AddSeconds(-1)),
                new Fileset(3, BackupType.PARTIAL_BACKUP, now.AddMinutes(-1)),
                new Fileset(4, BackupType.PARTIAL_BACKUP, now.AddHours(-1)),
                new Fileset(5, BackupType.PARTIAL_BACKUP, now.AddDays(-1)),
                new Fileset(6, BackupType.FULL_BACKUP, now.AddDays(-6)),

                // Past 3 months.
                new Fileset(7, BackupType.FULL_BACKUP, now.AddDays(-8)), // Keep; first in interval.
                new Fileset(8, BackupType.FULL_BACKUP, now.AddDays(-8).AddHours(1)), // Delete; second in interval.
                new Fileset(9, BackupType.FULL_BACKUP, now.AddDays(-8).AddHours(2)), // Delete; third in interval
                new Fileset(10, BackupType.PARTIAL_BACKUP, now.AddMonths(-1)), // Keep; partial
                new Fileset(11, BackupType.PARTIAL_BACKUP, now.AddMonths(-1).AddHours(1)), // Keep; partial
                new Fileset(12, BackupType.FULL_BACKUP, now.AddMonths(-1).AddHours(2)), // Keep; first full in interval.  Do not discard full in favor of partial.
                new Fileset(13, BackupType.FULL_BACKUP, now.AddMonths(-2)), // Keep; first in interval
                new Fileset(14, BackupType.FULL_BACKUP, now.AddMonths(-2).AddHours(1)), // Delete; second in interval.
                new Fileset(15, BackupType.FULL_BACKUP, now.AddDays(-89).AddHours(1)), // Keep; first in interval.
                new Fileset(16, BackupType.FULL_BACKUP, now.AddDays(-89).AddHours(2)), // Delete; second in interval.

                // Past year.
                new Fileset(17, BackupType.FULL_BACKUP, now.AddDays(-92)), // Keep; first in interval.
                new Fileset(18, BackupType.FULL_BACKUP, now.AddDays(-91)), // Delete; second in interval.
                new Fileset(19, BackupType.PARTIAL_BACKUP, now.AddDays(-(90 + 73))), // Keep; partial
                new Fileset(20, BackupType.PARTIAL_BACKUP, now.AddDays(-(90 + 72))), // Keep; partial.
                new Fileset(21, BackupType.PARTIAL_BACKUP, now.AddDays(-(90 + 71))), // Keep; first full in interval.  Do not discard full in favor of partial.
                new Fileset(22, BackupType.FULL_BACKUP, now.AddDays(-(90 + 142))), // Keep; first in interval.
                new Fileset(23, BackupType.FULL_BACKUP, now.AddDays(-(90 + 141))), // Delete; second in interval.

                // Unlimited.
                new Fileset(24, BackupType.FULL_BACKUP, now.AddYears(-1).AddMonths(-1)), // Keep; first in interval.
                new Fileset(25, BackupType.FULL_BACKUP, now.AddYears(-1).AddMonths(-1).AddDays(1)), // Delete; second in interval.
                new Fileset(26, BackupType.PARTIAL_BACKUP, new DateTime(1, 1, 1)), // Keep; partial
                new Fileset(27, BackupType.FULL_BACKUP, new DateTime(1, 1, 30)), // Keep; first full in interval.  Do not discard full in favor of partial.
            };

            IListResultFileset[] expectedFilesetsToRemove =
            {
                // 3M:1D
                filesets[8],
                filesets[9],
                filesets[14],
                filesets[16],

                // 1Y:1W
                filesets[18],
                filesets[23],

                // U:1M
                filesets[25],
            };

            Random random = new Random();
            RetentionPolicyRemover remover = new RetentionPolicyRemover(options);
            IListResultFileset[] filesetsToRemove = remover.GetFilesetsToDelete(filesets.OrderBy(x => random.Next())).ToArray();

            CollectionAssert.AreEquivalent(expectedFilesetsToRemove, filesetsToRemove);
        }

        [Test]
        [Category("DeleteHandler")]
        public void SpecificVersionsRemover()
        {
            IListResultFileset[] filesets =
            {
                new Fileset(0, BackupType.FULL_BACKUP, new DateTime(2000, 1, 1)),
                new Fileset(1, BackupType.PARTIAL_BACKUP, new DateTime(2000, 1, 2)),
                new Fileset(2, BackupType.FULL_BACKUP, new DateTime(2000, 1, 3)),
                new Fileset(3, BackupType.PARTIAL_BACKUP, new DateTime(2000, 1, 4)),
                new Fileset(4, BackupType.FULL_BACKUP, new DateTime(2000, 1, 5))
            };

            Options options = new Options(new Dictionary<string, string> {{"version", "0,3,4"}});
            IListResultFileset[] expectedFilesetsToRemove =
            {
                filesets[0],
                filesets[3],
                filesets[4]
            };

            Random random = new Random();
            SpecificVersionsRemover remover = new SpecificVersionsRemover(options);
            IListResultFileset[] filesetsToRemove = remover.GetFilesetsToDelete(filesets.OrderBy(x => random.Next())).ToArray();

            CollectionAssert.AreEquivalent(expectedFilesetsToRemove, filesetsToRemove);
        }
    }
}