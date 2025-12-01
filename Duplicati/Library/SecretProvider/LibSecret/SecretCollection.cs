// Copyright (C) 2025, The Duplicati Team
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
using System.Threading.Tasks;
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
    /// Creates a new secret collection
    /// </summary>
    /// <param name="collectionName">The collection name</param>
    /// <param name="cancellationToken">The cancellation token</param>
    /// <returns>The created secret collection</returns>
    public static Task<SecretCollection> CreateAsync(string collectionName, CancellationToken cancellationToken)
        => CreateAsync(collectionName, autoCreateCollection: false, cancellationToken);

    /// <summary>
    /// Creates a new secret collection and optionally auto-creates it if it does not exist.
    /// </summary>
    /// <param name="collectionName">The collection name</param>
    /// <param name="autoCreateCollection">If set, the collection will be created when it does not exist</param>
    /// <param name="cancellationToken">The cancellation token</param>
    /// <returns>The created secret collection</returns>
    public static async Task<SecretCollection> CreateAsync(string collectionName, bool autoCreateCollection, CancellationToken cancellationToken)
    {
        var connection = Connection.Session;
        var secretsService = new secretsService(connection, "org.freedesktop.secrets");
        var service = secretsService.CreateService("/org/freedesktop/secrets");
        var (_, sessionPath) = await service.OpenSessionAsync("plain", "").ConfigureAwait(false);
        collectionName ??= string.Empty;

        var collections = await service.GetCollectionsAsync().ConfigureAwait(false);
        var collectionPath = collections
            .FirstOrDefault(c => c.ToString().EndsWith(collectionName, StringComparison.OrdinalIgnoreCase));

        // If "default" collection doesn't exist, fall back to "login" collection
        if (!collectionPath.ToString().EndsWith(collectionName, StringComparison.OrdinalIgnoreCase)
            && string.Equals(collectionName, "default", StringComparison.OrdinalIgnoreCase))
        {
            collectionPath = collections
                .FirstOrDefault(c => c.ToString().EndsWith(DefaultCollectionActualName, StringComparison.OrdinalIgnoreCase));

            if (collectionPath.ToString().EndsWith(DefaultCollectionActualName, StringComparison.OrdinalIgnoreCase))
                collectionName = DefaultCollectionActualName;
        }

        if (!collectionPath.ToString().EndsWith(collectionName, StringComparison.OrdinalIgnoreCase))
        {
            if (!autoCreateCollection)
                throw new UserInformationException($"Collection {collectionName} not found", "CollectionNotFound");

            // Auto-create the collection
            var properties = new Dictionary<string, VariantValue>
            {
                ["org.freedesktop.Secret.Collection.Label"] = VariantValue.String(collectionName)
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

        var session = secretsService.CreateSession(sessionPath);
        var collection = secretsService.CreateCollection(collectionPath.ToString());
        var locked = await collection.GetLockedAsync().ConfigureAwait(false);

        return new SecretCollection(secretsService, service, session, collection, locked);
    }

    /// <summary>
    /// Checks whether the libsecret DBus service is available on the current platform.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns><c>true</c> when the provider can be used; otherwise <c>false</c>.</returns>
    public static async Task<bool> IsSupported(CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsLinux())
            return false;

        // Heuristic: libsecret prompts require a graphical session to be useful.
        // If there is no X11/Wayland display, we treat libsecret as unsupported.
        var hasDisplay =
            !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY")) ||
            !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY"));

        if (!hasDisplay)
            return false;

        try
        {
            var connection = Connection.Session;
            var secretsService = new secretsService(connection, "org.freedesktop.secrets");
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
    /// Checks whether a specific libsecret collection exists.
    /// This is a non-throwing, best-effort check used by <see cref="Duplicati.Library.SecretProvider.LibSecretLinuxProvider"/>
    /// to determine if the provider should be considered supported for a given collection.
    /// </summary>
    /// <param name="collectionName">The collection name to check. If null or empty, the default collection name is used.</param>
    /// param name="cancellationToken">The cancellation token.</param>
    /// <returns><c>true</c> if the collection exists and libsecret is available; otherwise <c>false</c>.</returns>
    public static async Task<bool> CollectionExists(string collectionName, CancellationToken cancellationToken)
    {
        try
        {
            var connection = Connection.Session;
            var secretsService = new secretsService(connection, "org.freedesktop.secrets");
            var service = secretsService.CreateService("/org/freedesktop/secrets");

            collectionName ??= string.Empty;

            var task = service.GetCollectionsAsync();

            if (await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(5), cancellationToken)).ConfigureAwait(false) != task)
                return false;

            var collections = await task.ConfigureAwait(false);
            return collections.Any(c => c.ToString().EndsWith(collectionName, StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
            // Any failure in talking to the secrets service or enumerating collections
            // is treated as the collection not being available.
            return false;
        }
    }

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
    }

    /// <summary>
    /// Obtains the secrets from the collection
    /// </summary>
    /// <param name="labels">The labels to look for</param>
    /// <param name="comparer">The string comparer</param>
    /// <returns>The dictionary of secrets</returns>
    public async Task<Dictionary<string, string>> GetSecretsAsync(IEnumerable<string> labels, StringComparer comparer)
    {
        var attributes = new Dictionary<string, string>();
        var collection = _secretsService.CreateCollection(_collection.Path);
        var entries = await collection.SearchItemsAsync(attributes).ConfigureAwait(false);
        var result = new Dictionary<string, string>(comparer);
        var missing = labels.ToHashSet(comparer);
        if (missing.Count == 0)
            return result;

        // Enumerate all items in the collection
        foreach (var r in entries)
        {
            var item = _secretsService.CreateItem(r);
            try
            {
                var label = await item.GetLabelAsync().ConfigureAwait(false);
                if (missing.Contains(label))
                {
                    var (sessionPath, _, secret, contentType) = await item.GetSecretAsync(_session.Path).ConfigureAwait(false);
                    result[label] = Encoding.Default.GetString(secret);
                    missing.Remove(label);

                    if (missing.Count == 0)
                        break;
                }
            }
            catch (Exception ex)
            {
                Log.WriteWarningMessage(LogTag, "SecretLookupError", ex, "Failed to get returned secret");
            }
        }

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
        var entries = await collection.SearchItemsAsync(new Dictionary<string, string>()).ConfigureAwait(false);

        Item? existingItem = null;
        foreach (var entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var item = _secretsService.CreateItem(entry);
            try
            {
                var existingLabel = await item.GetLabelAsync().ConfigureAwait(false);
                if (comparer.Equals(existingLabel, label))
                {
                    existingItem = item;
                    break;
                }
            }
            catch (Exception ex)
            {
                Log.WriteWarningMessage(LogTag, "SecretLookupError", ex, "Failed to inspect secret");
            }
        }

        if (existingItem != null && !overwrite)
            throw new UserInformationException($"The key '{label}' already exists", "KeyAlreadyExists");

        var secretPayload = (_session.Path, Array.Empty<byte>(), Encoding.UTF8.GetBytes(value), "text/plain");

        if (existingItem != null)
        {
            await existingItem.SetSecretAsync(secretPayload).ConfigureAwait(false);
            await existingItem.SetLabelAsync(label).ConfigureAwait(false);
            return;
        }

        var properties = new Dictionary<string, VariantValue>
        {
            ["org.freedesktop.Secret.Item.Label"] = VariantValue.String(label)
        };

        await collection.CreateItemAsync(properties, secretPayload, false).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        try { _session.CloseAsync().Wait(); } catch { }
    }
}
