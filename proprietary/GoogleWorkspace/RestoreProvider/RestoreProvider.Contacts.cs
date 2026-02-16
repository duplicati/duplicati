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

        /// <summary>
        /// Cleans contact metadata and removes read-only fields that cannot be sent during creation.
        /// </summary>
        private static void CleanContactMetadata(Person contact)
        {
            // Remove top-level read-only properties
            contact.ResourceName = null;
            contact.ETag = null;
            contact.Metadata = null;
            contact.Photos = null; // Photos are read-only and must be uploaded separately

            // Remove source IDs from fields as they are not allowed during creation
            if (contact.Names != null)
            {
                foreach (var name in contact.Names)
                {
                    name.Metadata = null;
                }
            }
            if (contact.EmailAddresses != null)
            {
                foreach (var email in contact.EmailAddresses)
                {
                    email.Metadata = null;
                }
            }
            if (contact.PhoneNumbers != null)
            {
                foreach (var phone in contact.PhoneNumbers)
                {
                    phone.Metadata = null;
                }
            }
            if (contact.Addresses != null)
            {
                foreach (var address in contact.Addresses)
                {
                    address.Metadata = null;
                }
            }
            if (contact.Organizations != null)
            {
                foreach (var org in contact.Organizations)
                {
                    org.Metadata = null;
                }
            }
            if (contact.ImClients != null)
            {
                foreach (var im in contact.ImClients)
                {
                    im.Metadata = null;
                }
            }
            if (contact.Birthdays != null)
            {
                foreach (var birthday in contact.Birthdays)
                {
                    birthday.Metadata = null;
                }
            }
            if (contact.Biographies != null)
            {
                foreach (var bio in contact.Biographies)
                {
                    bio.Metadata = null;
                }
            }
            if (contact.Nicknames != null)
            {
                foreach (var nickname in contact.Nicknames)
                {
                    nickname.Metadata = null;
                }
            }
            if (contact.Occupations != null)
            {
                foreach (var occupation in contact.Occupations)
                {
                    occupation.Metadata = null;
                }
            }
            if (contact.Relations != null)
            {
                foreach (var relation in contact.Relations)
                {
                    relation.Metadata = null;
                }
            }
            if (contact.Locations != null)
            {
                foreach (var location in contact.Locations)
                {
                    location.Metadata = null;
                }
            }
            if (contact.Events != null)
            {
                foreach (var evt in contact.Events)
                {
                    evt.Metadata = null;
                }
            }
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
            CleanContactMetadata(contact);

            var createdContact = await peopleService.People.CreateContact(contact).ExecuteAsync(cancel);
            return createdContact.ResourceName;
        }

        public async Task UploadContactPhoto(string userId, string contactResourceName, Stream photoStream, CancellationToken cancel)
        {
            var peopleService = Provider._apiHelper.GetPeopleService(userId);

            // Read the photo data into a byte array
            using var memoryStream = new MemoryStream();
            await photoStream.CopyToAsync(memoryStream, cancel);
            var photoBytes = memoryStream.ToArray();

            // Upload the photo using the updateContactPhoto endpoint
            var updateRequest = new Google.Apis.PeopleService.v1.Data.UpdateContactPhotoRequest
            {
                PhotoBytes = Convert.ToBase64String(photoBytes)
            };

            await peopleService.People.UpdateContactPhoto(updateRequest, contactResourceName).ExecuteAsync(cancel);
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
                // Look for content.json in the contact folder
                var contentJsonPath = SystemIO.IO_OS.PathCombine(originalPath, "content.json");
                var contentEntry = _temporaryFiles.GetValueOrDefault(contentJsonPath);

                if (contentEntry == null)
                {
                    Log.WriteWarningMessage(LOGTAG, "RestoreContactsMissingContent", null, $"Missing content.json for contact {originalPath}, skipping.");
                    continue;
                }

                Person? contactData;
                using (var contentStream = SystemIO.IO_OS.FileOpenRead(contentEntry))
                {
                    contactData = await JsonSerializer.DeserializeAsync<Person>(contentStream, GoogleApiJsonDeserializer.Options, cancellationToken: cancel);
                }

                if (contactData == null)
                {
                    Log.WriteWarningMessage(LOGTAG, "RestoreContactsInvalidContent", null, $"Invalid content for contact {originalPath}, skipping.");
                    continue;
                }

                var createdResourceName = await ContactRestore.CreateContact(userId, contactData, cancel);

                // Restore contact photos if any exist
                if (!string.IsNullOrEmpty(createdResourceName))
                {
                    await RestoreContactPhotos(userId, originalPath, createdResourceName, cancel);
                }

                // Clean up metadata and temporary files
                _metadata.TryRemove(originalPath, out _);
                _metadata.TryRemove(contentJsonPath, out _);
                _temporaryFiles.TryRemove(contentJsonPath, out var contentFile);
                contentFile?.Dispose();

                // Discard the contact.vcf file if it exists
                var vcfPath = SystemIO.IO_OS.PathCombine(originalPath, "contact.vcf");
                if (_temporaryFiles.TryRemove(vcfPath, out var vcfFile))
                {
                    vcfFile?.Dispose();
                    _metadata.TryRemove(vcfPath, out _);
                }
            }
            catch (Exception ex)
            {
                Log.WriteErrorMessage(LOGTAG, "RestoreContactsFailed", ex, $"Failed to restore contact {contact.Key}");
            }
        }
    }

    private async Task RestoreContactPhotos(string userId, string contactPath, string contactResourceName, CancellationToken cancel)
    {
        // Look for photo files in the contact folder (photo-0.jpg, photo-1.jpg, etc.)
        var photoIndex = 0;
        while (true)
        {
            var photoPath = SystemIO.IO_OS.PathCombine(contactPath, $"photo-{photoIndex}.jpg");
            if (!_temporaryFiles.TryGetValue(photoPath, out var photoEntry))
            {
                // No more photos found
                break;
            }

            try
            {
                using (var photoStream = SystemIO.IO_OS.FileOpenRead(photoEntry))
                {
                    await ContactRestore.UploadContactPhoto(userId, contactResourceName, photoStream, cancel);
                }

                Log.WriteInformationMessage(LOGTAG, "RestoreContactPhotoSuccess", $"Successfully restored photo {photoIndex} for contact {contactResourceName}");
            }
            catch (Exception ex)
            {
                Log.WriteWarningMessage(LOGTAG, "RestoreContactPhotoFailed", ex, $"Failed to restore photo {photoIndex} for contact {contactResourceName}");
            }
            finally
            {
                // Clean up the photo file
                if (_temporaryFiles.TryRemove(photoPath, out var photoFile))
                {
                    photoFile?.Dispose();
                }
                _metadata.TryRemove(photoPath, out _);
            }

            photoIndex++;
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
                    groupData = await JsonSerializer.DeserializeAsync<ContactGroup>(contentStream, GoogleApiJsonDeserializer.Options, cancellationToken: cancel);
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
                        var members = await JsonSerializer.DeserializeAsync<List<string>>(membersStream, GoogleApiJsonDeserializer.Options, cancellationToken: cancel);

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
