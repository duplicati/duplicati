// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Duplicati.Proprietary.Office365.SourceItems;

namespace Duplicati.Proprietary.Office365;

partial class RestoreProvider
{
    internal PlannerApiImpl PlannerApi => new PlannerApiImpl(_apiHelper);

    internal class PlannerApiImpl(APIHelper provider)
    {
        public async Task<GraphPlannerBucket> CreatePlannerBucketAsync(
            string planId,
            string name,
            string? orderHint,
            CancellationToken ct)
        {
            var baseUrl = provider.GraphBaseUrl.TrimEnd('/');
            var url = $"{baseUrl}/v1.0/planner/buckets";

            var body = new
            {
                name,
                planId,
                orderHint
            };

            var options = new JsonSerializerOptions
            {
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            };

            async Task<HttpRequestMessage> requestFactory(CancellationToken rct)
            {
                var req = new HttpRequestMessage(HttpMethod.Post, new Uri(url));
                req.Headers.Authorization = await provider.GetAuthenticationHeaderAsync(false, rct).ConfigureAwait(false);
                req.Content = JsonContent.Create(body, options: options);
                return req;
            }

            using var resp = await provider.SendWithRetryShortAsync(
                requestFactory,
                ct).ConfigureAwait(false);

            await APIHelper.EnsureOfficeApiSuccessAsync(resp, ct).ConfigureAwait(false);

            await using var respStream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            var created = await JsonSerializer.DeserializeAsync<GraphPlannerBucket>(respStream, cancellationToken: ct).ConfigureAwait(false);

            if (created is null || string.IsNullOrWhiteSpace(created.Id))
                throw new InvalidOperationException("Graph did not return the created bucket id.");

            return created;
        }

        static readonly IReadOnlySet<string> AllowedPlannerTaskPatchValues =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "description",
                "previewType",
                "references",
                "checklist"
            };

        public async Task<GraphPlannerTask> CreatePlannerTaskAsync(
            string planId,
            string? bucketId,
            string title,
            DateTimeOffset? startDateTime,
            DateTimeOffset? dueDateTime,
            int? percentComplete,
            int? priority,
            JsonElement? assignments,
            JsonElement? appliedCategories,
            CancellationToken ct)
        {
            var baseUrl = provider.GraphBaseUrl.TrimEnd('/');
            var url = $"{baseUrl}/v1.0/planner/tasks";

            var body = new
            {
                planId,
                bucketId,
                title,
                startDateTime,
                dueDateTime,
                percentComplete,
                priority,
                assignments,
                appliedCategories
            };

            async Task<HttpRequestMessage> requestFactory(CancellationToken rct)
            {
                var req = new HttpRequestMessage(HttpMethod.Post, new Uri(url));
                req.Headers.Authorization = await provider.GetAuthenticationHeaderAsync(false, rct).ConfigureAwait(false);
                req.Content = JsonContent.Create(body, options: new JsonSerializerOptions { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull });
                return req;
            }

            using var resp = await provider.SendWithRetryShortAsync(
                requestFactory,
                ct).ConfigureAwait(false);

            await APIHelper.EnsureOfficeApiSuccessAsync(resp, ct).ConfigureAwait(false);

            await using var respStream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            var created = await JsonSerializer.DeserializeAsync<GraphPlannerTask>(respStream, cancellationToken: ct).ConfigureAwait(false);

            if (created is null || string.IsNullOrWhiteSpace(created.Id))
                throw new InvalidOperationException("Graph did not return the created task id.");

            return created;
        }

        private static void WriteSanitizedPlannerChecklist(Utf8JsonWriter jw, JsonElement checklist)
        {
            // checklist is an object: { "<key>": { ...plannerChecklistItem... }, ... }
            jw.WriteStartObject();

            foreach (var entry in checklist.EnumerateObject())
            {
                if (entry.Value.ValueKind != JsonValueKind.Object)
                    continue;

                jw.WritePropertyName(entry.Name);
                jw.WriteStartObject();

                jw.WriteString("@odata.type", "#microsoft.graph.plannerChecklistItem");

                // title
                if (entry.Value.TryGetProperty("title", out var title) && title.ValueKind != JsonValueKind.Null)
                {
                    jw.WritePropertyName("title");
                    title.WriteTo(jw);
                }

                // isChecked
                if (entry.Value.TryGetProperty("isChecked", out var isChecked) &&
                    (isChecked.ValueKind == JsonValueKind.True || isChecked.ValueKind == JsonValueKind.False))
                {
                    jw.WriteBoolean("isChecked", isChecked.GetBoolean());
                }

                // Intentionally NOT writing:
                // @odata.type, orderHint, lastModifiedDateTime, lastModifiedBy, etc.

                jw.WriteEndObject();
            }

            jw.WriteEndObject();
        }

