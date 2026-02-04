// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using Duplicati.Library.Interface;

namespace Duplicati.Proprietary.GoogleWorkspace;

public sealed class RestoreProvider : IRestoreDestinationProviderModule, IDisposable
{
    private readonly APIHelper _apiHelper;
    private readonly string _restorePath;
    private readonly OptionsHelper.GoogleWorkspaceOptions _options;
    private readonly string _tempPath;
    private readonly Dictionary<string, string> _tempFiles = new();
    private readonly Dictionary<string, Dictionary<string, string?>> _metadata = new();

    public RestoreProvider()
    {
        _apiHelper = null!;
        _restorePath = null!;
        _options = null!;
        _tempPath = null!;
    }

    public RestoreProvider(string url, string restorePath, Dictionary<string, string?> options)
    {
        _restorePath = restorePath;
        _options = OptionsHelper.ParseOptions(options);
        _apiHelper = new APIHelper(_options);

        _tempPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "Duplicati-GoogleWorkspace-Restore-" + Guid.NewGuid().ToString());
        System.IO.Directory.CreateDirectory(_tempPath);
    }

    public string Key => OptionsHelper.ModuleKey;

    public string DisplayName => Strings.Common.DisplayName;

    public string Description => Strings.Common.Description;

    public IList<ICommandLineArgument> SupportedCommands => OptionsHelper.SupportedCommands;

    public string TargetDestination => _restorePath;

    public void Dispose()
    {
        if (!string.IsNullOrEmpty(_tempPath) && System.IO.Directory.Exists(_tempPath))
        {
            try
            {
                System.IO.Directory.Delete(_tempPath, true);
            }
            catch { }
        }
    }

    public Task Initialize(CancellationToken cancellationToken)
    {
        _apiHelper.Initialize();
        return Task.CompletedTask;
    }

    public Task Test(CancellationToken cancellationToken)
    {
        _apiHelper.TestConnection();
        return Task.CompletedTask;
    }

    public Task<bool> CreateFolderIfNotExists(string path, CancellationToken cancellationToken)
    {
        return Task.FromResult(true);
    }

    public Task<bool> FileExists(string path, CancellationToken cancellationToken)
    {
        return Task.FromResult(_tempFiles.ContainsKey(path));
    }

    public Task<Stream> OpenWrite(string path, CancellationToken cancellationToken)
    {
        var tempFile = System.IO.Path.Combine(_tempPath, Guid.NewGuid().ToString());
        _tempFiles[path] = tempFile;
        return Task.FromResult<Stream>(new FileStream(tempFile, FileMode.Create, FileAccess.Write, FileShare.None));
    }

    public Task<Stream> OpenRead(string path, CancellationToken cancellationToken)
    {
        if (_tempFiles.TryGetValue(path, out var tempFile) && System.IO.File.Exists(tempFile))
        {
            return Task.FromResult<Stream>(new FileStream(tempFile, FileMode.Open, FileAccess.Read, FileShare.Read));
        }
        throw new FileNotFoundException("File not found in temp storage", path);
    }

    public Task<Stream> OpenReadWrite(string path, CancellationToken cancellationToken)
    {
        if (_tempFiles.TryGetValue(path, out var tempFile) && System.IO.File.Exists(tempFile))
        {
            return Task.FromResult<Stream>(new FileStream(tempFile, FileMode.Open, FileAccess.ReadWrite, FileShare.None));
        }
        throw new FileNotFoundException("File not found in temp storage", path);
    }

    public Task<long> GetFileLength(string path, CancellationToken cancellationToken)
    {
        if (_tempFiles.TryGetValue(path, out var tempFile) && System.IO.File.Exists(tempFile))
        {
            return Task.FromResult(new FileInfo(tempFile).Length);
        }
        return Task.FromResult(-1L);
    }

    public Task<bool> HasReadOnlyAttribute(string path, CancellationToken cancellationToken)
    {
        return Task.FromResult(false);
    }

    public Task ClearReadOnlyAttribute(string path, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public Task<bool> WriteMetadata(string path, Dictionary<string, string?> metadata, bool isFolder, bool isRoot, CancellationToken cancellationToken)
    {
        _metadata[path] = metadata;
        return Task.FromResult(true);
    }

    public Task DeleteFolder(string path, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public Task DeleteFile(string path, CancellationToken cancellationToken)
    {
        if (_tempFiles.TryGetValue(path, out var tempFile))
        {
            if (System.IO.File.Exists(tempFile))
                System.IO.File.Delete(tempFile);
            _tempFiles.Remove(path);
        }
        return Task.CompletedTask;
    }

    public Task Finalize(Action<double>? progress, CancellationToken cancellationToken)
    {
        // TODO: Implement restore logic based on _tempFiles and _metadata
        return Task.CompletedTask;
    }
}
