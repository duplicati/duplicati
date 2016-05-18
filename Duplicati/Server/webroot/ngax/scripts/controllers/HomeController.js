backupApp.controller('HomeController', function ($scope, $location, BackupList, AppService, DialogService) {
    $scope.backups = BackupList.watch($scope);

    $scope.doRun = function(id) {
        AppService.post('/backup/' + id + '/run');
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

    $scope.doDelete = function(id, name) {
        DialogService.dialog('Confirm delete', 'Do you really want to delete the backup: ' + name, ['No', 'Yes'], function(ix) {
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