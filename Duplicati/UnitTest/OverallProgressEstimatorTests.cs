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

using Duplicati.Library.Main;
using NUnit.Framework;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

namespace Duplicati.UnitTest
{
    /// <summary>
    /// Unit tests for <see cref="OverallProgressEstimator"/>, which derives the
    /// overall progress reported to report modules from the raw progress counters.
    /// </summary>
    [Category("ReportModule")]
    public class OverallProgressEstimatorTests
    {
        private const float Tolerance = 0.0001f;

        [Test]
        public void ProcessingFilesDerivesProgressFromByteCounters()
        {
            // Real-world sample from a report where the engine reported Progress = 0
            var pg = OverallProgressEstimator.Estimate(
                OperationPhase.Backup_ProcessingFiles,
                reportedProgress: 0f,
                filesProcessed: 58437,
                fileSizeProcessed: 39028884072,
                fileSize: 236032631107,
                countingFiles: false,
                currentFileOffset: 20878196736,
                currentFileComplete: false);

            var expected = (39028884072d + 20878196736d) / 236032631107d;
            Assert.AreEqual((float)expected, pg, Tolerance);
            Assert.That(pg, Is.GreaterThan(0.25f).And.LessThan(0.26f));
        }

        [Test]
        public void ProcessingFilesIgnoresOffsetWhenCurrentFileIsComplete()
        {
            var pg = OverallProgressEstimator.Estimate(
                OperationPhase.Backup_ProcessingFiles,
                reportedProgress: 0f,
                filesProcessed: 10,
                fileSizeProcessed: 500,
                fileSize: 1000,
                countingFiles: false,
                currentFileOffset: 200,
                currentFileComplete: true);

            Assert.AreEqual(0.5f, pg, Tolerance);
        }

        [Test]
        public void ProcessingFilesIsZeroWhileCounting()
        {
            var pg = OverallProgressEstimator.Estimate(
                OperationPhase.Backup_ProcessingFiles,
                reportedProgress: 0f,
                filesProcessed: 10,
                fileSizeProcessed: 500,
                fileSize: 1000,
                countingFiles: true,
                currentFileOffset: 0,
                currentFileComplete: false);

            Assert.AreEqual(0f, pg, Tolerance);
        }

        [Test]
        public void ProcessingFilesIsZeroBeforeFirstFileCompletes()
        {
            var pg = OverallProgressEstimator.Estimate(
                OperationPhase.Backup_ProcessingFiles,
                reportedProgress: 0f,
                filesProcessed: 0,
                fileSizeProcessed: 0,
                fileSize: 1000,
                countingFiles: false,
                currentFileOffset: 900,
                currentFileComplete: false);

            Assert.AreEqual(0f, pg, Tolerance);
        }

        [Test]
        public void ProcessingFilesHandlesZeroTotalSizeWithoutNaN()
        {
            var pg = OverallProgressEstimator.Estimate(
                OperationPhase.Backup_ProcessingFiles,
                reportedProgress: 0f,
                filesProcessed: 1,
                fileSizeProcessed: 0,
                fileSize: 0,
                countingFiles: false,
                currentFileOffset: 0,
                currentFileComplete: true);

            Assert.IsFalse(float.IsNaN(pg));
            Assert.AreEqual(0f, pg, Tolerance);
        }

        [Test]
        public void ProcessingFilesIsCappedBelowOne()
        {
            var pg = OverallProgressEstimator.Estimate(
                OperationPhase.Backup_ProcessingFiles,
                reportedProgress: 0f,
                filesProcessed: 100,
                fileSizeProcessed: 1000,
                fileSize: 1000,
                countingFiles: false,
                currentFileOffset: 0,
                currentFileComplete: true);

            Assert.AreEqual(OverallProgressEstimator.ProcessingFilesCap, pg, Tolerance);
        }

        [Test]
        public void RestoreDownloadPhaseUsesByteCounters()
        {
            var pg = OverallProgressEstimator.Estimate(
                OperationPhase.Restore_DownloadingRemoteFiles,
                reportedProgress: 0f,
                filesProcessed: 3,
                fileSizeProcessed: 250,
                fileSize: 1000,
                countingFiles: false,
                currentFileOffset: 0,
                currentFileComplete: true);

            Assert.AreEqual(0.25f, pg, Tolerance);
        }

        [TestCase(OperationPhase.Backup_Finalize, 0.90f)]
        [TestCase(OperationPhase.Backup_WaitForUpload, 0.90f)]
        [TestCase(OperationPhase.Backup_Delete, 0.95f)]
        [TestCase(OperationPhase.Backup_Compact, 0.95f)]
        [TestCase(OperationPhase.Backup_VerificationUpload, 0.98f)]
        [TestCase(OperationPhase.Backup_PostBackupVerify, 0.98f)]
        [TestCase(OperationPhase.Backup_Complete, 1f)]
        [TestCase(OperationPhase.Restore_Complete, 1f)]
        public void LaterBackupPhasesUseFixedValues(OperationPhase phase, float expected)
        {
            // These phases never report progress, so the byte counters are irrelevant
            var pg = OverallProgressEstimator.Estimate(
                phase,
                reportedProgress: 0f,
                filesProcessed: 0,
                fileSizeProcessed: 0,
                fileSize: 0,
                countingFiles: false,
                currentFileOffset: 0,
                currentFileComplete: false);

            Assert.AreEqual(expected, pg, Tolerance);
        }

        [TestCase(OperationPhase.Recreate_Running, 0.42f, 0.42f)]
        [TestCase(OperationPhase.Verify_Running, 0.75f, 0.75f)]
        [TestCase(OperationPhase.Backup_RemoteSynchronization, 0.3f, 0.3f)]
        [TestCase(OperationPhase.Backup_Begin, 0f, 0f)]
        [TestCase(OperationPhase.Recreate_Running, 1.5f, 1f)]
        [TestCase(OperationPhase.Recreate_Running, -0.5f, 0f)]
        [TestCase(OperationPhase.Recreate_Running, float.NaN, 0f)]
        public void OtherPhasesPassThroughReportedProgressClamped(OperationPhase phase, float reported, float expected)
        {
            var pg = OverallProgressEstimator.Estimate(
                phase,
                reportedProgress: reported,
                filesProcessed: 5,
                fileSizeProcessed: 500,
                fileSize: 1000,
                countingFiles: false,
                currentFileOffset: 0,
                currentFileComplete: false);

            Assert.AreEqual(expected, pg, Tolerance);
        }
    }
}
