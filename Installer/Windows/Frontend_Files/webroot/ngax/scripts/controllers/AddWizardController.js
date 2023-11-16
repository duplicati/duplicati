backupApp.controller('AddWizardController', function($scope, $location, $routeParams, gettextCatalog, BackupList, DialogService,) {
    $scope.selection = {
        style: 'blank'
    };
    $scope.nextPage = function() {
        $scope.backups = BackupList.watch($scope);
        if ($scope.selection.style == 'blank')
        {
            if($scope.backups.length === 0)
                $location.path('/add');
            else if($scope.backups.length === 1)
            {   
                DialogService.dialog(gettextCatalog.getString('Maximum number of Backups reached'), gettextCatalog.getString('You can edit or delete your Backup by clicking the options below'), [gettextCatalog.getString('Edit'), gettextCatalog.getString('Delete'), gettextCatalog.getString('Cancel')], function (ix) {
                    if (ix == 0)
                    {
                        $location.path('/edit/' + $scope.backups[0].Backup.ID);
                    }
                    else if(ix==1)
                    {
                        $location.path('/delete/' + $scope.backups[0].Backup.ID);
                    }
                    else
                        $location.path('/');

                });
            }
        }
        else
            $location.path('/import');
    };
});
