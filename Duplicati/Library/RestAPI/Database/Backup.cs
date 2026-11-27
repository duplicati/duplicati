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
using Duplicati.Server.Serialization;
using Duplicati.Server.Serialization.Interface;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;

namespace Duplicati.Server.Database
{
    public class Backup : IBackup
    {
        public Backup()
        {
            this.ID = null;
        }

        internal void LoadChildren(Connection con)
        {
            if (this.IsTemporary)
            {
                this.Sources = new string[0];
                this.Settings = new ISetting[0];
                this.Filters = new IFilter[0];
                this.Metadata = new Dictionary<string, string>();
            }
            else
            {
                var id = long.Parse(this.ID);
                this.Sources = con.GetSources(id);
                this.Settings = con.GetSettings(id);
                this.Filters = con.GetFilters(id);
                this.Metadata = con.GetMetadata(id);
                this.AdditionalTargetURLs = con.GetBackupTargetUrls(id);
            }
        }

        public void SetDBPath(string path) => this.DBPath = path;

        /// <summary>
        /// The backup ID
        /// </summary>
        public string ID { get; set; }
        /// <summary>
        /// The external ID for tracking the backup, or null if not set
        /// </summary>
        public string ExternalID { get; set; }
        /// <summary>
        /// The backup name
        /// </summary>
        public string Name { get; set; }
        /// <summary>
        /// The backup description
        /// </summary>
        public string Description { get; set; }
        /// <summary>
        /// The backup tags
        /// </summary>
        public string[] Tags { get; set; }
        /// <summary>
        /// The backup target url
        /// </summary>
        public string TargetURL { get; set; }
        /// <summary>
        /// The path to the local database
        /// </summary>
        public string DBPath { get; internal set; }

        /// <summary>
        /// The connection string ID, or -1 if not used
        /// </summary>
        public long ConnectionStringID { get; set; } = -1;

        /// <summary>
        /// The operation this backup performs when it runs.
        /// </summary>
        public OperationType OperationType { get; set; } = OperationType.Backup;

        /// <summary>
        /// Gets or sets a value indicating whether the scheduler runs this backup.
        /// </summary>
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// The ID of the backup this backup runs after, or <c>null</c> if it does not follow another backup.
        /// </summary>
        public string RunAfterBackupID { get; set; }

        /// <summary>
        /// The outcome the run of <see cref="RunAfterBackupID"/> must have for this backup to be queued.
        /// </summary>
        public RunAfterCondition RunAfterCondition { get; set; } = RunAfterCondition.Success;

        /// <summary>
        /// The backup source folders and files
        /// </summary>
        public string[] Sources { get; set; }

        /// <summary>
        /// The backup settings
        /// </summary>
        public ISetting[] Settings { get; set; }

        /// <summary>
        /// The filters applied to the source files
        /// </summary>
        public IFilter[] Filters { get; set; }

        /// <summary>
        /// The backup metadata
        /// </summary>
        public IDictionary<string, string> Metadata { get; set; }

        /// <summary>
        /// Additional target URLs for remote synchronization
        /// These are used by RemoteSynchronizationModule and are separate from the primary TargetURL
        /// </summary>
        public IEnumerable<ITargetUrlEntry> AdditionalTargetURLs { get; set; } = new List<ITargetUrlEntry>();

        /// <summary>
        /// Gets a value indicating if this instance is not persisted to the database
        /// </summary>        
        public bool IsTemporary { get { return ID != null && ID.IndexOf("-", StringComparison.Ordinal) > 0; } }

        /// <summary>
        /// Sanitizes the backup TargetUrl from any fields in the PasswordFieldNames list.
        /// </summary>
        private void SanitizeTargetUrl()
        {
            // The rest of the URL is kept as it is written, as the encoded values are what the
            // decode_uri function in AppUtils.js expects
            this.TargetURL = QuerystringMasking.Remove(this.TargetURL, Connection.PasswordFieldNames);
        }

        /// <summary>
        /// Sanitizes the settings from any fields in the PasswordFieldNames list.
        /// </summary>
        private void SanitizeSettings()
        {
            this.Settings = this.Settings.Where((setting) => !Connection.PasswordFieldNames.Contains(setting.Name)).ToArray();
        }

        /// <summary>
        /// Sanitizes the sources from any fields in the PasswordFieldNames list.
        /// </summary>
        private void SanitizeSources()
        {
            if (this.Sources == null)
                return;

            for (int i = 0; i < this.Sources.Length; i++)
            {
                if (SourceMasking.IsSpecialSource(this.Sources[i]))
                {
                    var urlString = SourceMasking.ExtractUrl(this.Sources[i]);
                    var sanitized = QuerystringMasking.Remove(urlString, Connection.PasswordFieldNames);
                    if (sanitized != urlString)
                        this.Sources[i] = SourceMasking.ReplaceUrl(this.Sources[i], sanitized);
                }
            }
        }

