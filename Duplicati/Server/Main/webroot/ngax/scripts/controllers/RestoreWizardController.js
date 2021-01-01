backupApp.controller('RestoreWizardController', function($scope, $location, BackupList, AppUtils, gettextCatalog) {
    $scope.backups = BackupList.watch($scope);

    $scope.selection = {
        backupid: '-1'
    };

    $scope.nextPage = function() {
        if ($scope.selection.backupid == 'direct')
            $location.path('/restoredirect');
        else if ($scope.selection.backupid == 'import')
            $location.path('/restore-import');
        else
            $location.path('/restore/' + $scope.selection.backupid);
    };

    $scope.formatDuration = AppUtils.formatDuration;
});
