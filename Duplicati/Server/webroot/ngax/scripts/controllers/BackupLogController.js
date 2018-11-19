backupApp.controller('BackupLogController', function($scope, $routeParams, LogService, BackupList) {
    $scope.BackupID = $routeParams.backupid;
    
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
                $scope.GeneralDataComplete = complete;
                $scope.Backup = BackupList.lookup[$scope.BackupID];
                for (let i in current) {
                    try { current[i].Result = JSON.parse(current[i].Message) }
                    catch {}
                }
                $scope.GeneralData = current;
                $scope.$digest();
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
                $scope.$digest();
            }); 
    };
    
    $scope.LoadMoreGeneralData();
    $scope.Backup = BackupList.lookup[$scope.BackupID];

});
