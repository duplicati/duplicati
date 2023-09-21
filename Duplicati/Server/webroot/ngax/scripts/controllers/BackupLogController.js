backupApp.controller('BackupLogController', function($scope, $routeParams, AppUtils, LogService, BackupList, gettextCatalog) {
    $scope.BackupID = $routeParams.backupid;

    const PAGE_SIZE = 15;

    $scope.$watch('Page', function() {
        if ($scope.Page == 'remote' && $scope.RemoteData == null)
            $scope.LoadMoreRemoteData();
    });

    $scope.parseTimestampToSeconds = function(ts) {
        return moment(ts).format('YYYY-MM-DD HH:mm:ss');
    }

    $scope.gettextCatalog = gettextCatalog;

    $scope.formatDuration = AppUtils.formatDuration;
    $scope.formatSize = AppUtils.formatSizeString;

    $scope.Page = 'general';

    $scope.LoadMoreGeneralData = function() {
        LogService.LoadMoreData('/backup/' + $scope.BackupID + '/log', $scope.GeneralData, 'ID', PAGE_SIZE)
            .then(function(result) {
                if (!result)
                    return;

                var current = result.current;
                $scope.GeneralDataComplete = result.complete;
                $scope.Backup = BackupList.lookup[$scope.BackupID];
                for (var i in current) {
                    try {
                        current[i].Result = JSON.parse(current[i].Message);
                        current[i].Formatted = JSON.stringify(current[i].Result, null, 2);
                    }
                    catch (err) {
                        // catch block meant to be empty (avoiding eslint warning)
                        // it is empty because if a result fails to be parsed, it was
                        // probably stored in the old format and thus should be displayed
                        // with a single gray code box.
                    }
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

                $scope.RemoteData = result.current;
                $scope.RemoteDataComplete = result.complete;
                $scope.Backup = BackupList.lookup[$scope.BackupID];
                $scope.$digest();
            });
    };

    $scope.ResultIcon = function (parsedResult) {
        if (parsedResult == 'Success') {
            return 'fa fa-check-circle success-color';
        } else if (parsedResult == 'Warning') {
            return 'fa fa-exclamation-circle warning-color';
        } else if (parsedResult == 'Error') {
            return 'fa fa-times-circle error-color';
        } else if (parsedResult == 'Fatal') {
            return 'fa fa-exclamation-triangle fatal-color';
        } else {
            return 'fa fa-question-circle';
        }
    };

    $scope.LoadMoreGeneralData();
    $scope.Backup = BackupList.lookup[$scope.BackupID];

});
