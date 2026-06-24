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
using System.Runtime.Versioning;
using System.Text;
using Duplicati.Library.Interface;
using Duplicati.Library.Logging;
using secrets.DBus;
using Tmds.DBus.Protocol;

namespace Duplicati.Library.SecretProvider.LibSecret;

/// <summary>
/// Implementation of the secret collection for libsecret
/// </summary>
[SupportedOSPlatform("Linux")]
public class SecretCollection : IDisposable
{
    /// <summary>
    /// The fallback collection name to use when "default" is requested but doesn't exist.
    /// The "login" collection is automatically unlocked when the user logs in.
    /// </summary>
    public const string DefaultCollectionActualName = "login";
    /// <summary>
    /// The attribute key used to mark items with a schema name.
    /// </summary>
    private const string SchemaAttribute = "xdg:schema";
    /// <summary>
    /// The schema name applied to created items.
    /// </summary>
    private const string AppliedSchema = "com.duplicati.Secret";
    /// <summary>
    /// The value used for the "display" attribute.
    /// </summary>
    private const string DuplicatiDisplayAttribute = "Duplicati Secrets";
    /// <summary>
    /// The log tag for the secret collection
    /// </summary>
    private static readonly string LogTag = Log.LogTagFromType<SecretCollection>();
    /// <summary>
    /// The secrets service
    /// </summary>
    private readonly secretsService _secretsService;
    /// <summary>
    /// The service instance
    /// </summary>
    private readonly Service _service;
    /// <summary>
    /// The session instance
    /// </summary>
    private readonly Session _session;
    /// <summary>
    /// The collection instance
    /// </summary>
    private readonly Collection _collection;
    /// <summary>
    /// Whether the collection is locked
    /// </summary>
    private bool _locked;
    /// <summary>
    /// Creates a new secret collection
    /// </summary>
    /// <param name="secretsService">The secrets service</param>
    /// <param name="service">The service instance</param>
    /// <param name="session">The session instance</param>
    /// <param name="collection">The collection instance</param>
    /// <param name="locked">Whether the collection is locked</param>
    private SecretCollection(secretsService secretsService, Service service, Session session, Collection collection, bool locked)
    {
        _secretsService = secretsService;
        _service = service;
        _session = session;
        _collection = collection;
        _locked = locked;
    }

