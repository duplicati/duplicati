backupApp.controller('HomeController', function ($scope, $location, BackupList, AppService) {
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
    	if (confirm('Do you really want to delete the backup: ' + name))
    		AppService.delete('/backup/' + id);
    };

    $scope.doDeleteLocalDb = function(id, name) {
    	if (confirm('Do you really want to delete the local database for: ' + name))
    		AppService.post('/backup/' + id + '/deletedb');
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

});