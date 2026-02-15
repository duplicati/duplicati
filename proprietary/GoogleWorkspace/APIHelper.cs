// Copyright (c) 2026 Duplicati Inc. All rights reserved.

using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Gmail.v1;
using Google.Apis.Admin.Directory.directory_v1;
using Google.Apis.Calendar.v3;
using Google.Apis.Drive.v3;
using Google.Apis.PeopleService.v1;
using Google.Apis.Tasks.v1;
using Google.Apis.Keep.v1;
using Google.Apis.Groupssettings.v1;
using Google.Apis.HangoutsChat.v1;
using Google.Apis.Services;
using Google.Apis.Http;
using Google.Apis.Util;

namespace Duplicati.Proprietary.GoogleWorkspace;

public class APIHelper
{
    private readonly string? _clientId;
    private readonly string? _clientSecret;
    private readonly string? _refreshToken;
    private readonly string? _serviceAccountJson;
    private readonly string? _adminEmail;
    private readonly bool _isRestoreOperation;

    private DirectoryService? _directoryServiceForUsers;
    private DirectoryService? _directoryServiceForGroups;
    private DirectoryService? _directoryServiceForOrgUnits;
    private DriveService? _driveService;
    private GroupssettingsService? _groupsSettingsService;

    public APIHelper(OptionsHelper.GoogleWorkspaceOptions options, bool isRestoreOperation)
    {
        _clientId = options.ClientId;
        _clientSecret = options.ClientSecret;
        _refreshToken = options.RefreshToken;
        _serviceAccountJson = options.ServiceAccountJson;
        _adminEmail = options.AdminEmail;
        _isRestoreOperation = isRestoreOperation;
    }

    private BaseClientService.Initializer GetServiceInitializer(string? userId, IEnumerable<string> scopes)
    {
        var backoffInitializer = new BackOffInitializer();
        if (!string.IsNullOrEmpty(_serviceAccountJson))
        {
            var credential = GoogleCredential.FromJson(_serviceAccountJson)
                .CreateScoped(scopes.ToArray());

            var userToImpersonate = userId ?? _adminEmail;
            if (!string.IsNullOrEmpty(userToImpersonate))
            {
                credential = credential.CreateWithUser(userToImpersonate);
            }

            return new BaseClientService.Initializer()
            {
                HttpClientInitializer = new CompositeHttpClientInitializer(credential, backoffInitializer),
                ApplicationName = "Duplicati",
            };
        }
        else if (!string.IsNullOrEmpty(_clientId) && !string.IsNullOrEmpty(_clientSecret) && !string.IsNullOrEmpty(_refreshToken))
        {
            var credential = new UserCredential(new GoogleAuthorizationCodeFlow(
                new GoogleAuthorizationCodeFlow.Initializer
                {
                    ClientSecrets = new ClientSecrets
                    {
                        ClientId = _clientId,
                        ClientSecret = _clientSecret
                    }
                }),
                "user",
                new Google.Apis.Auth.OAuth2.Responses.TokenResponse { RefreshToken = _refreshToken });

            return new BaseClientService.Initializer()
            {
                HttpClientInitializer = new CompositeHttpClientInitializer(credential, backoffInitializer),
                ApplicationName = "Duplicati",
            };
        }
        else
        {
            throw new Exception("Missing credentials");
        }
    }

    public void TestConnection()
    {
        // Test connection by getting the Gmail service
        var gmailService = GetGmailService("me");
        gmailService.Users.GetProfile("me").Execute();
    }
    public GmailService GetGmailService(string userId)
    {
        var scopes = _isRestoreOperation
            ? new[] { "https://www.googleapis.com/auth/gmail.modify" }
            : new[] { GmailService.Scope.GmailReadonly };
        return new GmailService(GetServiceInitializer(userId, scopes));
    }

    public DirectoryService GetDirectoryServiceForUsers()
    {
        if (_directoryServiceForUsers == null)
        {
            var scopes = _isRestoreOperation
                ? new[] { DirectoryService.Scope.AdminDirectoryUser }
                : new[] { DirectoryService.Scope.AdminDirectoryUserReadonly };
            _directoryServiceForUsers = new DirectoryService(GetServiceInitializer(null, scopes));
        }
        return _directoryServiceForUsers!;
    }

    public DirectoryService GetDirectoryServiceForGroups()
    {
        if (_directoryServiceForGroups == null)
        {
            var scopes = _isRestoreOperation
                ? new[] { DirectoryService.Scope.AdminDirectoryGroup }
                : new[] { DirectoryService.Scope.AdminDirectoryGroupReadonly };
            _directoryServiceForGroups = new DirectoryService(GetServiceInitializer(null, scopes));
        }
        return _directoryServiceForGroups!;
    }

