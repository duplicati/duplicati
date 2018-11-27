backupApp.controller('BackupLogController', function($scope, $routeParams, AppUtils, LogService, BackupList) {
    $scope.BackupID = $routeParams.backupid;
    
    const PAGE_SIZE = 100;

    $scope.$watch('Page', function() {
        if ($scope.Page == 'remote' && $scope.RemoteData == null)
            $scope.LoadMoreRemoteData();
    });

    $scope.parseTimestampToSeconds = function(ts) {
        return moment(ts).format('YYYY-MM-DD HH:mm:ss');
    }

    $scope.formatDuration = AppUtils.formatDuration;
    $scope.formatSize = AppUtils.formatSizeString;
    
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
                    try { 
                        current[i].Result = JSON.parse(current[i].Message);
                        current[i].Formatted = JSON.stringify(current[i].Result, null, 2);
                    }
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

    $scope.ResultIcon = function(parsedResult) {
        if (parsedResult == 'Success') {
            return 'fa fa-check-circle success-color';
        } else if (parsedResult == 'Warning') {
            return 'fa fa-exclamation-circle warning-color';
        } else if (parsedResult == 'Error') {
            return 'fa fa-times-circle error-color';
        } else {
            return 'fa fa-question-circle';
        }
    }
    
    $scope.LoadMoreGeneralData();
    $scope.Backup = BackupList.lookup[$scope.BackupID];

});
