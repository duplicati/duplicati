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

namespace Duplicati.AutoTune;

/// <summary>
/// Configuration input record for the AutoTune tool.
/// </summary>
/// <param name="BackendOptions">Duplicati options to pass to the backend during backup. Each option is a key-value pair separated by an equals sign, e.g. key1=value1 key2=value2.</param>
/// <param name="BaselineParams">The step value(s) to consider the baseline for the final comparison, 1 or 4 integers. If one value is specified, the same value is used for all parameters. If four values are specified, they are applied individually for file-processors, volume-decompressors, volume-decryptors, and volume-downloaders (in that order). If not specified, the default Duplicati parameters are used.</param>
/// <param name="Destination">Destination to store the test backup. The destination should be empty (as required by Duplicati). The data will be deleted again after the tuning process. If null, a temporary folder is used.</param>
/// <param name="DontRevisitParameters">When true, once a new better configuration has been found, already visited candidate parameters are excluded from subsequent tuning rounds. This makes tuning converge faster, but may not find an optimal configuration.</param>
/// <param name="ExponentialSteps">When true, the step size for the next candidate run is doubled instead of incremented by 1. This makes tuning converge faster, but may not find an optimal configuration.</param>
/// <param name="RestoreTarget">Target folder to restore a backup to. The folder should be empty beforehand. If null, a temporary folder is used.</param>
/// <param name="Runs">Number of runs to measure per configuration; the mean is reported.</param>
/// <param name="SourceFolder">Source folder to make a backup of. If the folder is empty, test data will be generated. If null, a temporary folder is used.</param>
/// <param name="TempFolder">Path to where temporary files should be created. If null, the system default (e.g. /tmp or %TEMP%) is used.</param>
/// <param name="TestdataMaxFileSize">When generating test data, the maximum size (in bytes) of a single generated file.</param>
/// <param name="TestdataMaxTotalSize">When generating test data, the maximum collective size (in bytes) of all generated files.</param>
/// <param name="TestdataNumFiles">When generating test data, the number of files to generate.</param>
/// <param name="TestdataSparseFactor">When generating test data, defines the amount of data that should explicitly be set 0 to force deduplication.</param>
/// <param name="StartingSteps">The starting concurrency values for the tunable parameters. Accepts 1 or 4 integer values. One value applies to all four parameters; four values apply individually to file-processors, volume-decompressors, volume-decryptors, and volume-downloaders (in that order). Defaults to an empty array, which is treated as 1 for every parameter.</param>
/// <param name="UseDefaultSettings">When true, tuning starts from the Duplicati built-in default settings instead of starting from 1 for all parameters. Ignored if --starting-steps is also specified.</param>
/// <param name="Verbose">Verbosity level: 0 disables output, 1 prints full progress information during tuning runs. Higher levels reserved for future debug printing. Defaults to 1.</param>
/// <param name="Warmup">Number of warmup runs to perform before measuring.</param>
public record ConfigAutoTune
(
    // Arguments / Options
    List<string> BackendOptions,
    int[] BaselineParams,
    string? Destination,
    bool DontRevisitParameters,
    bool ExponentialSteps,
    string? RestoreTarget,
    int Runs,
    string? SourceFolder,
    string? TempFolder,
    long TestdataMaxFileSize,
    long TestdataMaxTotalSize,
    long TestdataNumFiles,
    int TestdataSparseFactor,
    int[] StartingSteps,
    bool UseDefaultSettings,
    int Verbose,
    int Warmup
);
