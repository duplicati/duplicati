backupApp.controller('BackupLogController', function($scope, $routeParams, $timeout, SystemInfo, LogService, ServerStatus, AppService, DialogService, BackupList, gettextCatalog) {
    $scope.state = ServerStatus.watch($scope);
    $scope.BackupID = $routeParams.backupid;
    $scope.SystemInfo = SystemInfo.watch($scope);
    
    const PAGE_SIZE = 100;

    $scope.$watch('Page', function() {
        if ($scope.Page == 'remote' && $scope.RemoteData == null)
            $scope.LoadMoreRemoteData();
    });
    
    $scope.Page = 'general';        

    $scope.LoadMoreGeneralData = function() { 
        LogService.LoadMoreData('/backup/' + $scope.BackupID + '/log', $scope.GeneralData, 'ID', PAGE_SIZE)
            .then(function(result) {
                if (!result)
                    return;

                const { current, complete } = result;
                $scope.GeneralData = current;
                $scope.GeneralDataComplete = complete;
                $scope.Backup = BackupList.lookup[$scope.BackupID];
            }); 
    };
    $scope.LoadMoreRemoteData = function() { 
        LogService.LoadMoreData('/backup/' + $scope.BackupID + '/remotelog', $scope.RemoteData, 'ID', PAGE_SIZE)
            .then(function(result) {
                if (!result)
                    return;
                
                const { current, complete } = result;
                $scope.RemoteData = current;
                $scope.RemoteDataComplete = complete;
                $scope.Backup = BackupList.lookup[$scope.BackupID];
            }); 
    };
    
    $scope.LoadMoreGeneralData();
    $scope.Backup = BackupList.lookup[$scope.BackupID];

});