        private static void WriteSanitizedPlannerReferences(Utf8JsonWriter jw, JsonElement references)
        {
            // references is an object: { "<url>": { ...plannerExternalReference... }, ... }
            jw.WriteStartObject();

            foreach (var entry in references.EnumerateObject())
            {
                if (entry.Value.ValueKind != JsonValueKind.Object)
                    continue;

                jw.WritePropertyName(entry.Name); // key is the URL
                jw.WriteStartObject();

                // alias (string)
                if (entry.Value.TryGetProperty("alias", out var alias) && alias.ValueKind != JsonValueKind.Null)
                {
                    jw.WritePropertyName("alias");
                    alias.WriteTo(jw);
                }

                // type (string)
                if (entry.Value.TryGetProperty("type", out var type) && type.ValueKind != JsonValueKind.Null)
                {
                    jw.WritePropertyName("type");
                    type.WriteTo(jw);
                }

                // previewPriority (string) - optional; keep only if non-null
                if (entry.Value.TryGetProperty("previewPriority", out var previewPriority) &&
                    previewPriority.ValueKind != JsonValueKind.Null)
                {
                    jw.WritePropertyName("previewPriority");
                    previewPriority.WriteTo(jw);
                }

                // Intentionally omit:
                // @odata.type, lastModifiedDateTime, lastModifiedBy

                jw.WriteEndObject();
            }

            jw.WriteEndObject();
        }

        private static void WriteSanitizedPlannerAssignments(Utf8JsonWriter jw, JsonElement assignments)
        {
            // assignments is an object: { "<userId>": { ...plannerAssignment... }, ... }
            // For restore, the minimal valid shape is: { "<userId>": {} }
            jw.WriteStartObject();

            foreach (var entry in assignments.EnumerateObject())
            {
                // Key must be the userId (GUID). Value can be {} for assignment creation.
                jw.WritePropertyName(entry.Name);
                jw.WriteStartObject();

                // Intentionally omit:
                // @odata.type, assignedDateTime, assignedBy, orderHint

                jw.WriteEndObject();
            }

            jw.WriteEndObject();
        }

        public async Task UpdatePlannerTaskDetailsAsync(
            string taskId,
            Stream detailsStream,
            CancellationToken ct)
        {
            var baseUrl = provider.GraphBaseUrl.TrimEnd('/');
            var url = $"{baseUrl}/v1.0/planner/tasks/{Uri.EscapeDataString(taskId)}/details";

            // 1) Get ETag
            async Task<HttpRequestMessage> getRequestFactory(CancellationToken rct)
            {
                var req = new HttpRequestMessage(HttpMethod.Get, new Uri(url));
                req.Headers.Authorization = await provider.GetAuthenticationHeaderAsync(false, rct).ConfigureAwait(false);
                return req;
            }

            string etag;
            using (var getResp = await provider.SendWithRetryShortAsync(getRequestFactory, ct).ConfigureAwait(false))
            {
                await APIHelper.EnsureOfficeApiSuccessAsync(getResp, ct).ConfigureAwait(false);
                etag = getResp.Headers.ETag?.Tag
                       ?? throw new InvalidOperationException("ETag missing from planner task details response.");
            }

            // 2) Parse backup JSON
            if (detailsStream.CanSeek)
                detailsStream.Position = 0;

            using var doc = await JsonDocument.ParseAsync(detailsStream, cancellationToken: ct).ConfigureAwait(false);
            var root = doc.RootElement;

            // 3) Build sanitized PATCH body
            using var ms = new MemoryStream();
            using (var jw = new Utf8JsonWriter(ms))
            {
                jw.WriteStartObject();

                if (root.TryGetProperty("description", out var description) &&
                    description.ValueKind != JsonValueKind.Null)
                {
                    jw.WritePropertyName("description");
                    description.WriteTo(jw);
                }

                if (root.TryGetProperty("previewType", out var previewType) &&
                    previewType.ValueKind != JsonValueKind.Null)
                {
                    jw.WritePropertyName("previewType");
                    previewType.WriteTo(jw);
                }

                if (root.TryGetProperty("references", out var references) &&
                    references.ValueKind == JsonValueKind.Object)
                {
                    jw.WritePropertyName("references");
                    WriteSanitizedPlannerReferences(jw, references);
                }

                if (root.TryGetProperty("checklist", out var checklist) &&
                    checklist.ValueKind == JsonValueKind.Object)
                {
                    jw.WritePropertyName("checklist");
                    WriteSanitizedPlannerChecklist(jw, checklist);
                }

                if (root.TryGetProperty("assignments", out var assignments) &&
                    assignments.ValueKind == JsonValueKind.Object)
                {
                    jw.WritePropertyName("assignments");
                    WriteSanitizedPlannerAssignments(jw, assignments);
                }

                jw.WriteEndObject();
                jw.Flush();
            }

            if (ms.Length <= 2) // "{}")
                return;

            async Task<HttpRequestMessage> patchRequestFactory(CancellationToken rct)
            {
                var req = new HttpRequestMessage(HttpMethod.Patch, new Uri(url));
                req.Headers.Authorization = await provider.GetAuthenticationHeaderAsync(false, rct).ConfigureAwait(false);
                req.Headers.IfMatch.Add(new EntityTagHeaderValue(etag));
                ms.Position = 0;
                req.Content = new StreamContent(ms);
                req.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
                return req;
            }

            using var patchResp = await provider.SendWithRetryShortAsync(patchRequestFactory, ct).ConfigureAwait(false);
            await APIHelper.EnsureOfficeApiSuccessAsync(patchResp, ct).ConfigureAwait(false);
        }

