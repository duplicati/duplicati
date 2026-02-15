// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using System.Text.Json;
using Duplicati.Library.Common.IO;
using Duplicati.Library.Logging;
using Duplicati.Proprietary.GoogleWorkspace.SourceItems;
using Google.Apis.PeopleService.v1.Data;

namespace Duplicati.Proprietary.GoogleWorkspace;

partial class RestoreProvider
{
    private ContactRestoreHelper? _contactRestoreHelper = null;
    internal ContactRestoreHelper ContactRestore => _contactRestoreHelper ??= new ContactRestoreHelper(this);

    internal class ContactRestoreHelper(RestoreProvider Provider)
    {
        private string? _targetUserId = null;
        private bool _hasLoadedTargetInfo = false;

        public async Task<string?> GetUserIdAndContactsTarget(CancellationToken cancel)
        {
            if (_hasLoadedTargetInfo)
                return _targetUserId;

            var target = Provider.RestoreTarget;
            if (target == null)
                throw new InvalidOperationException("Restore target is not set");

            if (target.Type == SourceItemType.User)
            {
                _targetUserId = target.Metadata.GetValueOrDefault("gsuite:Id");
            }
            else if (target.Type == SourceItemType.UserContacts)
            {
                _targetUserId = target.Path.TrimStart('/').Split('/').Skip(1).FirstOrDefault();
            }
            else
            {
                Log.WriteWarningMessage(LOGTAG, "RestoreContactsInvalidTargetType", null, $"Restore target type {target.Type} is not valid for restoring contacts.");
            }

            _hasLoadedTargetInfo = true;

            if (string.IsNullOrWhiteSpace(_targetUserId))
            {
                Log.WriteWarningMessage(LOGTAG, "RestoreContactsMissingUserId", null, $"Missing target userId for restoring contacts.");
                return null;
            }

            return _targetUserId;
        }

        public async Task<string?> CreateContact(string userId, Person contact, CancellationToken cancel)
        {
            var peopleService = Provider._apiHelper.GetPeopleService(userId);

            // Check for duplicates by email if available
            if (!Provider._ignoreExisting && contact.EmailAddresses?.Count > 0)
            {
                var searchEmail = contact.EmailAddresses[0].Value;
                if (!string.IsNullOrWhiteSpace(searchEmail))
                {
                    try
                    {
                        var searchRequest = peopleService.People.SearchContacts();
                        searchRequest.Query = searchEmail;
                        var searchResponse = await searchRequest.ExecuteAsync(cancel);
                        var duplicate = searchResponse.Results?.FirstOrDefault(r =>
                            r.Person?.EmailAddresses?.Any(e => e.Value?.Equals(searchEmail, StringComparison.OrdinalIgnoreCase) == true) == true);

                        if (duplicate != null)
                        {
                            Log.WriteInformationMessage(LOGTAG, "CreateContactSkipDuplicate", $"Contact with email {searchEmail} already exists, skipping.");
                            return duplicate.Person?.ResourceName;
                        }
                    }
                    catch
                    {
                        // Search might fail, continue with creation
                    }
                }
            }

            // Clean up properties that shouldn't be sent on creation
            contact.ResourceName = null;
            contact.ETag = null;
            contact.Metadata = null;

            var createdContact = await peopleService.People.CreateContact(contact).ExecuteAsync(cancel);
            return createdContact.ResourceName;
        }

        public async Task<string?> CreateContactGroup(string userId, ContactGroup group, CancellationToken cancel)
        {
            var peopleService = Provider._apiHelper.GetPeopleService(userId);

            // Check for duplicates by name
            if (!Provider._ignoreExisting)
            {
                var existingGroups = await peopleService.ContactGroups.List().ExecuteAsync(cancel);
                var duplicate = existingGroups.ContactGroups?.FirstOrDefault(g =>
                    g.Name?.Equals(group.Name, StringComparison.OrdinalIgnoreCase) == true);

                if (duplicate != null)
                {
                    Log.WriteInformationMessage(LOGTAG, "CreateContactGroupSkipDuplicate", $"Contact group {group.Name} already exists, skipping.");
                    return duplicate.ResourceName;
                }
            }

            // Clean up properties
            group.ResourceName = null;
            group.ETag = null;
            group.MemberCount = null;
            group.MemberResourceNames = null;

            var createdGroup = await peopleService.ContactGroups.Create(new CreateContactGroupRequest { ContactGroup = group }).ExecuteAsync(cancel);
            return createdGroup.ResourceName;
        }

        public async Task AddContactToGroup(string userId, string groupResourceName, string contactResourceName, CancellationToken cancel)
        {
            var peopleService = Provider._apiHelper.GetPeopleService(userId);

            var modifyRequest = new ModifyContactGroupMembersRequest
            {
                ResourceNamesToAdd = new List<string> { contactResourceName }
            };

            await peopleService.ContactGroups.Members.Modify(modifyRequest, groupResourceName).ExecuteAsync(cancel);
        }
    }

