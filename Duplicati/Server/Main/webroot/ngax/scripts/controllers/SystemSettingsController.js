backupApp.controller('SystemSettingsController', function($rootScope, $scope, $route, $cookies, AppService, DialogService, AppUtils, SystemInfo, gettextCatalog) {

    $scope.SystemInfo = SystemInfo.watch($scope);    
    $scope.theme = $scope.$parent.$parent.saved_theme;
    if (($scope.theme || '').trim().length == 0)
        $scope.theme = 'default';

    $scope.usageReporterLevel = '';

    function reloadOptionsList() {
        $scope.advancedOptionList = AppUtils.buildOptionList($scope.SystemInfo, false, false, false);
        var mods = [];
        if ($scope.SystemInfo.ServerModules != null)
            for(var ix in $scope.SystemInfo.ServerModules)
            {
                var m = $scope.SystemInfo.ServerModules[ix];
                if (m.SupportedGlobalCommands != null && m.SupportedGlobalCommands.length > 0)
                    mods.push(m);
            }

        $scope.ServerModules = mods;
        AppUtils.extractServerModuleOptions($scope.advancedOptions, $scope.ServerModules, $scope.servermodulesettings, 'SupportedGlobalCommands');
    };

    reloadOptionsList();
    $scope.$on('systeminfochanged', reloadOptionsList);

    $scope.$watch('theme', function() {
        $rootScope.$broadcast('preview_theme', { theme: $scope.theme });
    });

    $scope.uiLanguage = $cookies.get('ui-locale');
    $scope.lang_browser_default = gettextCatalog.getString('Browser default');
    $scope.lang_default = gettextCatalog.getString('Default');

    function setUILanguage() {
        if (($scope.uiLanguage || '').trim().length == 0) {
            $cookies.remove('ui-locale');
            gettextCatalog.setCurrentLanguage($scope.SystemInfo.BrowserLocale.Code.replace("-", "_"));
        } else {
            var now = new Date();
            var exp = new Date(now.getFullYear()+10, now.getMonth(), now.getDate());
            $cookies.put('ui-locale', $scope.uiLanguage, { expires: exp });

            gettextCatalog.setCurrentLanguage($scope.uiLanguage.replace("-", "_"));
        }
        $rootScope.$broadcast('ui_language_changed');
    };

    AppService.get('/serversettings').then(function(data) {

        $scope.rawdata = data.data;

        $scope.requireRemotePassword = data.data['server-passphrase'] != null && data.data['server-passphrase'] != '';
        $scope.remotePassword = data.data['server-passphrase'];
        $scope.confirmPassword = '';
        $scope.allowRemoteAccess = data.data['server-listen-interface'] != 'loopback';
        $scope.startupDelayDurationValue = data.data['startup-delay'].substr(0, data.data['startup-delay'].length - 1) == "" ? "0" : data.data['startup-delay'].substr(0, data.data['startup-delay'].length - 1);
        $scope.startupDelayDurationMultiplier = data.data['startup-delay'].substr(-1) == "" ? "s" : data.data['startup-delay'].substr(-1);
        $scope.updateChannel = data.data['update-channel'];
        $scope.originalUpdateChannel = data.data['update-channel'];
        $scope.usageReporterLevel = data.data['usage-reporter-level'];
        $scope.disableTrayIconLogin =  AppUtils.parseBoolString(data.data['disable-tray-icon-login']);
        $scope.remoteHostnames = data.data['allowed-hostnames'];
        $scope.advancedOptions = AppUtils.serializeAdvancedOptionsToArray(data.data);
        $scope.servermodulesettings = {};

        AppUtils.extractServerModuleOptions($scope.advancedOptions, $scope.ServerModules, $scope.servermodulesettings, 'SupportedGlobalCommands');
        
    }, AppUtils.connectionError);


    $scope.save = function() {

        if ($scope.requireRemotePassword && $scope.remotePassword.trim().length == 0)
            return AppUtils.notifyInputError('Cannot use empty password');

        var patchdata = {
            'server-passphrase': $scope.requireRemotePassword ? $scope.remotePassword : '',
            'allowed-hostnames': $scope.remoteHostnames,
            'server-listen-interface': $scope.allowRemoteAccess ? 'any' : 'loopback',
            'startup-delay': $scope.startupDelayDurationValue + '' + $scope.startupDelayDurationMultiplier,
            'update-channel': $scope.updateChannel,
            'usage-reporter-level': $scope.usageReporterLevel,
            'disable-tray-icon-login': $scope.disableTrayIconLogin
        };

        if ($scope.requireRemotePassword && $scope.rawdata['server-passphrase'] != $scope.remotePassword) {
            if ($scope.remotePassword != $scope.confirmPassword) {
                AppUtils.notifyInputError(gettextCatalog.getString('The passwords do not match'));
                return;
            }
            patchdata['server-passphrase-salt'] =  CryptoJS.lib.WordArray.random(256/8).toString(CryptoJS.enc.Base64);
            patchdata['server-passphrase'] = CryptoJS.SHA256(CryptoJS.enc.Hex.parse(CryptoJS.enc.Utf8.parse($scope.remotePassword) + CryptoJS.enc.Base64.parse(patchdata['server-passphrase-salt']))).toString(CryptoJS.enc.Base64);
        } else if (!$scope.requireRemotePassword) {
            patchdata['server-passphrase-salt'] = null;
            patchdata['server-passphrase'] = null;
        }

        AppUtils.mergeAdvancedOptions($scope.advancedOptions, patchdata, $scope.rawdata);
        for(var n in $scope.servermodulesettings)
            patchdata['--' + n] = $scope.servermodulesettings[n];

        $rootScope.$broadcast('update_theme', { theme: $scope.theme } );

        AppService.patch('/serversettings', patchdata, {headers: {'Content-Type': 'application/json; charset=utf-8'}}).then(
            function() {
                setUILanguage();

                // Check for updates if we changed the channel
                if ($scope.updateChannel != $scope.originalUpdateChannel)
                    AppService.post('/updates/check');

                $route.reload();
            },
            AppUtils.connectionError(gettextCatalog.getString('Failed to save:') + ' ')
        );
    };

    $scope.suppressDonationMessages = function() {
        AppService.post('/systeminfo/suppressdonationmessages').then(
            function() 
            { 
                $scope.SystemInfo.SuppressDonationMessages = true; 
                SystemInfo.notifyChanged();
            }, 
            AppUtils.connectionError(gettextCatalog.getString('Operation failed:') + ' ')
        );
    };

    $scope.displayDonationMessages = function() {
        AppService.post('/systeminfo/showdonationmessages').then(
            function() 
            { 
                $scope.SystemInfo.SuppressDonationMessages = false; 
                SystemInfo.notifyChanged();
            }, 
            AppUtils.connectionError(gettextCatalog.getString('Operation failed:') + ' ')
        );
    };
});
