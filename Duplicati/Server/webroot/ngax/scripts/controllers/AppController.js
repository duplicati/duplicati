backupApp.controller('AppController', function($scope, $cookies, $location, AppService, BrandingService, ServerStatus, SystemInfo, AppUtils, DialogService, gettextCatalog) {
    $scope.brandingService = BrandingService.watch($scope);
    $scope.state = ServerStatus.watch($scope);
    $scope.systemInfo = SystemInfo.watch($scope);

    $scope.localized = {};
    $scope.location = $location;
    $scope.saved_theme = $scope.active_theme = $cookies.get('current-theme') || 'default';
    $scope.throttle_active = false;

    // If we want the theme settings
    // to be persisted on the server,
    // set to "true" here
    var save_theme_on_server = false;

    $scope.doReconnect = function() {
        ServerStatus.reconnect();
    };

    $scope.reload = function() {
        location.reload();
    };

    $scope.login = function() {
        location.href = '/login.html';
    };

    $scope.resume = function() {
        ServerStatus.resume().then(function() {}, AppUtils.connectionError);
    };

    $scope.pause = function(duration, pauseTransfers) {
        ServerStatus.pause(duration, pauseTransfers).then(function() {}, AppUtils.connectionError);
    };

    $scope.isLoggedIn = false;

    $scope.log_out = function() {
        // Use a path under /auth/refresh to allow the cookie to be sent for deletion
        // Calling `/auth/logout` also works, but does not revoke the token in the database
        AppService.post('/auth/refresh/logout').then(function() {
            AppService.clearAccessToken();
            location.href = '/login.html';            
        }, AppUtils.connectionError);
    };

    $scope.pauseOptions = function() {
        if ($scope.state.programState != 'Running') {
            $scope.resume();
        } else {
            DialogService.htmlDialog(
                gettextCatalog.getString('Pause options'), 
                'templates/pause.html', 
                [gettextCatalog.getString('OK'), gettextCatalog.getString('Cancel')], 
                function(index, text, cur) {
                    if (index == 0 && cur != null && cur.time != null) {
                        var time = cur.time;
                        var pauseTransfers = cur.pauseTransfers;
                        $scope.pause(time == 'infinite' ? '' : time, pauseTransfers);
                    }
                }
            );
        }
    };

    $scope.throttleOptions = function() {
        DialogService.htmlDialog(
            gettextCatalog.getString('Throttle settings'), 
            'templates/throttle.html', 
            [gettextCatalog.getString('OK'), gettextCatalog.getString('Cancel')], 
            function(index, text, cur) {
                if (index == 0 && cur != null && cur.uploadspeed != null && cur.downloadspeed != null) {
                    var patchdata = {
                        'max-download-speed': cur.downloadthrottleenabled ? cur.downloadspeed : '',
                        'max-upload-speed': cur.uploadthrottleenabled ? cur.uploadspeed : '',
                    };

                    AppService.patch('/serversettings', patchdata, {headers: {'Content-Type': 'application/json; charset=utf-8'}}).then(function(data) {
                        $scope.throttle_active = cur.downloadthrottleenabled || cur.uploadthrottleenabled;
                    }, AppUtils.connectionError);
                }
            }
        );
    };

    function updateCurrentPage() {

        $scope.active_theme = $scope.saved_theme;

        if ($location.$$path == '/' || $location.$$path == '')
            $scope.current_page = 'home';
        else if ($location.$$path == '/addstart' || $location.$$path == '/add' || $location.$$path == '/import')
            $scope.current_page = 'add';
        else if ($location.$$path == '/restorestart' || $location.$$path == '/restore' || $location.$$path == '/restoredirect' || $location.$$path.indexOf('/restore/') == 0)
            $scope.current_page = 'restore';
        else if ($location.$$path == '/settings')
            $scope.current_page = 'settings';
        else if ($location.$$path == '/log')
            $scope.current_page = 'log';
        else if ($location.$$path == '/about')
            $scope.current_page = 'about';
        else
            $scope.current_page = '';
    };

    $scope.$on('serverstatechanged', function() {
        // Unwanted jQuery interference, but the menu is built with this
        if (ServerStatus.state.programState == 'Paused') {
            $('#contextmenu_pause').removeClass('open');
            $('#contextmenulink_pause').removeClass('open');            
        }
        $scope.isLoggedIn = ServerStatus.state.connectionState == 'connected';
    });

    //$scope.$on('$routeUpdate', updateCurrentPage);
    $scope.$watch('location.$$path', updateCurrentPage);
    updateCurrentPage();

    function loadCurrentTheme() {
        if (save_theme_on_server) {
            AppService.get('/uisettings/ngax').then(
                function(data) {
                    var theme = 'default';
                    if (data.data != null && (data.data['theme'] || '').trim().length > 0)
                        theme = data.data['theme'];

                    var now = new Date();
                    var exp = new Date(now.getFullYear()+10, now.getMonth(), now.getDate());
                    $cookies.put('current-theme', theme, { expires: exp });
                    $scope.saved_theme = $scope.active_theme = theme;
                }, function() {}
            );
        }
    };

    // In case the cookie is out-of-sync
    loadCurrentTheme();

    $scope.$on('update_theme', function(event, args) {
        var theme = 'default';
        if (args != null && (args.theme || '').trim().length != 0)
            theme = args.theme;

        if (save_theme_on_server) {
            // Set it here to avoid flickering when the page changes
            $scope.saved_theme = $scope.active_theme = theme;

            AppService.patch('/uisettings/ngax', { 'theme': theme }, {'headers': {'Content-Type': 'application/json'}}).then(
                function(data) {
                    var now = new Date();
                    var exp = new Date(now.getFullYear()+10, now.getMonth(), now.getDate());
                    $cookies.put('current-theme', theme, { expires: exp });
                    $scope.saved_theme = $scope.active_theme = theme;
                }, function() {}
            );
        } else {
            var now = new Date();
            var exp = new Date(now.getFullYear()+10, now.getMonth(), now.getDate());
            $cookies.put('current-theme', theme, { expires: exp });
            $scope.saved_theme = $scope.active_theme = theme;
        }

        loadCurrentTheme();
    });

    $scope.$on('preview_theme', function(event, args) {
        if (args == null || (args.theme + '').trim().length == 0)
            $scope.active_theme = $scope.saved_theme;
        else
            $scope.active_theme = args.theme || '';
    });

    AppService.get('/serversettings').then(function(data) {
        $scope.forceActualDate = AppUtils.parseBoolString(data.data['--force-actual-date']);

        var ut = data.data['max-upload-speed'];
        var dt = data.data['max-download-speed'];
        $scope.throttle_active = (ut != null && ut.trim().length != 0) || (dt != null && dt.trim().length != 0);

        var has_asked = AppUtils.parseBoolString(data.data['has-asked-for-password-change']);
        var autogen_passphrase = AppUtils.parseBoolString(data.data['autogenerated-passphrase']);
        if (!has_asked && autogen_passphrase) {
            DialogService.dialog(
                gettextCatalog.getString('First run setup'),
                gettextCatalog.getString('Duplicati needs to be secured with a passphrase and a random passphrase has been generated for you.\nIf you open Duplicati from the tray icon, you do not need a passphrase, but if you plan to open it from another location you need to set a passphrase you know.\nDo you want to set a passphrase now?'),                
                [gettextCatalog.getString('Yes'), gettextCatalog.getString('No')],
                function(btn) {
                    if (btn == 1) {
                        // Set the flag so we don't ask again
                        AppService.patch('/serversettings', { 'has-asked-for-password-change': 'true'});
                    } 
                    else 
                    {
                        DialogService.htmlDialog(
                            gettextCatalog.getString('Change server password'),
                            'templates/changepassword.html',
                            [gettextCatalog.getString('OK'), gettextCatalog.getString('Cancel')], 
                            function(index, text, cur) {
                                if (index != 0)
                                    return;

                                AppService.patch('/serversettings', 
                                { 
                                    'has-asked-for-password-change': 'true',
                                    'server-passphrase': cur.remotePassword
                                })
                                .then(function() {}, AppUtils.connectionError);
                            },
                            null,
                            function(index, text, cur) {
                                if (index != 0)
                                    return true;

                                if (cur.remotePassword == null || cur.remotePassword.length == 0)
                                {
                                    alert("Please enter a passphrase");
                                    return false;
                                }

                                if(cur.remotePassword != cur.confirmPassword)
                                {
                                    alert("Passwords do not match");
                                    return false;
                                }

                                return true;
                            }
                        );
                    }
                }
            );
        } 
    }, AppUtils.connectionError);
});