        /// <inheritdoc/>
        public void RemoveSensitiveInformation()
        {
            SanitizeTargetUrl();
            SanitizeSettings();
            SanitizeSources();
            SanitizeAdditionalTargetUrls();
        }

        /// <summary>
        /// Sanitizes the additional target URLs from any fields in the PasswordFieldNames list.
        /// </summary>
        private void SanitizeAdditionalTargetUrls()
        {
            if (AdditionalTargetURLs == null)
                return;

            foreach (var target in AdditionalTargetURLs)
            {
                if (target == null || string.IsNullOrEmpty(target.TargetUrl))
                    continue;

                target.TargetUrl = QuerystringMasking.Remove(target.TargetUrl, Connection.PasswordFieldNames);
            }
        }

        /// <summary>
        /// Reads the value of an option of a secondary destination as text
        /// </summary>
        /// <param name="value">The value, which is a string or the JSON it was read from</param>
        /// <returns>The text, or null if the value is not a string</returns>
        private static string TargetOptionText(object value)
            => value switch
            {
                string text => text,
                System.Text.Json.JsonElement { ValueKind: System.Text.Json.JsonValueKind.String } element => element.GetString(),
                _ => null
            };

        /// <summary>
        /// Checks if an option of a secondary destination holds the password placeholder
        /// </summary>
        /// <param name="target">The secondary destination</param>
        /// <returns><c>true</c> if an option holds the placeholder; otherwise <c>false</c></returns>
        internal static bool HasMaskedTargetOption(ITargetUrlEntry target)
            => target?.Options != null && target.Options.Values.Any(x => Connection.IsPasswordPlaceholder(TargetOptionText(x)));

        /// <summary>
        /// Replaces the options of a secondary destination that hold a secret with the password placeholder
        /// </summary>
        /// <param name="options">The options of the destination</param>
        /// <param name="protectedNames">The names of the options that hold a secret</param>
        /// <returns>The options without the secrets</returns>
        private static Dictionary<string, object> MaskTargetOptions(Dictionary<string, object> options, IReadOnlySet<string> protectedNames)
            => options == null || !options.Keys.Any(protectedNames.Contains)
                ? options
                : options.ToDictionary(
                    x => x.Key,
                    x => protectedNames.Contains(x.Key) ? (object)Connection.PASSWORD_PLACEHOLDER : x.Value,
                    options.Comparer);

        /// <summary>
        /// Puts the stored secrets back in the options of a secondary destination that hold the password placeholder
        /// </summary>
        /// <param name="target">The secondary destination</param>
        /// <param name="previous">The stored destination with the same key, if there is one</param>
        private static void UnmaskTargetOptions(ITargetUrlEntry target, ITargetUrlEntry previous)
        {
            if (!HasMaskedTargetOption(target))
                return;

            var prevOptions = new Dictionary<string, object>(previous?.Options ?? [], StringComparer.OrdinalIgnoreCase);
            target.Options = target.Options.ToDictionary(
                x => x.Key,
                x => !Connection.IsPasswordPlaceholder(TargetOptionText(x.Value))
                    ? x.Value
                    : prevOptions.TryGetValue(x.Key, out var prevValue)
                        ? prevValue
                        : throw new InvalidOperationException($"Cannot unmask option '{x.Key}' of the target URL with key '{target.TargetUrlKey}' because it did not exist in the previous configuration."),
                target.Options.Comparer);
        }

        /// <inheritdoc/>
        public void MaskSensitiveInformation()
        {
            var protectedNames = Connection.PasswordFieldNames;
            TargetURL = QuerystringMasking.Mask(TargetURL, protectedNames);
            Sources = SourceMasking.MaskSources(Sources, protectedNames);

            // Mask additional target URLs
            if (AdditionalTargetURLs != null)
                foreach (var target in AdditionalTargetURLs)
                    if (target != null)
                    {
                        target.TargetUrl = QuerystringMasking.Mask(target.TargetUrl, protectedNames);
                        target.Options = MaskTargetOptions(target.Options, protectedNames);
                    }

            if (Settings != null)
                foreach (var setting in Settings)
                    if (protectedNames.Contains(setting.Name))
                        setting.Value = Connection.PASSWORD_PLACEHOLDER;
        }