    public DirectoryService GetDirectoryServiceForOrgUnits()
    {
        if (_directoryServiceForOrgUnits == null)
        {
            var scopes = _isRestoreOperation
                ? new[] { DirectoryService.Scope.AdminDirectoryOrgunit }
                : new[] { DirectoryService.Scope.AdminDirectoryOrgunitReadonly };
            _directoryServiceForOrgUnits = new DirectoryService(GetServiceInitializer(null, scopes));
        }
        return _directoryServiceForOrgUnits!;
    }

    public CalendarService GetCalendarService(string userId)
    {
        var scopes = _isRestoreOperation
            ? new[] { CalendarService.Scope.Calendar }
            : new[] { CalendarService.Scope.CalendarReadonly };

        return new CalendarService(GetServiceInitializer(userId, scopes));
    }

    public CalendarService GetCalendarAclService(string userId)
    {
        // ACL reading requires WRITE permissions
        var scopes = new[] { CalendarService.Scope.Calendar };
        return new CalendarService(GetServiceInitializer(userId, scopes));
    }

    public DriveService GetDriveService(string? userId = null)
    {
        var scopes = _isRestoreOperation
            ? new[] { DriveService.Scope.Drive }
            : new[] { DriveService.Scope.DriveReadonly };

        if (userId != null)
        {
            return new DriveService(GetServiceInitializer(userId, scopes));
        }
        if (_driveService == null)
        {
            _driveService = new DriveService(GetServiceInitializer(null, scopes));
        }
        return _driveService!;
    }

    public PeopleServiceService GetPeopleService(string userId)
    {
        var scopes = _isRestoreOperation
            ? new[] { "https://www.googleapis.com/auth/contacts" }
            : new[] { PeopleServiceService.Scope.ContactsReadonly };

        return new PeopleServiceService(GetServiceInitializer(userId, scopes));
    }

    public TasksService GetTasksService(string userId)
    {
        var scopes = _isRestoreOperation
            ? new[] { TasksService.Scope.Tasks }
            : new[] { TasksService.Scope.TasksReadonly };

        return new TasksService(GetServiceInitializer(userId, scopes));
    }

    public KeepService GetKeepService(string userId)
    {
        var scopes = _isRestoreOperation
            ? new[] { KeepService.Scope.Keep }
            : new[] { KeepService.Scope.KeepReadonly };

        return new KeepService(GetServiceInitializer(userId, scopes));
    }

    public GroupssettingsService GetGroupsSettingsService()
    {
        if (_groupsSettingsService == null)
        {
            var scopes = new[] { GroupssettingsService.Scope.AppsGroupsSettings };
            _groupsSettingsService = new GroupssettingsService(GetServiceInitializer(null, scopes));
        }
        return _groupsSettingsService!;
    }

    public HangoutsChatService GetChatService(string userId)
    {
        var scopes = _isRestoreOperation
            ? new[]
            {
                HangoutsChatService.Scope.ChatMessages
            }
            : new[]
            {
                HangoutsChatService.Scope.ChatSpacesReadonly,
                HangoutsChatService.Scope.ChatMessagesReadonly,
                HangoutsChatService.Scope.ChatMembershipsReadonly
            };

        return new HangoutsChatService(GetServiceInitializer(userId, scopes));
    }

    private class CompositeHttpClientInitializer : IConfigurableHttpClientInitializer
    {
        private readonly IConfigurableHttpClientInitializer[] _initializers;

        public CompositeHttpClientInitializer(params IConfigurableHttpClientInitializer[] initializers)
        {
            _initializers = initializers;
        }

        public void Initialize(ConfigurableHttpClient httpClient)
        {
            foreach (var initializer in _initializers)
            {
                initializer.Initialize(httpClient);
            }
        }
    }

    private class BackOffInitializer : IConfigurableHttpClientInitializer
    {
        public void Initialize(ConfigurableHttpClient httpClient)
        {
            var backoffHandler = new BackOffHandler(new BackOffHandler.Initializer(new ExponentialBackOff())
            {
                HandleUnsuccessfulResponseFunc = (r) => r.StatusCode == System.Net.HttpStatusCode.ServiceUnavailable || r.StatusCode == (System.Net.HttpStatusCode)429
            });

            httpClient.MessageHandler.AddUnsuccessfulResponseHandler(backoffHandler);
            httpClient.MessageHandler.AddExceptionHandler(backoffHandler);
        }
    }
}
