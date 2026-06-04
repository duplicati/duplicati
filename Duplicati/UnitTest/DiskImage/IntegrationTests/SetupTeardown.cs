using System;
using System.IO;
using Duplicati.UnitTest.DiskImage.Helpers;
using NUnit.Framework;

namespace Duplicati.UnitTest.DiskImage.IntegrationTests;

/// <summary>
/// Unit tests for the DiskImage backup and restore functionality.
/// These tests use disk image files managed to test the full backup and restore flow.
/// </summary>
[TestFixture]
[Category("DiskImage")]
[Platform("Win,MacOsX,Linux")]
public partial class DiskImageTests : BasicSetupHelper
{
    protected string _sourceImagePath = null!;
    protected string _restoreImagePath = null!;
    protected string _sourceMountPath = null!;
    protected string _restoreMountPath = null!;
    protected IDiskImageHelper _diskHelper = null!;

    protected const long MiB = 1024 * 1024;

    /// <summary>
    /// Sets up the test environment before each test.
    /// Creates the disk helper and temporary disk image paths.
    /// </summary>
    [SetUp]
    public void DiskImageSetUp()
    {
        // Create the appropriate disk image helper for the current platform
        _diskHelper = DiskImageHelperFactory.Create();

        // Check for admin privileges
        if (!_diskHelper.HasRequiredPrivileges())
        {
            Assert.Ignore("DiskImage tests require administrator privileges");
        }

        // Create temp disk image paths
        var extension = DiskImageTestHelpers.GetPlatformDiskImageExtension();
        _sourceImagePath = Path.Combine(DATAFOLDER, $"duplicati_test_source_{Guid.NewGuid()}.{extension}");
        _restoreImagePath = Path.Combine(DATAFOLDER, $"duplicati_test_restore_{Guid.NewGuid()}.{extension}");
        _sourceMountPath = Path.Combine(DATAFOLDER, $"mnt_source_{Guid.NewGuid()}");
        _restoreMountPath = Path.Combine(DATAFOLDER, $"mnt_restore_{Guid.NewGuid()}");
        Directory.CreateDirectory(_sourceMountPath);
        Directory.CreateDirectory(_restoreMountPath);
    }

    /// <summary>
    /// Cleans up the test environment after each test.
    /// Detaches and deletes disk image files.
    /// </summary>
    [TearDown]
    public void DiskImageTearDown()
    {
        // Cleanup disk images
        try
        {
            _diskHelper.CleanupDisk(_sourceImagePath);
        }
        catch (Exception ex)
        {
            TestContext.Progress.WriteLine($"Warning: Failed to cleanup source disk image: {ex.Message}");
        }

        try
        {
            _diskHelper.CleanupDisk(_restoreImagePath);
        }
        catch (Exception ex)
        {
            TestContext.Progress.WriteLine($"Warning: Failed to cleanup restore disk image: {ex.Message}");
        }

        try
        {
            _diskHelper.Dispose();
        }
        catch (Exception ex)
        {
            TestContext.Progress.WriteLine($"Warning: Failed to dispose disk helper: {ex.Message}");
        }

        DiskImageTestHelpers.SafeDeleteFile(_sourceImagePath);
        DiskImageTestHelpers.SafeDeleteFile(_restoreImagePath);
    }
}