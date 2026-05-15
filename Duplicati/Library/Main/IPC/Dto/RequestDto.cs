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

using System;
using System.Collections.Generic;
using System.Linq;
using Duplicati.Library.Interface;
using Duplicati.Library.Utility;

#nullable enable

namespace Duplicati.Library.Main.IPC.Dto;

/// <summary>
/// DTO for filter
/// </summary>
[Serializable]
public class FilterDto
{
    /// <summary>
    /// The filter expressions
    /// </summary>
    public List<FilterEntryDto> Entries { get; set; } = new();

    /// <summary>
    /// Converts a filter to a DTO
    /// </summary>
    public static FilterDto? FromFilter(IFilter? filter)
    {
        if (filter == null || filter.Empty) return null;

        var dto = new FilterDto();

        // Use the Serialize method to get the filter strings
        var serialized = FilterExpression.Serialize(filter);
        if (serialized != null)
        {
            foreach (var item in serialized)
            {
                if (string.IsNullOrEmpty(item) || item.Length < 2)
                    continue;

                bool include = item[0] == '+';
                string expression = item.Substring(1);

                dto.Entries.Add(new FilterEntryDto
                {
                    Expression = expression,
                    Include = include
                });
            }
        }

        return dto;
    }

    /// <summary>
    /// Converts the DTO to a filter
    /// </summary>
    public IFilter? ToFilter()
    {
        if (Entries == null || Entries.Count == 0) return null;

        var items = Entries
            .Select(e => new FilterExpression(e.Expression, e.Include))
            .Cast<IFilter?>();

        return items.Aggregate((a, b) => FilterExpression.Combine(a, b));
    }
}

/// <summary>
/// DTO for a single filter entry
/// </summary>
[Serializable]
public class FilterEntryDto
{
    public required string Expression { get; set; }
    public required bool Include { get; set; }
}

/// <summary>
/// DTO for log entry
/// </summary>
[Serializable]
public class LogEntryDto
{
    public required DateTime When { get; set; }
    public required string Message { get; set; }
    public required object[]? Arguments { get; set; }
    public required string Level { get; set; }
    public required string FilterTag { get; set; }
    public required string Tag { get; set; }
    public required string Id { get; set; }
    public required ExceptionDto? Exception { get; set; }
    public required Dictionary<string, string> Context { get; set; }

    /// <summary>
    /// Converts a LogEntry to a DTO
    /// </summary>
    public static LogEntryDto? FromEntry(Logging.LogEntry entry)
    {
        if (entry == null) return null;

        var dto = new LogEntryDto
        {
            When = entry.When,
            Message = entry.Message,
            Arguments = entry.Arguments?.Select(a => a?.ToString()).Cast<object>().ToArray(),
            Level = entry.Level.ToString(),
            FilterTag = entry.FilterTag,
            Tag = entry.Tag,
            Id = entry.Id,
            Exception = ExceptionDto.FromException(entry.Exception),
            Context = []
        };

        // Copy context keys
        if (entry.ContextKeys != null)
        {
            foreach (var key in entry.ContextKeys)
            {
                dto.Context[key] = entry[key];
            }
        }

        return dto;
    }

    /// <summary>
    /// Converts the DTO back to a LogEntry
    /// </summary>
    public Logging.LogEntry ToLogEntry()
    {
        var level = Enum.TryParse<Logging.LogMessageType>(Level, out var parsedLevel)
            ? parsedLevel
            : Logging.LogMessageType.Information;

        var entry = new Logging.LogEntry(
            Message,
            Arguments,
            level,
            Tag,
            Id,
            Exception?.ToException()
        );

        // Restore context
        if (Context != null)
        {
            foreach (var kvp in Context)
            {
                entry[kvp.Key] = kvp.Value;
            }
        }

        return entry;
    }
}

/// <summary>
/// DTO for exception
/// </summary>
[Serializable]
public class ExceptionDto
{
    public required string Type { get; set; }
    public required string Message { get; set; }
    public required string? StackTrace { get; set; }
    public required ExceptionDto? InnerException { get; set; }
    public required string? HelpLink { get; set; }
    public string? HelpId { get; set; }

    /// <summary>
    /// Converts an Exception to a DTO
    /// </summary>
    public static ExceptionDto? FromException(Exception? ex)
    {
        if (ex == null) return null;

        var dto = new ExceptionDto
        {
            Type = ex.GetType().FullName ?? "Unknown-type",
            Message = ex.Message,
            StackTrace = ex.StackTrace,
            InnerException = FromException(ex.InnerException),
            HelpLink = ex.HelpLink
        };

        // Handle UserInformationException
        if (ex is UserInformationException uie)
            dto.HelpId = uie.HelpID;

        return dto;
    }

    /// <summary>
    /// Converts the DTO back to an Exception
    /// </summary>
    public Exception ToException()
    {
        if (Type == typeof(Library.Interface.UserInformationException).FullName)
            return new RpcMappedUserInformationException(Type, Message, StackTrace ?? "", HelpId ?? "RPC-Unknown");

        // For simplicity, create a generic Exception with the details
        // The exact type cannot always be reconstructed across process boundaries
        return new RpcMappedException(Type, Message, StackTrace ?? "")
        {
            HelpLink = HelpLink
        };
    }


