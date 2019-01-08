backupApp.controller('LogController', function($scope, $timeout, AppService, LogService, SystemInfo, ServerStatus) {
    $scope.state = ServerStatus.watch($scope);
    $scope.SystemInfo = SystemInfo.watch($scope);

    var liveRefreshTimer = null;
    const PAGE_SIZE = 100;

    function updateLivePoll() {
        if ($scope.LiveRefreshing) {
            $scope.LiveRefreshPending = true;
            return;
        }

        if (liveRefreshTimer != null) {
            $timeout.cancel(liveRefreshTimer);
            liveRefreshTimer = null;
        }

        if ($scope.Page != 'live' || ($scope.LiveLogLevel || '') == '')
            return;

        $scope.LiveRefreshPending = false;
        $scope.LiveRefreshing = true;
        AppService.get('/logdata/poll?level=' + $scope.LiveLogLevel + '&id=' + $scope.LiveRefreshID + '&pagesize=' + PAGE_SIZE).then(
            function(resp) {
                for(var n in resp.data)
                    $scope.LiveRefreshID = Math.max($scope.LiveRefreshID, resp.data[n].ID);

                if ($scope.LiveData == null)
                    $scope.LiveData = [];

                resp.data.reverse();
                $scope.LiveData.unshift.apply($scope.LiveData, resp.data);
                $scope.LiveData.Length = Math.min(300, $scope.LiveData.length);
                $scope.LiveData = $scope.LiveData.slice(0,$scope.LiveData.Length)

                $scope.LiveRefreshing = false;
                if ($scope.LiveRefreshPending)
                    updateLivePoll();
                else
                    if ($scope.Page == 'live' && $scope.LiveLogLevel != '')
                        liveRefreshTimer = $timeout(updateLivePoll, 3000);

            }, function(resp) {
                if ($scope.Page == 'live' && $scope.LiveLogLevel != '')
                    liveRefreshTimer = $timeout(updateLivePoll, 3000);
            }
        );
    };


    $scope.$watch('LiveLogLevel', updateLivePoll);
    $scope.$watch('Page', updateLivePoll);

    $scope.Page = 'stored';
    $scope.LiveLogLevel = '';
    $scope.LiveRefreshID = 0;
    $scope.LiveRefreshing = false;
    $scope.LiveRefreshPending = false;

    $scope.LoadMoreStoredData = function() {
        LogService.LoadMoreData('/logdata/log', $scope.LogData, 'Timestamp', PAGE_SIZE)
            .then(function(result) {
                if (!result)
                    return;

                $scope.LogData = result.current;
                $scope.LogDataComplete = result.complete;
                $scope.$digest();
            });
    };
    $scope.LoadMoreStoredData();


});