    private async Task RestoreContacts(CancellationToken cancel)
    {
        if (RestoreTarget == null)
            throw new InvalidOperationException("Restore target entry is not set");

        var contacts = GetMetadataByType(SourceItemType.Contact);
        if (contacts.Count == 0)
            return;

        var userId = await ContactRestore.GetUserIdAndContactsTarget(cancel);
        if (string.IsNullOrWhiteSpace(userId))
            return;

        foreach (var contact in contacts)
        {
            if (cancel.IsCancellationRequested)
                break;

            try
            {
                var originalPath = contact.Key;
                var contentEntry = _temporaryFiles.GetValueOrDefault(originalPath);

                if (contentEntry == null)
                {
                    Log.WriteWarningMessage(LOGTAG, "RestoreContactsMissingContent", null, $"Missing content for contact {originalPath}, skipping.");
                    continue;
                }

                Person? contactData;
                using (var contentStream = SystemIO.IO_OS.FileOpenRead(contentEntry))
                {
                    contactData = await JsonSerializer.DeserializeAsync<Person>(contentStream, cancellationToken: cancel);
                }

                if (contactData == null)
                {
                    Log.WriteWarningMessage(LOGTAG, "RestoreContactsInvalidContent", null, $"Invalid content for contact {originalPath}, skipping.");
                    continue;
                }

                await ContactRestore.CreateContact(userId, contactData, cancel);

                _metadata.TryRemove(originalPath, out _);
                _temporaryFiles.TryRemove(originalPath, out var contentFile);
                contentFile?.Dispose();
            }
            catch (Exception ex)
            {
                Log.WriteErrorMessage(LOGTAG, "RestoreContactsFailed", ex, $"Failed to restore contact {contact.Key}");
            }
        }
    }

    private async Task RestoreContactGroups(CancellationToken cancel)
    {
        if (RestoreTarget == null)
            throw new InvalidOperationException("Restore target entry is not set");

        var groups = GetMetadataByType(SourceItemType.ContactGroup);
        if (groups.Count == 0)
            return;

        var userId = await ContactRestore.GetUserIdAndContactsTarget(cancel);
        if (string.IsNullOrWhiteSpace(userId))
            return;

        foreach (var group in groups)
        {
            if (cancel.IsCancellationRequested)
                break;

            try
            {
                var originalPath = group.Key;
                var contentEntry = _temporaryFiles.GetValueOrDefault(originalPath);

                if (contentEntry == null)
                {
                    Log.WriteWarningMessage(LOGTAG, "RestoreContactGroupsMissingContent", null, $"Missing content for group {originalPath}, skipping.");
                    continue;
                }

                ContactGroup? groupData;
                using (var contentStream = SystemIO.IO_OS.FileOpenRead(contentEntry))
                {
                    groupData = await JsonSerializer.DeserializeAsync<ContactGroup>(contentStream, cancellationToken: cancel);
                }

                if (groupData == null)
                {
                    Log.WriteWarningMessage(LOGTAG, "RestoreContactGroupsInvalidContent", null, $"Invalid content for group {originalPath}, skipping.");
                    continue;
                }

                var newGroupResourceName = await ContactRestore.CreateContactGroup(userId, groupData, cancel);

                // Restore group members if available
                var membersPath = SystemIO.IO_OS.PathCombine(originalPath, "members.json");
                if (_temporaryFiles.TryRemove(membersPath, out var membersEntry))
                {
                    try
                    {
                        using var membersStream = SystemIO.IO_OS.FileOpenRead(membersEntry);
                        var members = await JsonSerializer.DeserializeAsync<List<string>>(membersStream, cancellationToken: cancel);

                        if (members != null && newGroupResourceName != null)
                        {
                            foreach (var memberResourceName in members)
                            {
                                try
                                {
                                    await ContactRestore.AddContactToGroup(userId, newGroupResourceName, memberResourceName, cancel);
                                }
                                catch (Exception ex)
                                {
                                    Log.WriteWarningMessage(LOGTAG, "RestoreContactGroupMemberFailed", ex, $"Failed to add member {memberResourceName} to group {newGroupResourceName}");
                                }
                            }
                        }

                        _metadata.TryRemove(membersPath, out _);
                        membersEntry?.Dispose();
                    }
                    catch (Exception ex)
                    {
                        Log.WriteWarningMessage(LOGTAG, "RestoreContactGroupMembersFailed", ex, $"Failed to restore members for group {originalPath}");
                    }
                }

                _metadata.TryRemove(originalPath, out _);
                _temporaryFiles.TryRemove(originalPath, out var contentFile);
                contentFile?.Dispose();
            }
            catch (Exception ex)
            {
                Log.WriteErrorMessage(LOGTAG, "RestoreContactGroupsFailed", ex, $"Failed to restore contact group {group.Key}");
            }
        }
    }
}