        /// <inheritdoc/>
        public void UnmaskSensitiveInformation(IBackup previous, IReadOnlyDictionary<long, string> connectionStrings)
        {
            if (previous == null)
                throw new ArgumentNullException(nameof(previous));

            // If there is a connection string ID, and it is different from the previous one,
            // the user has changed the connection strings source
            var constr = ConnectionStringID > 0 && ConnectionStringID != previous.ConnectionStringID
                ? connectionStrings.GetValueOrDefault(previous.ConnectionStringID)
                : previous.TargetURL;

            // An empty target is left for the validation to report, rather than failing here
            if (!string.IsNullOrEmpty(TargetURL))
                TargetURL = QuerystringMasking.Unmask(TargetURL, [constr, previous.TargetURL]);
            Sources = SourceMasking.UnmaskSources(Sources, previous.Sources);

            // Unmask additional target URLs
            var prevAdditionalTargets = previous.AdditionalTargetURLs?.ToList();
            if (AdditionalTargetURLs != null && prevAdditionalTargets != null)
            {
                var prevTargets = prevAdditionalTargets.ToDictionary(
                    x => x.TargetUrlKey,
                    x => x,
                    StringComparer.OrdinalIgnoreCase);

                foreach (var target in AdditionalTargetURLs)
                {
                    if (target != null)
                        UnmaskTargetOptions(target, prevTargets.GetValueOrDefault(target.TargetUrlKey));

                    if (target != null && Connection.UrlContainsPasswordPlaceholder(target.TargetUrl))
                    {
                        if (prevTargets.TryGetValue(target.TargetUrlKey, out var prevValue))
                        {
                            var prevTarget = target.ConnectionStringID > 0 && target.ConnectionStringID != prevValue.ConnectionStringID
                                ? connectionStrings.GetValueOrDefault(prevValue.ConnectionStringID)
                                : prevValue.TargetUrl;

                            target.TargetUrl = QuerystringMasking.Unmask(target.TargetUrl, [prevTarget, prevValue.TargetUrl]);
                        }
                        else if (target.ConnectionStringID > 0)
                        {
                            target.TargetUrl = QuerystringMasking.Unmask(target.TargetUrl, connectionStrings.GetValueOrDefault(target.ConnectionStringID));
                        }
                        else
                            throw new InvalidOperationException($"Cannot unmask target URL with key '{target.TargetUrlKey}' because it did not exist in the previous configuration.");
                    }
                }
            }

            var prevSettings = previous.Settings.ToDictionary(x => x.Name, x => x.Value, StringComparer.OrdinalIgnoreCase);
            foreach (var setting in this.Settings)
            {
                if (Connection.IsPasswordPlaceholder(setting.Value))
                {
                    if (prevSettings.TryGetValue(setting.Name, out var prevValue))
                        setting.Value = prevValue;
                    else
                        throw new InvalidOperationException($"Cannot unmask setting '{setting.Name}' because it did not exist in the previous configuration.");
                }
            }
        }

        public Backup Clone()
        {
            return new Backup()
            {
                ID = this.ID,
                ExternalID = this.ExternalID,
                Name = this.Name,
                Description = this.Description,
                Tags = (string[])this.Tags?.Clone() ?? [],
                TargetURL = this.TargetURL,
                ConnectionStringID = this.ConnectionStringID,
                OperationType = this.OperationType,
                Enabled = this.Enabled,
                RunAfterBackupID = this.RunAfterBackupID,
                RunAfterCondition = this.RunAfterCondition,
                DBPath = this.DBPath,
                Sources = (string[])this.Sources?.Clone() ?? [],
                Settings = this.Settings?.Select(s => new Setting { Name = s.Name, Value = s.Value, Filter = s.Filter }).ToArray() ?? [],
                Filters = this.Filters?.Select(f => new Filter { Order = f.Order, Include = f.Include, Expression = f.Expression }).ToArray() ?? [],
                Metadata = new Dictionary<string, string>(this.Metadata),
                AdditionalTargetURLs = this.AdditionalTargetURLs?.Select(t => new TargetUrlEntry
                {
                    ID = t.ID,
                    BackupID = t.BackupID,
                    TargetUrlKey = t.TargetUrlKey,
                    TargetUrl = t.TargetUrl,
                    Mode = t.Mode,
                    Interval = t.Interval,
                    Options = t.Options?.ToDictionary(kvp => kvp.Key, kvp => kvp.Value),
                    CreatedAt = t.CreatedAt,
                    UpdatedAt = t.UpdatedAt
                }).Cast<ITargetUrlEntry>().ToList() ?? new List<ITargetUrlEntry>()
            };
        }
    }
}

