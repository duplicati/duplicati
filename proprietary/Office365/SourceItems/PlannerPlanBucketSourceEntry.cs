using System.Runtime.CompilerServices;
using System.Text.Json;
using Duplicati.Library.Common.IO;
using Duplicati.Library.Interface;

namespace Duplicati.Proprietary.Office365.SourceItems;

internal class PlannerPlanBucketSourceEntry(SourceProvider provider, string path, GraphPlannerPlan plan)
    : MetaEntryBase(Util.AppendDirSeparator(SystemIO.IO_OS.PathCombine(path, "buckets")), null, null)
{
    public override async IAsyncEnumerable<ISourceProviderEntry> Enumerate([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var bucket in provider.PlannerApi.ListPlannerBucketsAsync(plan.Id, cancellationToken).ConfigureAwait(false))
        {
            if (cancellationToken.IsCancellationRequested)
                yield break;

            yield return new StreamResourceEntryFunction(
                SystemIO.IO_OS.PathCombine(this.Path, bucket.Id + ".json"),
                createdUtc: DateTime.UnixEpoch,
                lastModificationUtc: DateTime.UnixEpoch,
                size: -1,
                streamFactory: (ct) =>
                {
                    var stream = new MemoryStream();
                    JsonSerializer.Serialize(stream, bucket);
                    stream.Position = 0;
                    return Task.FromResult<Stream>(stream);
                },
                minorMetadataFactory: (ct) => Task.FromResult(new Dictionary<string, string?>()
                {
                    { "o365:v", "1" },
                    { "o365:Id", bucket.Id },
                    { "o365:Type", SourceItemType.PlannerBucket.ToString() },
                    { "o365:Name", bucket.Name },
                    { "o365:OrderHint", bucket.OrderHint }
                })
            );
        }
    }
}
