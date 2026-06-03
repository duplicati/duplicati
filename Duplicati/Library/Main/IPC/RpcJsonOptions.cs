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
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Duplicati.Library.Interface;
using Duplicati.Library.Main.IPC.Dto;
using Duplicati.Library.Utility;

#nullable enable

namespace Duplicati.Library.Main.IPC;

/// <summary>
/// Provides JSON serializer options for RPC communication
/// </summary>
public static class RpcJsonOptions
{
    private static readonly JsonSerializerOptions s_options = Create();

    /// <summary>
    /// Gets the JSON serializer options configured for RPC
    /// </summary>
    public static JsonSerializerOptions Options => s_options;

    private static JsonSerializerOptions Create()
    {
        var options = new JsonSerializerOptions();
        options.Converters.Add(new FilterConverter());
        options.Converters.Add(new SecretProviderConverter());
        options.Converters.Add(new ResultsDtoConverterFactory());
        return options;
    }
}

/// <summary>
/// JSON converter for IFilter
/// </summary>
public class FilterConverter : JsonConverter<IFilter>
{
    public override IFilter? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var dto = JsonSerializer.Deserialize<FilterDto>(ref reader, options);
        return dto?.ToFilter();
    }

    public override void Write(Utf8JsonWriter writer, IFilter value, JsonSerializerOptions options)
    {
        var dto = FilterDto.FromFilter(value);
        if (dto == null)
            writer.WriteNullValue();
        else
            JsonSerializer.Serialize(writer, dto, options);
    }
}

/// <summary>
/// JSON converter for ISecretProvider
/// </summary>
public class SecretProviderConverter : JsonConverter<ISecretProvider>
{
    public override ISecretProvider? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var dto = JsonSerializer.Deserialize<SecretProviderDto>(ref reader, options);
        return dto?.ToProvider();
    }

    public override void Write(Utf8JsonWriter writer, ISecretProvider value, JsonSerializerOptions options)
    {
        var dto = SecretProviderDto.FromProvider(value);
        if (dto == null)
            writer.WriteNullValue();
        else
            JsonSerializer.Serialize(writer, dto, options);
    }
}

