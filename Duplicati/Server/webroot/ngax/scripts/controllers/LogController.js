backupApp.controller('LogController', function($scope, $routeParams, $timeout, SystemInfo, ServerStatus, AppService, DialogService, BackupList, gettextCatalog) {
    $scope.state = ServerStatus.watch($scope);
    $scope.BackupID = $routeParams.backupid;
    $scope.SystemInfo = SystemInfo.watch($scope);

    var liveRefreshTimer = null;
    var PAGE_SIZE = 100;

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
                $scope.LiveData.Length = Math.min(1000, $scope.LiveData.length);

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

    function LoadMoreData(url, key, idfield) {
        if ($scope.LoadingData)
            return;

        var last = null;
        if ($scope[key] != null && $scope[key].length > 0 )
            last = $scope[key][$scope[key].length - 1][idfield];

        $scope.LoadingData = true;
        AppService.get(url + '?pagesize=' + PAGE_SIZE + (last == null ? '' : ('&offset=' + last))).then(
            function(resp) { 
                if ($scope[key] == null)
                    $scope[key] = [];
                $scope[key].push.apply($scope[key], resp.data);
                $scope.LoadingData = false;
                $scope[key + 'Complete'] = resp.data.length < PAGE_SIZE;
                if ($scope.BackupID != null)
                    $scope.Backup = BackupList.lookup[$scope.BackupID];

            }, function(resp) {
                var message = resp.statusText;
                if (resp.data != null && resp.data.Message != null)
                    message = resp.data.Message;

                $scope.LoadingData = false;
                DialogService.dialog('Error', gettextCatalog.getString('Failed to connect: {{message}}', { message: message }));
            });        
    };


    $scope.$watch('LiveLogLevel', updateLivePoll);
    $scope.$watch('Page', updateLivePoll);
    $scope.$watch('Page', function() {
        if ($scope.Page == 'remote' && $scope.RemoteData == null)
            $scope.LoadMoreRemoteData();
    });

    if ($scope.BackupID == null) {
        $scope.Page = 'stored';
        $scope.LiveLogLevel = '';
        $scope.LiveRefreshID = 0;
        $scope.LiveRefreshing = false;
        $scope.LiveRefreshPending = false;

        $scope.LoadMoreStoredData = function() { LoadMoreData('/logdata/log', 'LogData', 'Timestamp'); };
        $scope.LoadMoreStoredData();
    } else {
        $scope.Page = 'general';        

        $scope.LoadMoreGeneralData = function() { LoadMoreData('/backup/' + $scope.BackupID + '/log', 'GeneralData', 'ID'); };
        $scope.LoadMoreRemoteData = function() { LoadMoreData('/backup/' + $scope.BackupID + '/remotelog', 'RemoteData', 'ID'); };
        
        $scope.LoadMoreGeneralData();
        $scope.Backup = BackupList.lookup[$scope.BackupID];
    }

});
