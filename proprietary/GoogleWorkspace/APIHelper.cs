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
    private readonly OptionsHelper.GoogleWorkspaceOptions _options;
    private readonly HashSet<string> _scopes;

    private GmailService? _gmailService;
    private DirectoryService? _directoryService;
    private CalendarService? _calendarService;
    private DriveService? _driveService;
    private PeopleServiceService? _peopleService;
    private TasksService? _tasksService;
    private KeepService? _keepService;
    private GroupssettingsService? _groupsSettingsService;
    private HangoutsChatService? _chatService;

    private static readonly Dictionary<GoogleRootType, string[]> _rootTypeScopes = new()
    {
        { GoogleRootType.Users, [DirectoryService.Scope.AdminDirectoryUserReadonly] },
        { GoogleRootType.Groups, [DirectoryService.Scope.AdminDirectoryGroupReadonly, GroupssettingsService.Scope.AppsGroupsSettings] },
        { GoogleRootType.SharedDrives, [DriveService.Scope.DriveReadonly] },
        { GoogleRootType.Sites, [DriveService.Scope.DriveReadonly] },
        { GoogleRootType.OrganizationalUnits, [DirectoryService.Scope.AdminDirectoryOrgunitReadonly] }
    };

    private static readonly Dictionary<GoogleUserType, string[]> _userTypeScopes = new()
    {
        { GoogleUserType.Gmail, [GmailService.Scope.GmailReadonly] },
        { GoogleUserType.Drive, [DriveService.Scope.DriveReadonly] },
        { GoogleUserType.Calendar, [CalendarService.Scope.CalendarReadonly] },
        { GoogleUserType.Contacts, [PeopleServiceService.Scope.ContactsReadonly] },
        { GoogleUserType.Tasks, [TasksService.Scope.TasksReadonly] },
        { GoogleUserType.Keep, [KeepService.Scope.KeepReadonly] },
        { GoogleUserType.Chat, [HangoutsChatService.Scope.ChatSpacesReadonly, HangoutsChatService.Scope.ChatMessagesReadonly, HangoutsChatService.Scope.ChatMembershipsReadonly] }
    };

    public APIHelper(OptionsHelper.GoogleWorkspaceOptions options)
    {
        _options = options;
        _clientId = options.ClientId;
        _clientSecret = options.ClientSecret;
        _refreshToken = options.RefreshToken;
        _serviceAccountJson = options.ServiceAccountJson;
        _adminEmail = options.AdminEmail;
        _scopes = GetConfiguredScopes();
    }

    public void Initialize()
    {
        var initializer = GetServiceInitializer();

        _gmailService = new GmailService(initializer);
        _directoryService = new DirectoryService(initializer);
        _calendarService = new CalendarService(initializer);
        _driveService = new DriveService(initializer);
        _peopleService = new PeopleServiceService(initializer);
        _tasksService = new TasksService(initializer);
        _keepService = new KeepService(initializer);
        _groupsSettingsService = new GroupssettingsService(initializer);
        _chatService = new HangoutsChatService(initializer);
    }

    private BaseClientService.Initializer GetServiceInitializer(string? userId = null)
    {
        var backoffInitializer = new BackOffInitializer();

        if (!string.IsNullOrEmpty(_serviceAccountJson))
        {
            var credential = GoogleCredential.FromJson(_serviceAccountJson)
                .CreateScoped(_scopes.ToArray());

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
        if (_gmailService == null) Initialize();
        _gmailService!.Users.GetProfile("me").Execute();
    }

    private HashSet<string> GetConfiguredScopes()
    {
        var scopes = new HashSet<string>();
        if (_options.RequestedScopes != null && _options.RequestedScopes.Length > 0)
        {
            scopes.UnionWith(_options.RequestedScopes);
        }
        else
        {
            foreach (var type in _options.IncludedRootTypes)
                if (_rootTypeScopes.TryGetValue(type, out var s))
                    scopes.UnionWith(s);

            foreach (var type in _options.IncludedUserTypes)
                if (_userTypeScopes.TryGetValue(type, out var s))
                    scopes.UnionWith(s);
        }
        return scopes;
    }

    public bool HasScope(string scope)
    {
        return _scopes.Contains(scope);
    }

    public GmailService GetGmailService(string userId)
    {
        return new GmailService(GetServiceInitializer(userId));
    }

    public DirectoryService GetDirectoryService()
    {
        if (_directoryService == null) Initialize();
        return _directoryService!;
    }

    public CalendarService GetCalendarService()
    {
        if (_calendarService == null) Initialize();
        return _calendarService!;
    }

    public DriveService GetDriveService()
    {
        if (_driveService == null) Initialize();
        return _driveService!;
    }

    public PeopleServiceService GetPeopleService()
    {
        if (_peopleService == null) Initialize();
        return _peopleService!;
    }

    public TasksService GetTasksService()
    {
        if (_tasksService == null) Initialize();
        return _tasksService!;
    }

    public KeepService GetKeepService()
    {
        if (_keepService == null) Initialize();
        return _keepService!;
    }

    public GroupssettingsService GetGroupsSettingsService()
    {
        if (_groupsSettingsService == null) Initialize();
        return _groupsSettingsService!;
    }

    public HangoutsChatService GetChatService()
    {
        if (_chatService == null) Initialize();
        return _chatService!;
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