/// <summary>
/// Factory for creating JSON converters that map result interfaces to DTOs
/// </summary>
public class ResultsDtoConverterFactory : JsonConverterFactory
{
    private static readonly Dictionary<Type, (Type DtoType, Type WrapperType)> s_map = new()
    {
        [typeof(IBackupResults)] = (typeof(BackupResultsDto), typeof(BackupResultsWrapper)),
        [typeof(IRestoreResults)] = (typeof(RestoreResultsDto), typeof(RestoreResultsWrapper)),
        [typeof(IListResults)] = (typeof(ListResultsDto), typeof(ListResultsWrapper)),
        [typeof(IDeleteResults)] = (typeof(DeleteResultsDto), typeof(DeleteResultsWrapper)),
        [typeof(IRepairResults)] = (typeof(RepairResultsDto), typeof(RepairResultsWrapper)),
        [typeof(ICompactResults)] = (typeof(CompactResultsDto), typeof(CompactResultsWrapper)),
        [typeof(IVacuumResults)] = (typeof(VacuumResultsDto), typeof(VacuumResultsWrapper)),
        [typeof(ITestResults)] = (typeof(TestResultsDto), typeof(TestResultsWrapper)),
        [typeof(IListFilesetResults)] = (typeof(ListFilesetResultsDto), typeof(ListFilesetResultsWrapper)),
        [typeof(IListFolderResults)] = (typeof(ListFolderResultsDto), typeof(ListFolderResultsWrapper)),
        [typeof(IListFileVersionsResults)] = (typeof(ListFileVersionsResultsDto), typeof(ListFileVersionsResultsWrapper)),
        [typeof(ISearchFilesResults)] = (typeof(SearchFilesResultsDto), typeof(SearchFilesResultsWrapper)),
        [typeof(ICreateLogDatabaseResults)] = (typeof(CreateLogDatabaseResultsDto), typeof(CreateLogDatabaseResultsWrapper)),
        [typeof(IListRemoteResults)] = (typeof(ListRemoteResultsDto), typeof(ListRemoteResultsWrapper)),
        [typeof(ISetLockResults)] = (typeof(SetLockResultsDto), typeof(SetLockResultsWrapper)),
        [typeof(IReadLockInfoResults)] = (typeof(ReadLockInfoResultsDto), typeof(ReadLockInfoResultsWrapper)),
        [typeof(IRecreateDatabaseResults)] = (typeof(RecreateDatabaseResultsDto), typeof(RecreateDatabaseResultsWrapper)),
        [typeof(IPurgeFilesResults)] = (typeof(PurgeFilesResultsDto), typeof(PurgeFilesResultsWrapper)),
        [typeof(IListBrokenFilesResults)] = (typeof(ListBrokenFilesResultsDto), typeof(ListBrokenFilesResultsWrapper)),
        [typeof(IPurgeBrokenFilesResults)] = (typeof(PurgeBrokenFilesResultsDto), typeof(PurgeBrokenFilesResultsWrapper)),
        [typeof(ISendMailResults)] = (typeof(SendMailResultsDto), typeof(SendMailResultsWrapper)),
        [typeof(ISystemInfoResults)] = (typeof(SystemInfoResultsDto), typeof(SystemInfoResultsWrapper)),
        [typeof(ITestFilterResults)] = (typeof(TestFilterResultsDto), typeof(TestFilterResultsWrapper)),
        [typeof(IListChangesResults)] = (typeof(ListChangesResultsDto), typeof(ListChangesResultsWrapper)),
        [typeof(IListAffectedResults)] = (typeof(ListAffectedResultsDto), typeof(ListAffectedResultsWrapper)),
        [typeof(IRestoreControlFilesResults)] = (typeof(RestoreControlFilesResultsDto), typeof(RestoreControlFilesResultsWrapper)),
    };

    public override bool CanConvert(Type typeToConvert) => s_map.ContainsKey(typeToConvert);

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        var (dtoType, wrapperType) = s_map[typeToConvert];
        var converterType = typeof(ResultsDtoConverter<,,>).MakeGenericType(typeToConvert, dtoType, wrapperType);
        return (JsonConverter)Activator.CreateInstance(converterType)!;
    }
}

/// <summary>
/// JSON converter that serializes result interfaces via their DTO representation
/// and deserializes into wrapper types.
/// </summary>
internal class ResultsDtoConverter<TInterface, TDto, TWrapper> : JsonConverter<TInterface>
    where TWrapper : TInterface
{
    private static readonly MethodInfo s_fromResultsMethod = typeof(TDto).GetMethod("FromResults", new[] { typeof(TInterface) })
        ?? throw new InvalidOperationException($"No FromResults({typeof(TInterface).Name}) method found on {typeof(TDto).Name}");
    private static readonly ConstructorInfo s_wrapperConstructor = typeof(TWrapper).GetConstructor(new[] { typeof(TDto) })
        ?? throw new InvalidOperationException($"No constructor found on {typeof(TWrapper).Name} taking {typeof(TDto).Name}");

    public override TInterface Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var dto = JsonSerializer.Deserialize<TDto>(ref reader, options);
        if (dto == null) return default!;
        return (TInterface)s_wrapperConstructor.Invoke(new object?[] { dto });
    }

    public override void Write(Utf8JsonWriter writer, TInterface value, JsonSerializerOptions options)
    {
        if (value == null)
        {
            writer.WriteNullValue();
            return;
        }
        var dto = s_fromResultsMethod.Invoke(null, new object?[] { value });
        JsonSerializer.Serialize(writer, dto, typeof(TDto), options);
    }
}
