backupApp.controller('SystemSettingsController', function($rootScope, $scope, $location, $cookies, AppService, AppUtils, SystemInfo, gettextCatalog) {

    $scope.SystemInfo = SystemInfo.watch($scope);

    function reloadOptionsList() {
        $scope.advancedOptionList = AppUtils.buildOptionList($scope.SystemInfo, false, false, false);
    }

    reloadOptionsList();

    $scope.$on('systeminfochanged', reloadOptionsList);

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
    }

    AppService.get('/serversettings').then(function(data) {

        $scope.rawdata = data.data;

        $scope.requireRemotePassword = data.data['server-passphrase'] != null && data.data['server-passphrase'] != '';
        $scope.remotePassword = data.data['server-passphrase'];
        $scope.allowRemoteAccess = data.data['server-listen-interface'] != 'loopback';
        $scope.startupDelayDurationValue = data.data['startup-delay'].substr(0, data.data['startup-delay'].length - 1);
        $scope.startupDelayDurationMultiplier = data.data['startup-delay'].substr(-1);
        $scope.updateChannel = data.data['update-channel'];
        $scope.originalUpdateChannel = data.data['update-channel'];
        $scope.usageReporterLevel = data.data['usage-reporter-level'];
        $scope.advancedOptions = AppUtils.serializeAdvancedOptionsToArray(data.data);

    }, AppUtils.connectionError);


    $scope.save = function() {

        if ($scope.requireRemotePassword && $scope.remotePassword.trim().length == 0)
            return AppUtil.notifyInputError('Cannot use empty password');

        var patchdata = {
            'server-passphrase': $scope.requireRemotePassword ? $scope.remotePassword : '',

            'server-listen-interface': $scope.allowRemoteAccess ? 'any' : 'loopback',
            'startup-delay': $scope.startupDelayDurationValue + '' + $scope.startupDelayDurationMultiplier,
            'update-channel': $scope.updateChannel,
            'usage-reporter-level': $scope.usageReporterLevel
        };


        if ($scope.requireRemotePassword) {
            if ($scope.rawdata['server-passphrase'] != $scope.remotePassword) {
                patchdata['server-passphrase-salt'] =  CryptoJS.lib.WordArray.random(256/8).toString(CryptoJS.enc.Base64);
                patchdata['server-passphrase'] = CryptoJS.SHA256(CryptoJS.enc.Hex.parse(CryptoJS.enc.Utf8.parse($scope.remotePassword) + CryptoJS.enc.Base64.parse(patchdata['server-passphrase-salt']))).toString(CryptoJS.enc.Base64);
            }
        } else {
            patchdata['server-passphrase-salt'] = null;
            patchdata['server-passphrase'] = null;
        }

        AppUtils.mergeAdvancedOptions($scope.advancedOptions, patchdata, $scope.rawdata);

        AppService.patch('/serversettings', patchdata, {headers: {'Content-Type': 'application/json; charset=utf-8'}}).then(
            function() {
                setUILanguage();

                // Check for updates if we changed the channel
                if ($scope.updateChannel != $scope.originalUpdateChannel)
                    AppService.post('/updates/check');

                $location.path('/');
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
