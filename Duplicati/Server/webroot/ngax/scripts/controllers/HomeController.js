backupApp.controller('HomeController', function ($scope, $location, ServerStatus, BackupList, AppService, DialogService, gettextCatalog) {
    $scope.backups = BackupList.watch($scope);

    $scope.doRun = function(id) {
        AppService.post('/backup/' + id + '/run').then(function() {
            if (ServerStatus.state.programState == 'Paused') {
                DialogService.dialog(gettextCatalog.getString('Server paused'), gettextCatalog.getString('Server is currently paused, do you want to resume now?'), [gettextCatalog.getString('No'), gettextCatalog.getString('Yes')], function(ix) {
                    if (ix == 1)
                        ServerStatus.resume();
                });

            }
        }, function() {});
    };

    $scope.doRestore = function(id) {
        $location.path('/restore/' + id);
    };

    $scope.doEdit = function(id) {
        $location.path('/edit/' + id);
    };

    $scope.doExport = function(id) {
        $location.path('/export/' + id);
    };

    $scope.doCompact = function(id) {
        AppService.post('/backup/' + id + '/compact');
    };

    $scope.doDelete = function(id, name) {
        $location.path('/delete/' + id);
    };

    $scope.doLocalDb = function(id) {
        $location.path('/localdb/' + id);
    };

    $scope.doRepairLocalDb = function(id, name) {
        AppService.post('/backup/' + id + '/repair');
    };

    $scope.doVerifyRemote = function(id, name) {
        AppService.post('/backup/' + id + '/verify');
    };

    $scope.doShowLog = function(id, name) {
        $location.path('/log/' + id);
    };

    $scope.doCommandLine = function(id, name) {
        $location.path('/commandline/' + id);
    };

    $scope.doCreateBugReport = function(id, name) {
        AppService.post('/backup/' + id + '/createreport');
    };

    $scope.formatDuration = function(duration) {
        // parse days if timespan is over 24 hours long
        var days = 0;
        if (duration != null && duration.indexOf(".") < 7) {
            days = duration.substring(0, duration.indexOf("."));
            duration = duration.substring(duration.indexOf(".")+1, duration.length);
        }

        // strip miliseconds
        if (duration != null && duration.indexOf(".") > 0)
            duration = duration.substring(0, duration.indexOf("."));

        // prefix the days if applicable
        if (days != 0)
            return days + ":" + duration;
        else
            return duration;
    };
});
