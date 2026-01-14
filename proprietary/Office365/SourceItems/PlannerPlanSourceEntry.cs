// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using System.Runtime.CompilerServices;
using System.Text.Json;
using Duplicati.Library.Common.IO;
using Duplicati.Library.Interface;

namespace Duplicati.Proprietary.Office365.SourceItems;

internal class PlannerPlanSourceEntry(SourceProvider provider, string parentPath, GraphPlannerPlan plan)
    : MetaEntryBase(Util.AppendDirSeparator(SystemIO.IO_OS.PathCombine(parentPath, plan.Id)), plan.CreatedDateTime.FromGraphDateTime(), plan.CreatedDateTime.FromGraphDateTime())
{
    public override async IAsyncEnumerable<ISourceProviderEntry> Enumerate([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        yield return new PlannerPlanBucketSourceEntry(provider, this.Path, plan);
        yield return new PlannerPlanTasksSourceEntry(provider, this.Path, plan);
    }

    public override Task<Dictionary<string, string?>> GetMinorMetadata(CancellationToken cancellationToken)
        => Task.FromResult(new Dictionary<string, string?>()
            {
                { "o365:v", "1" },
                { "o365:Id", plan.Id },
                { "o365:Type", SourceItemType.Planner.ToString() },
                { "o365:Name", plan.Title ?? "" },
                { "o365:Title", plan.Title ?? "" },
                { "o365:CreatedBy", JsonSerializer.Serialize(plan.CreatedBy) ?? "" },
                { "o365:Container", JsonSerializer.Serialize(plan.Container) ?? "" },
                { "o365:Owner", plan.Owner ?? "" },
            }
            .Where(kv => !string.IsNullOrEmpty(kv.Value))
            .ToDictionary(kv => kv.Key, kv => kv.Value));

}