        public async Task<List<GraphPlannerPlan>> GetGroupPlansAsync(string groupId, CancellationToken ct)
        {
            var baseUrl = provider.GraphBaseUrl.TrimEnd('/');
            var group = Uri.EscapeDataString(groupId);
            var url = $"{baseUrl}/v1.0/groups/{group}/planner/plans";

            async Task<HttpRequestMessage> requestFactory(CancellationToken rct)
            {
                var req = new HttpRequestMessage(HttpMethod.Get, new Uri(url));
                req.Headers.Authorization = await provider.GetAuthenticationHeaderAsync(false, rct).ConfigureAwait(false);
                return req;
            }

            using var resp = await provider.SendWithRetryShortAsync(requestFactory, ct).ConfigureAwait(false);
            await APIHelper.EnsureOfficeApiSuccessAsync(resp, ct).ConfigureAwait(false);

            using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            var page = await JsonSerializer.DeserializeAsync<GraphPage<GraphPlannerPlan>>(stream, cancellationToken: ct).ConfigureAwait(false);
            return page?.Value ?? [];
        }

        public async Task<GraphPlannerPlan> CreatePlanAsync(string groupId, string title, CancellationToken ct)
        {
            var baseUrl = provider.GraphBaseUrl.TrimEnd('/');
            var url = $"{baseUrl}/v1.0/planner/plans";

            var body = new
            {
                owner = groupId,
                title
            };

            async Task<HttpRequestMessage> requestFactory(CancellationToken rct)
            {
                var req = new HttpRequestMessage(HttpMethod.Post, new Uri(url));
                req.Headers.Authorization = await provider.GetAuthenticationHeaderAsync(false, rct).ConfigureAwait(false);
                req.Content = JsonContent.Create(body);
                return req;
            }

            using var resp = await provider.SendWithRetryShortAsync(requestFactory, ct).ConfigureAwait(false);
            await APIHelper.EnsureOfficeApiSuccessAsync(resp, ct).ConfigureAwait(false);

            using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            var created = await JsonSerializer.DeserializeAsync<GraphPlannerPlan>(stream, cancellationToken: ct).ConfigureAwait(false);

            if (created is null || string.IsNullOrWhiteSpace(created.Id))
                throw new InvalidOperationException("Graph did not return the created plan id.");

            return created;
        }

        public async Task<List<GraphPlannerBucket>> GetPlannerBucketsAsync(string planId, CancellationToken ct)
        {
            var baseUrl = provider.GraphBaseUrl.TrimEnd('/');
            var plan = Uri.EscapeDataString(planId);
            var url = $"{baseUrl}/v1.0/planner/plans/{plan}/buckets";

            async Task<HttpRequestMessage> requestFactory(CancellationToken rct)
            {
                var req = new HttpRequestMessage(HttpMethod.Get, new Uri(url));
                req.Headers.Authorization = await provider.GetAuthenticationHeaderAsync(false, rct).ConfigureAwait(false);
                return req;
            }

            using var resp = await provider.SendWithRetryShortAsync(requestFactory, ct).ConfigureAwait(false);
            await APIHelper.EnsureOfficeApiSuccessAsync(resp, ct).ConfigureAwait(false);

            using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            var page = await JsonSerializer.DeserializeAsync<GraphPage<GraphPlannerBucket>>(stream, cancellationToken: ct).ConfigureAwait(false);
            return page?.Value ?? [];
        }

