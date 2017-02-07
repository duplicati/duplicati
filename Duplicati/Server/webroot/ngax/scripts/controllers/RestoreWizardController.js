backupApp.controller('RestoreWizardController', function($scope, $location, BackupList, gettextCatalog) {
    $scope.backups = BackupList.watch($scope);

    $scope.selection = {
        backupid: '-1'
    };

    $scope.nextPage = function() {
        if ($scope.selection.backupid == '-1')
            $location.path('/restoredirect');
        else
            $location.path('/restore/' + $scope.selection.backupid);
    };
});
