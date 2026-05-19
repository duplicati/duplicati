namespace AutoTune;

public record ConfigAutoTune
(
    // Arguments / Options
    List<string> BackendOptions,
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
    int Warmup
);