        public async Task<List<GraphPlannerTask>> GetPlannerTasksAsync(
            string planId,
            CancellationToken ct)
        {
            var baseUrl = provider.GraphBaseUrl.TrimEnd('/');
            var plan = Uri.EscapeDataString(planId);

            var url = $"{baseUrl}/v1.0/planner/plans/{plan}/tasks";

            async Task<HttpRequestMessage> requestFactory(CancellationToken rct)
            {
                var req = new HttpRequestMessage(HttpMethod.Get, new Uri(url));
                req.Headers.Authorization = await provider.GetAuthenticationHeaderAsync(false, rct).ConfigureAwait(false);
                return req;
            }

            using var resp = await provider.SendWithRetryShortAsync(
                requestFactory,
                ct).ConfigureAwait(false);

            await APIHelper.EnsureOfficeApiSuccessAsync(resp, ct).ConfigureAwait(false);

            using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            var page = await JsonSerializer.DeserializeAsync<GraphPage<GraphPlannerTask>>(stream, cancellationToken: ct).ConfigureAwait(false);
            return page?.Value ?? [];
        }
    }

    private PlannerRestoreHelper? _plannerRestoreHelper = null;
    internal PlannerRestoreHelper PlannerRestore => _plannerRestoreHelper ??= new PlannerRestoreHelper(this);

    internal class PlannerRestoreHelper(RestoreProvider Provider)
    {
        private string? _targetPlanId = null;
        private bool _hasLoadedPlanId = false;

        public async Task<string?> GetTargetPlanId(CancellationToken cancel)
        {
            if (_hasLoadedPlanId)
                return _targetPlanId;

            _hasLoadedPlanId = true;
            var target = Provider.RestoreTarget;

            if (target == null)
                return null;

            if (target.Type == SourceItemType.Planner)
            {
                _targetPlanId = target.Metadata.GetValueOrDefault("o365:Id");
            }
            else if (target.Type == SourceItemType.Group)
            {
                var groupId = target.Metadata.GetValueOrDefault("o365:Id");
                if (!string.IsNullOrWhiteSpace(groupId))
                {
                    // Check if "Restored" plan exists
                    var plans = await Provider.PlannerApi.GetGroupPlansAsync(groupId, cancel);
                    var restoredPlan = plans.FirstOrDefault(p => p.Title == "Restored");

                    if (restoredPlan != null)
                    {
                        _targetPlanId = restoredPlan.Id;
                    }
                    else
                    {
                        // Create "Restored" plan
                        var newPlan = await Provider.PlannerApi.CreatePlanAsync(groupId, "Restored", cancel);
                        _targetPlanId = newPlan.Id;
                    }
                }
            }

            return _targetPlanId;
        }

        private string? _targetBucketId = null;
        private bool _hasLoadedBucketId = false;

        public async Task<string?> GetTargetBucketId(CancellationToken cancel)
        {
            if (_hasLoadedBucketId)
                return _targetBucketId;

            _hasLoadedBucketId = true;
            var planId = await GetTargetPlanId(cancel);
            if (string.IsNullOrWhiteSpace(planId))
                return null;

            // Check if "Restored" bucket exists
            var buckets = await Provider.PlannerApi.GetPlannerBucketsAsync(planId, cancel);
            var restoredBucket = buckets.FirstOrDefault(b => b.Name == "Restored");

            if (restoredBucket != null)
            {
                _targetBucketId = restoredBucket.Id;
            }
            else
            {
                // Create "Restored" bucket
                var newBucket = await Provider.PlannerApi.CreatePlannerBucketAsync(planId, "Restored", null, cancel);
                _targetBucketId = newBucket.Id;
            }

            return _targetBucketId;
        }
    }
}

internal sealed class GraphPlannerTaskDetails
{
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("previewType")]
    public string? PreviewType { get; set; }

    [JsonPropertyName("references")]
    public object? References { get; set; }

    [JsonPropertyName("checklist")]
    public object? Checklist { get; set; }
}
