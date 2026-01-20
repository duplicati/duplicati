// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

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

            async Task<HttpRequestMessage> requestFactory(CancellationToken rct)
            {
                var req = new HttpRequestMessage(HttpMethod.Post, new Uri(url));
                req.Headers.Authorization = await provider.GetAuthenticationHeaderAsync(false, rct).ConfigureAwait(false);
                req.Content = JsonContent.Create(body);
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

        public async Task UpdatePlannerTaskDetailsAsync(
            string taskId,
            Stream detailsStream,
            CancellationToken ct)
        {
            var baseUrl = provider.GraphBaseUrl.TrimEnd('/');
            var url = $"{baseUrl}/v1.0/planner/tasks/{taskId}/details";

            // 1. Get current details to get ETag
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
                etag = getResp.Headers.ETag?.Tag ?? throw new InvalidOperationException("ETag missing from planner task details response");
            }

            // 2. Patch details
            // We need to deserialize the stream to get the properties to update
            // The stream contains the full details object from the backup
            if (detailsStream.CanSeek) detailsStream.Position = 0;
            var details = await JsonSerializer.DeserializeAsync<GraphPlannerTaskDetails>(detailsStream, cancellationToken: ct).ConfigureAwait(false);

            if (details == null) return;

            var patchBody = new
            {
                description = details.Description,
                previewType = details.PreviewType,
                references = details.References,
                checklist = details.Checklist
            };

            async Task<HttpRequestMessage> patchRequestFactory(CancellationToken rct)
            {
                var req = new HttpRequestMessage(HttpMethod.Patch, new Uri(url));
                req.Headers.Authorization = await provider.GetAuthenticationHeaderAsync(false, rct).ConfigureAwait(false);
                req.Headers.IfMatch.Add(new EntityTagHeaderValue(etag));
                req.Content = JsonContent.Create(patchBody, options: new JsonSerializerOptions { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull });
                req.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json"); // Graph requires this
                return req;
            }

            using var patchResp = await provider.SendWithRetryShortAsync(patchRequestFactory, ct).ConfigureAwait(false);
            await APIHelper.EnsureOfficeApiSuccessAsync(patchResp, ct).ConfigureAwait(false);
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
