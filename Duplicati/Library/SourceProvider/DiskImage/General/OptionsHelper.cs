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

using System.Collections.Generic;
using Duplicati.Library.Interface;

namespace Duplicati.Library.SourceProvider.DiskImage.General;

/// <summary>
/// Helper class containing constants and command-line argument definitions for the DiskImage module.
/// </summary>
internal static class OptionsHelper
{
    /// <summary>
    /// The module key identifier for the DiskImage module.
    /// </summary>
    internal const string ModuleKey = "diskimage";

    /// <summary>
    /// The module key identifier for restoring disk images to image files.
    /// </summary>
    internal const string ImageFileModuleKey = "diskimage-file";

    /// <summary>
    /// Option to automatically unmount the target disk before restore operations.
    /// </summary>
    internal const string DISK_RESTORE_AUTO_UNMOUNT_OPTION = "diskimage-restore-auto-unmount";

    /// <summary>
    /// Option to skip partition table restoration during restore operations.
    /// </summary>
    internal const string DISK_RESTORE_SKIP_PARTITION_TABLE_OPTION = "diskimage-restore-skip-partition-table";

    /// <summary>
    /// Option to skip validation of the target disk size before restoring.
    /// </summary>
    internal const string DISK_RESTORE_SKIP_SIZE_VALIDATION_OPTION = "diskimage-restore-skip-size-validation";

    /// <summary>
    /// Option to reserve the full size of a restored image file up front instead of creating it sparse.
    /// </summary>
    internal const string DISK_RESTORE_IMAGE_PREALLOCATE_OPTION = "diskimage-restore-image-preallocate";

    /// <summary>
    /// Option to choose the file format of a restored image file.
    /// </summary>
    internal const string DISK_RESTORE_IMAGE_FORMAT_OPTION = "diskimage-restore-image-format";

    /// <summary>
    /// The image format value that chooses the format from the file extension or content.
    /// </summary>
    internal const string ImageFormatAuto = "auto";

    /// <summary>
    /// The values accepted by the image format option.
    /// </summary>
    internal static readonly string[] ImageFormatValues = [ImageFormatAuto, "raw", "vhd", "vhdx"];

    /// <summary>
    /// Option to treat filesystem as unknown (force raw block-based backup).
    /// </summary>
    // internal const string DISK_IMAGE_FILESYSTEM_UNKNOWN_OPTION = "diskimage-filesystem-unknown";

    /// <summary>
    /// Option to opt-in to filesystem parsing (enables filesystem-aware backup).
    /// </summary>
    internal const string DISK_IMAGE_FILESYSTEM_PARSED_OPTION = "diskimage-filesystem-parsed";

    /// <summary>
    /// Gets the list of supported command-line arguments for the DiskImage module.
    /// </summary>
    internal static IList<ICommandLineArgument> SupportedCommands =>
    [
        new CommandLineArgument(DISK_RESTORE_AUTO_UNMOUNT_OPTION, CommandLineArgument.ArgumentType.Boolean, Strings.DiskRestoreAutoUnmountShort, Strings.DiskRestoreAutoUnmountLong, "false"),
        new CommandLineArgument(DISK_RESTORE_SKIP_PARTITION_TABLE_OPTION, CommandLineArgument.ArgumentType.Boolean, Strings.DiskRestoreSkipPartitionTableShort, Strings.DiskRestoreSkipPartitionTableLong, "false"),
        new CommandLineArgument(DISK_RESTORE_SKIP_SIZE_VALIDATION_OPTION, CommandLineArgument.ArgumentType.Boolean, Strings.DiskRestoreSkipSizeValidationShort, Strings.DiskRestoreSkipSizeValidationLong, "false"),
        //new CommandLineArgument(DISK_IMAGE_FILESYSTEM_UNKNOWN_OPTION, CommandLineArgument.ArgumentType.Boolean, Strings.DiskImageFilesystemUnknownShort, Strings.DiskImageFilesystemUnknownLong, "false"),
        new CommandLineArgument(DISK_IMAGE_FILESYSTEM_PARSED_OPTION, CommandLineArgument.ArgumentType.Boolean, Strings.DiskImageFilesystemParsedShort, Strings.DiskImageFilesystemParsedLong, "false"),
    ];

    /// <summary>
    /// Gets the list of supported command-line arguments for restoring to an image file.
    /// </summary>
    internal static IList<ICommandLineArgument> ImageFileSupportedCommands =>
    [
        new CommandLineArgument(DISK_RESTORE_SKIP_PARTITION_TABLE_OPTION, CommandLineArgument.ArgumentType.Boolean, Strings.DiskRestoreSkipPartitionTableShort, Strings.DiskRestoreSkipPartitionTableLong, "false"),
        new CommandLineArgument(DISK_RESTORE_SKIP_SIZE_VALIDATION_OPTION, CommandLineArgument.ArgumentType.Boolean, Strings.DiskRestoreSkipSizeValidationShort, Strings.DiskRestoreSkipSizeValidationLong, "false"),
        new CommandLineArgument(DISK_RESTORE_IMAGE_PREALLOCATE_OPTION, CommandLineArgument.ArgumentType.Boolean, Strings.DiskRestoreImagePreallocateShort, Strings.DiskRestoreImagePreallocateLong, "false"),
        new CommandLineArgument(DISK_RESTORE_IMAGE_FORMAT_OPTION, CommandLineArgument.ArgumentType.Enumeration, Strings.DiskRestoreImageFormatShort, Strings.DiskRestoreImageFormatLong, ImageFormatAuto, null, ImageFormatValues),
    ];
}