    /// <summary>
    /// Creates a new secret collection and optionally auto-creates it if it does not exist.
    /// </summary>
    /// <param name="collectionName">The collection name</param>
    /// <param name="autoCreateCollection">If set, the collection will be created when it does not exist</param>
    /// <param name="serviceName">The D-Bus service name implementing the freedesktop Secret Service API</param>
    /// <param name="cancellationToken">The cancellation token</param>
    /// <returns>The created secret collection</returns>
    public static async Task<SecretCollection> CreateAsync(string collectionName, bool autoCreateCollection, string serviceName, CancellationToken cancellationToken)
    {
        var connection = DBusConnection.Session;
        var secretsService = new secretsService(connection, serviceName);
        var service = secretsService.CreateService("/org/freedesktop/secrets");
        var (_, sessionPath) = await service.OpenSessionAsync("plain", "").ConfigureAwait(false);
        collectionName ??= string.Empty;

        // An empty collection name means "use the canonical default collection".
        var collectionPath = await GetCollectionPath(collectionName, serviceName, cancellationToken).ConfigureAwait(false);

        if (collectionPath == null)
        {
            // When creating for the default case, use the "login" collection label.
            var useDefault = string.IsNullOrWhiteSpace(collectionName);
            var createLabel = useDefault ? DefaultCollectionActualName : collectionName;

            if (!autoCreateCollection)
                throw new UserInformationException($"Collection {createLabel} not found", "CollectionNotFound");

            // Auto-create the collection
            var properties = new Dictionary<string, VariantValue>
            {
                ["org.freedesktop.Secret.Collection.Label"] = VariantValue.String(createLabel)
            };

            var (createdCollectionPath, promptPath) = await service.CreateCollectionAsync(properties, string.Empty).ConfigureAwait(false);

            if (promptPath != null && promptPath != "/")
            {
                var promptInstance = secretsService.CreatePrompt(promptPath);
                var completedTask = new TaskCompletionSource<string>();

                // Cancel the wait if the caller cancels
                using var cancellationRegistration = cancellationToken.Register(() =>
                {
                    completedTask.TrySetCanceled(cancellationToken);
                });

                // Subscribe to completion signal
                using var _ = await promptInstance.WatchCompletedAsync((exception, result) =>
                {
                    if (exception != null)
                        completedTask.TrySetException(exception);
                    else if (result.Dismissed)
                        completedTask.TrySetException(new UserInformationException("Dismissed collection create prompt", "CreateCollectionDismissed"));
                    else
                    {
                        // The result contains the path to the created collection
                        var resultPath = result.Result.GetObjectPathAsString();
                        completedTask.TrySetResult(resultPath);
                    }
                }).ConfigureAwait(false);

                // Ask the secrets service to show the prompt
                await promptInstance.PromptAsync(string.Empty).ConfigureAwait(false);

                // Wait for either completion or a timeout
                var timeoutTask = Task.Delay(TimeSpan.FromSeconds(120), cancellationToken);
                var finishedTask = await Task.WhenAny(completedTask.Task, timeoutTask).ConfigureAwait(false);

                if (finishedTask != completedTask.Task)
                    throw new UserInformationException("Timed out waiting for libsecret collection create prompt. Ensure that a secret service/keyring is running and able to show prompts.", "CreateCollectionPromptTimeout");

                var resultPathString = await completedTask.Task.ConfigureAwait(false);
                collectionPath = new ObjectPath(resultPathString);
            }
            else
            {
                collectionPath = createdCollectionPath;
            }
        }

        var collectionPathString = collectionPath?.ToString();
        if (string.IsNullOrEmpty(collectionPathString) || collectionPathString == "/")
            throw new UserInformationException("The secret service returned an invalid collection path", "InvalidCollectionPath");

        var session = secretsService.CreateSession(sessionPath);
        var collection = secretsService.CreateCollection(collectionPathString);
        var locked = await collection.GetLockedAsync().ConfigureAwait(false);

        return new SecretCollection(secretsService, service, session, collection, locked);
    }

    /// <summary>
    /// Checks whether the secret service DBus service is available on the current platform.
    /// </summary>
    /// <param name="serviceName">The D-Bus service name implementing the freedesktop Secret Service API</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns><c>true</c> when the provider can be used; otherwise <c>false</c>.</returns>
    public static async Task<bool> IsSupported(string serviceName, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsLinux())
            return false;

        // Heuristic: secret service prompts require a graphical session to be useful.
        // If there is no X11/Wayland display, we treat the provider as unsupported.
        var hasDisplay =
            !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY")) ||
            !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY"));

        if (!hasDisplay)
            return false;