    private class RpcMappedException(string Type, string Message, string Stack) : Exception(Message)
    {
        public override string ToString()
        {
            return $"[{Type}] {Message}\n{Stack}";
        }
    }

    private class RpcMappedUserInformationException(string Type, string Message, string Stack, string HelpId) : RpcMappedException(Type, Message, Stack)
    {
        public override string ToString()
        {
            return $"{Message}\nHelpId: {HelpId}";
        }
    }
}

/// <summary>
/// DTO for operation result wrapper
/// </summary>
[Serializable]
public class OperationResultDto
{
    public required string OperationType { get; set; }
    public required BasicResultsDto Results { get; set; }

    public static OperationResultDto? FromResults(IBasicResults results)
    {
        if (results == null) return null;

        return new OperationResultDto
        {
            OperationType = results.GetType().Name,
            Results = BasicResultsDto.FromResults(results)
        };
    }
}

/// <summary>
/// DTO for secret provider
/// </summary>
[Serializable]
public class SecretProviderDto
{
    // TODO: Need to figure out how to handle the actual provider settings

    public required string ProviderType { get; set; }

    public static SecretProviderDto? FromProvider(Library.Interface.ISecretProvider provider)
    {
        if (provider == null) return null;

        // For RPC, we primarily need to identify the provider type
        // The actual secrets will be resolved by the provider in the child process
        return new SecretProviderDto
        {
            ProviderType = provider.GetType().FullName ?? "Unknown-provider",
        };
    }

    public Library.Interface.ISecretProvider? ToProvider()
    {
        // The provider would need to be reconstructed based on type and settings
        // This is a simplified implementation as we do not need it
        return null;
    }
}

/// <summary>
/// DTO for backend action progress
/// </summary>
[Serializable]
public class BackendActionProgressDto
{
    public BackendActionType Action { get; set; }
    public string Path { get; set; } = string.Empty;
    public long Size { get; set; }
    public long Progress { get; set; }
    public long BytesPerSecond { get; set; }
    public bool IsBlocking { get; set; }

    public static BackendActionProgressDto? FromProgress(BackendActionProgress? progress)
    {
        if (progress == null) return null;
        return new BackendActionProgressDto
        {
            Action = progress.Action,
            Path = progress.Path,
            Size = progress.Size,
            Progress = progress.Progress,
            BytesPerSecond = progress.BytesPerSecond,
            IsBlocking = progress.IsBlocking
        };
    }
}

/// <summary>
/// DTO for backend progress
/// </summary>
[Serializable]
public class BackendProgressDto
{
    public List<BackendActionProgressDto> Transfers { get; set; } = new();

    public static BackendProgressDto? FromProgress(IBackendProgress? progress)
    {
        if (progress == null) return null;
        return new BackendProgressDto
        {
            Transfers = progress.GetActiveTransfers()?.Select(BackendActionProgressDto.FromProgress).Where(x => x != null).Cast<BackendActionProgressDto>().ToList() ?? new List<BackendActionProgressDto>()
        };
    }
}

/// <summary>
/// DTO for operation progress
/// </summary>
[Serializable]
public class OperationProgressDto
{
    public OperationPhase Phase { get; set; }
    public float Progress { get; set; }
    public long FilesProcessed { get; set; }
    public long FileSizeProcessed { get; set; }
    public long FileCount { get; set; }
    public long FileSize { get; set; }
    public bool CountingFiles { get; set; }
    public string? CurrentFilename { get; set; }
    public long CurrentFileSize { get; set; }
    public long CurrentFileOffset { get; set; }
    public bool CurrentFileComplete { get; set; }
    public int RemoteSyncDestinationIndex { get; set; }
    public int RemoteSyncDestinationCount { get; set; }

    public static OperationProgressDto? FromProgress(IOperationProgress? progress)
    {
        if (progress == null) return null;
        var dto = new OperationProgressDto();
        progress.UpdateOverall(out var phase, out var pg, out var filesProcessed, out var fileSizeProcessed, out var fileCount, out var fileSize, out var countingFiles);
        dto.Phase = phase;
        dto.Progress = pg;
        dto.FilesProcessed = filesProcessed;
        dto.FileSizeProcessed = fileSizeProcessed;
        dto.FileCount = fileCount;
        dto.FileSize = fileSize;
        dto.CountingFiles = countingFiles;
        progress.UpdateFile(out var currentFilename, out var currentFileSize, out var currentFileOffset, out var currentFileComplete);
        dto.CurrentFilename = currentFilename;
        dto.CurrentFileSize = currentFileSize;
        dto.CurrentFileOffset = currentFileOffset;
        dto.CurrentFileComplete = currentFileComplete;
        progress.UpdateRemoteSyncDestination(out var destinationIndex, out var destinationCount);
        dto.RemoteSyncDestinationIndex = destinationIndex;
        dto.RemoteSyncDestinationCount = destinationCount;
        return dto;
    }
}
