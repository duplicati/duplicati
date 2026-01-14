// Copyright (c) 2026 Duplicati Inc. All rights reserved.

namespace Duplicati.Proprietary.Office365.SourceItems;

internal class StreamResourceEntryFunction(string path, DateTime createdUtc, DateTime lastModificationUtc, long size, Func<CancellationToken, Task<Stream>> streamFactory, Func<CancellationToken, Task<Dictionary<string, string?>>>? minorMetadataFactory = null) : StreamResourceEntryBase(path)
{
    public override DateTime CreatedUtc => createdUtc;

    public override DateTime LastModificationUtc => lastModificationUtc;
    public override long Size => _stream == null ? size : _stream.Length;
    private Stream? _stream;

    public async Task<bool> ProbeIfExistsAsync(CancellationToken cancellationToken)
    {
        if (_stream != null)
            return true;

        try
        {
            _stream = await streamFactory(cancellationToken).ConfigureAwait(false);
            return _stream != null;
        }
        catch (FileNotFoundException)
        {
            return false;
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return false;
        }
    }

    public override async Task<Stream> OpenRead(CancellationToken cancellationToken)
        => _stream != null
            ? _stream
            : await streamFactory(cancellationToken).ConfigureAwait(false);

    public override Task<Dictionary<string, string?>> GetMinorMetadata(CancellationToken cancellationToken)
        => minorMetadataFactory != null
            ? minorMetadataFactory(cancellationToken)
            : base.GetMinorMetadata(cancellationToken);
}