        try
        {
            var connection = DBusConnection.Session;
            var secretsService = new secretsService(connection, serviceName);
            var service = secretsService.CreateService("/org/freedesktop/secrets");
            var task = service.GetCollectionsAsync();

            var timeoutTask = Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
            var finishedTask = Task.WhenAny(task, timeoutTask);

            if (await finishedTask.ConfigureAwait(false) != task)
                return false;

            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Checks whether a secret service collection exists.
    /// This is a non-throwing, best-effort check used to determine if the provider
    /// should be considered supported for a given collection.
    /// </summary>
    /// <param name="collectionName">The collection name to check. When null or empty, the canonical default collection (resolved via the "default" alias, falling back to "login") is checked.</param>
    /// <param name="serviceName">The D-Bus service name implementing the freedesktop Secret Service API</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns><c>true</c> if the collection exists and the secret service is available; otherwise <c>false</c>.</returns>
    public static async Task<bool> CollectionExists(string collectionName, string serviceName, CancellationToken cancellationToken)
        => (await GetCollectionPath(collectionName, serviceName, cancellationToken)) is not null;

    /// <summary>
    /// Returns the collection path for a given collection name, or <c>null</c> if the collection does not exist.
    /// </summary>
    /// <param name="collectionName">The collection name to check. When null or empty, the canonical default collection (resolved via the "default" alias, falling back to "login") is checked.</param>
    /// <param name="serviceName">The D-Bus service name implementing the freedesktop Secret Service API</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The collection path, or <c>null</c> if the collection does not exist.</returns>
    public static async Task<ObjectPath?> GetCollectionPath(string collectionName, string serviceName, CancellationToken cancellationToken)
    {
        try
        {
            var connection = DBusConnection.Session;
            var secretsService = new secretsService(connection, serviceName);
            var service = secretsService.CreateService("/org/freedesktop/secrets");

            collectionName ??= string.Empty;

            // When no specific collection was requested, the canonical resolution is via
            // the Secret Service "default" alias, which may point to a collection whose
            // name is not literally "default".
            var useDefault = string.IsNullOrWhiteSpace(collectionName);
            if (useDefault)
            {
                var aliasTask = service.ReadAliasAsync("default");
                if (await Task.WhenAny(aliasTask, Task.Delay(TimeSpan.FromSeconds(5), cancellationToken)).ConfigureAwait(false) == aliasTask)
                {
                    var aliasPath = await aliasTask.ConfigureAwait(false);
                    if (aliasPath.ToString() is { Length: > 0 } ap && ap != "/")
                        return aliasPath;
                }
            }

            var task = service.GetCollectionsAsync();

            if (await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(5), cancellationToken)).ConfigureAwait(false) != task)
                return null;

            var collections = await task.ConfigureAwait(false);

            // For the default case (no alias set), fall back to checking the "login" collection.
            var matchName = useDefault ? DefaultCollectionActualName : collectionName;

            // Match on the final path segment exactly (e.g. ".../collection/login"), so that
            // a request for "login" does not also match a collection named "mylogin".
            return collections
                .FirstOrDefault(c => string.Equals(GetCollectionSegment(c), matchName, StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
            // Any failure in talking to the secrets service or enumerating collections
            // is treated as the collection not being available.
            return null;
        }
    }

    /// <summary>
    /// Gets the final path segment of a collection object path (e.g. "login" from
    /// "/org/freedesktop/secrets/collection/login").
    /// </summary>
    /// <param name="path">The collection object path.</param>
    /// <returns>The final path segment.</returns>
    private static string GetCollectionSegment(ObjectPath path)
        => path.ToString().Split('/', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? path.ToString();

    /// <summary>
    /// Unlocks the collection
    /// </summary>
    /// <returns>The task to await</returns>
    public async Task UnlockAsync()
    {
        if (!_locked)
            return;

        var (unlocked, prompt) = await _service.UnlockAsync([_collection.Path]);
        if (prompt != null && prompt != "/")
        {
            var promptInstance = _secretsService.CreatePrompt(prompt);
            var completedTask = new TaskCompletionSource<bool>();

            // Set up callback
            using var result = await promptInstance.WatchCompletedAsync((exception, result) =>
            {
                if (exception != null)
                    completedTask.TrySetException(exception);
                else if (result.Dismissed)
                    completedTask.TrySetResult(false);
                else
                    completedTask.TrySetResult(true);
            }).ConfigureAwait(false);

            // Prompt
            await promptInstance.PromptAsync(string.Empty).ConfigureAwait(false);

            // Wait for prompt to be dismissed or completed, with timeout safeguard
            var timeoutTask = Task.Delay(TimeSpan.FromSeconds(30));
            var finishedTask = await Task.WhenAny(completedTask.Task, timeoutTask).ConfigureAwait(false);

            if (finishedTask != completedTask.Task)
                throw new UserInformationException("Timed out waiting for libsecret unlock prompt. Ensure that a secret service/keyring is running and able to show prompts.", "UnlockPromptTimeout");

            var done = await completedTask.Task.ConfigureAwait(false);
            if (!done)
                throw new UserInformationException("Dimissed collection unlock prompt", "UnlockDismissed");

            // Unlock again, so we have the handles
            (unlocked, prompt) = await _service.UnlockAsync([_collection.Path]);
        }

        if (unlocked.Length == 0)
            throw new UserInformationException("Failed to unlock collection", "UnlockFailed");
        _locked = false;
    }

    /// <summary>
    /// Obtains the secrets from the collection
    /// </summary>
    /// <param name="labels">The labels to look for</param>
    /// <param name="comparer">The string comparer</param>
    /// <param name="cancellationToken">The cancellation token</param>
    /// <returns>The dictionary of secrets</returns>
    public async Task<Dictionary<string, string>> GetSecretsAsync(IEnumerable<string> labels, StringComparer comparer, CancellationToken cancellationToken)
    {
        var collection = _secretsService.CreateCollection(_collection.Path);
        var result = new Dictionary<string, string>(comparer);
        var requested = labels.ToHashSet(comparer);
        if (requested.Count == 0)
            return result;

        // Resolve all requested secrets in one batch by label.
        var items = await FindItemsAsync(collection, requested, comparer, cancellationToken).ConfigureAwait(false);

        foreach (var (label, item) in items)
        {
            try
            {
                var (_, _, secret, _) = await item.GetSecretAsync(_session.Path).ConfigureAwait(false);
                result[label] = Encoding.Default.GetString(secret);
            }
            catch (Exception ex)
            {
                Log.WriteWarningMessage(LogTag, "SecretLookupError", ex, $"Failed to get the secret value for {label}");
            }
        }

        var missing = requested.Where(l => !result.ContainsKey(l)).ToList();
        if (missing.Count > 0)
            throw new UserInformationException($"Missing secrets: {string.Join(", ", missing)}", "MissingSecrets");

        return result;
    }

    /// <summary>
    /// Stores or updates a secret in the collection.
    /// </summary>
    /// <param name="label">The label of the secret.</param>
    /// <param name="value">The secret value.</param>
    /// <param name="overwrite">Indicates whether existing secrets should be overwritten.</param>
    /// <param name="comparer">The comparer used for label comparison.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>An awaitable task.</returns>
    public async Task StoreSecretAsync(string label, string value, bool overwrite, StringComparer comparer, CancellationToken cancellationToken)
    {
        var collection = _secretsService.CreateCollection(_collection.Path);
        var found = await FindItemsAsync(collection, [label], comparer, cancellationToken).ConfigureAwait(false);
        var existingItem = found.GetValueOrDefault(label);

        if (existingItem != null && !overwrite)
            throw new UserInformationException($"The key '{label}' already exists", "KeyAlreadyExists");

        var secretPayload = (_session.Path, Array.Empty<byte>(), Encoding.UTF8.GetBytes(value), "text/plain");

        if (existingItem != null)
        {
            // Update existing item
            await existingItem.SetSecretAsync(secretPayload).ConfigureAwait(false);
            return;
        }

        // KDE's KeepSecret groups by the "server" attribute
        // Seahorse shows the "user" attribute underneath
        var attributes = new Dictionary<string, string>
        {
            [SchemaAttribute] = AppliedSchema,
            ["server"] = DuplicatiDisplayAttribute,
            ["user"] = DuplicatiDisplayAttribute,
            ["type"] = "plaintext"
        };

        var properties = new Dictionary<string, VariantValue>
        {
            ["org.freedesktop.Secret.Item.Label"] = VariantValue.String(label),
            ["org.freedesktop.Secret.Item.Attributes"] = new Dict<string, string>(attributes).AsVariantValue()
        };

        await collection.CreateItemAsync(properties, secretPayload, false).ConfigureAwait(false);
    }

    /// <summary>
    /// Finds existing items in the collection by label.
    /// </summary>
    /// <param name="collection">The collection to search.</param>
    /// <param name="labels">The labels to match.</param>
    /// <param name="comparer">The comparer used for label comparison.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A dictionary mapping each found label to its item.</returns>
    private async Task<Dictionary<string, Item>> FindItemsAsync(Collection collection, IEnumerable<string> labels, StringComparer comparer, CancellationToken cancellationToken)
    {
        var found = new Dictionary<string, Item>(comparer);
        var missing = labels.ToHashSet(comparer);
        if (missing.Count == 0)
            return found;

        // Search for items by label
        var entries = await collection.SearchItemsAsync(new Dictionary<string, string>()).ConfigureAwait(false);
        foreach (var entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (missing.Count == 0)
                break;

            var item = _secretsService.CreateItem(entry);
            try
            {
                var existingLabel = await item.GetLabelAsync().ConfigureAwait(false);
                if (missing.Remove(existingLabel))
                    found[existingLabel] = item;
            }
            catch (Exception ex)
            {
                Log.WriteWarningMessage(LogTag, "SecretLookupError", ex, "Failed to inspect secret");
            }
        }

        return found;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        try { _session.CloseAsync().Wait(); } catch { }
    }
}
