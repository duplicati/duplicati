
using System.Runtime.CompilerServices;
using System.Text.Json;
using Duplicati.Library.Common.IO;
using Duplicati.Library.Interface;

namespace Duplicati.Proprietary.Office365.SourceItems;

internal class PlannerTaskSourceEntry(SourceProvider provider, string path, GraphPlannerTask task)
    : MetaEntryBase(Util.AppendDirSeparator(SystemIO.IO_OS.PathCombine(path, task.Id)), task.CreatedDateTime.FromGraphDateTime(), null)
{
    public override async IAsyncEnumerable<ISourceProviderEntry> Enumerate([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        yield return new StreamResourceEntryFunction(
            SystemIO.IO_OS.PathCombine(this.Path, "task.json"),
            createdUtc: task.CreatedDateTime.FromGraphDateTime(),
            lastModificationUtc: task.CreatedDateTime.FromGraphDateTime(),
            size: -1,
            streamFactory: async (ct) =>
            {
                var stream = new MemoryStream();
                await JsonSerializer.SerializeAsync(stream, task, cancellationToken: ct);
                stream.Position = 0;
                return stream;
            });

        yield return new StreamResourceEntryFunction(
            SystemIO.IO_OS.PathCombine(this.Path, "content.json"),
            createdUtc: task.CreatedDateTime.FromGraphDateTime(),
            lastModificationUtc: task.CreatedDateTime.FromGraphDateTime(),
            size: -1,
            streamFactory: (ct) => provider.PlannerApi.GetPlannerTaskDetailsStreamAsync(task.Id, ct));

        yield return new StreamResourceEntryFunction(
            SystemIO.IO_OS.PathCombine(this.Path, "assignedToTaskBoardFormat.json"),
            createdUtc: task.CreatedDateTime.FromGraphDateTime(),
            lastModificationUtc: task.CreatedDateTime.FromGraphDateTime(),
            size: -1,
            streamFactory: (ct) => provider.PlannerApi.GetPlannerAssignedToTaskBoardFormatStreamAsync(task.Id, ct));

        yield return new StreamResourceEntryFunction(
            SystemIO.IO_OS.PathCombine(this.Path, "bucketTaskBoardFormat.json"),
            createdUtc: task.CreatedDateTime.FromGraphDateTime(),
            lastModificationUtc: task.CreatedDateTime.FromGraphDateTime(),
            size: -1,
            streamFactory: (ct) => provider.PlannerApi.GetPlannerBucketTaskBoardFormatStreamAsync(task.Id, ct));

        yield return new StreamResourceEntryFunction(
            SystemIO.IO_OS.PathCombine(this.Path, "progressTaskBoardFormat.json"),
            createdUtc: task.CreatedDateTime.FromGraphDateTime(),
            lastModificationUtc: task.CreatedDateTime.FromGraphDateTime(),
            size: -1,
            streamFactory: (ct) => provider.PlannerApi.GetPlannerProgressTaskBoardFormatStreamAsync(task.Id, ct));
    }

    public override Task<Dictionary<string, string?>> GetMinorMetadata(CancellationToken cancellationToken)
        => Task.FromResult(new Dictionary<string, string?>()
            {
                { "o365:v", "1" },
                { "o365:Id", task.Id },
                { "o365:Type", SourceItemType.UserPlannerTasks.ToString() },
                { "o365:Name", task.Title },
                { "o365:Title", task.Title },
                { "o365:BucketId", task.BucketId },
                { "o365:PercentComplete", task.PercentComplete?.ToString() },
            });


}
