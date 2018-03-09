backupApp.directive('notificationArea', function() {
  return {
    restrict: 'E',
    templateUrl: 'templates/notificationarea.html',
    controller: function($scope, $location, $timeout, NotificationService, ServerStatus, AppService, AppUtils, DialogService) {
        $scope.Notifications = NotificationService.watch($scope);
        $scope.state = ServerStatus.watch($scope);

        $scope.doDismiss = function(id) {
            AppService.delete('/notification/' + id).then(
                function() { }, // Don't care, the message will be removed
                function(resp) {
                    // Most likely there was a sync problem, so attempt to reload
                    NotificationService.refresh_notifications();
                }
            );
        };

        $scope.doShowLog = function(backupid) {
            AppService.get('/backup/' + backupid + '/isactive').then(
                function() {
                    $location.path('/log/' + backupid);
                },

                function(resp) {

                    if (resp.status == 404) {
                        if ((parseInt(backupid) + '') != backupid)
                            DialogService.dialog('Error', 'The backup was temporary and does not exist anymore, so the log data is lost');
                        else
                            DialogService.dialog('Error', 'The backup is missing, has it been deleted?');
                    } else {
                        AppUtils.connectionError('Failed to find backup: ', resp);
                    }

                }
            );
        };

        $scope.doRepair = function(backupid) {
        AppService.post('/backup/' + backupid + '/repair');
        $location.path('/');
        };

        $scope.doInstallUpdate = function(id) {
            AppService.post('/updates/install');
        };

        $scope.doActivateUpdate = function(id) {
            AppService.post('/updates/activate').then(function() { $scope.doDismiss(id); }, AppUtils.connectionError('Activate failed: '));
        };

        $scope.doShowUpdate = function(id) {
            $location.path('/updatechangelog'); 
        };

        $scope.doDownloadBugreport = function(item) {
            var id = item.Action.substr('bug-report:created:'.length);
            item.DownloadLink = $scope.DownloadLink = AppService.get_bugreport_url(id);
        };
    }
  }
});
