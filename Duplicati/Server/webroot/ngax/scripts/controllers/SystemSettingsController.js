backupApp.controller('SystemSettingsController', function($scope, $location, AppService, AppUtils, Localization) {

    AppService.get('/serversettings').then(function(data) {

        $scope.rawdata = data.data;

        $scope.requireRemotePassword = data.data[''] != null && data.data.WebserverPassword != '';
        $scope.remotePassword = data.data.WebserverPassword;
        $scope.allowRemoteAccess = data.data['server-listen-interface'] != 'loopback';
        $scope.startupDelayDurationValue = data.data['startup-delay'].substr(0, data.data['startup-delay'].length - 1);
        $scope.startupDelayDurationMultiplier = data.data['startup-delay'].substr(-1);
        $scope.advancedOptions = AppUtils.serializeAdvancedOptions(data.data);
    }, AppUtils.connectionError);


    $scope.save = function() {

        if ($scope.requireRemotePassword && $scope.remotePassword.trim().length == 0)
            return AppUtil.notifyInputError('Cannot use empty password');

        var patchdata = {
            'server-passphrase': $scope.requireRemotePassword ? $scope.remotePassword : '',

            'server-listen-interface': $scope.allowRemoteAccess ? any : 'loopback',
            'startup-delay': $scope.startupDelayDurationValue + '' + $scope.startupDelayDurationMultiplier
        }


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

        AppService.patch('/serversettings', patchdata).then(
            function() {
                $location.path('/');
            },
            AppUtils.connectionError(Localization.localize('Failed to save'))
        );


    }
});