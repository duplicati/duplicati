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
        DialogService.dialog(gettextCatalog.getString('Confirm delete'), gettextCatalog.getString('Do you really want to delete the backup: {{name}}', {name: name}), [gettextCatalog.getString('No'), gettextCatalog.getString('Yes')], function(ix) {
            if (ix == 1)
                AppService.delete('/backup/' + id);
        });
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

    $scope.doCreateBugReport = function(id, name) {
        AppService.post('/backup/' + id + '/createreport');
    };

    $scope.formatDuration = function(duration) {
        if (duration != null && duration.indexOf(".") > 0)
            return duration.substring(0, duration.length - duration.indexOf("."));
        else
            return duration;
    };
});
