backupApp.controller('StateController', function($scope, $timeout, ServerStatus, BackupList, AppService, AppUtils) {
    $scope.state = ServerStatus.watch($scope);
    $scope.backups = BackupList.watch($scope);
    $scope.ServerStatus = ServerStatus;

    $scope.activeTask = null;

    var updateActiveTask = function() {
        $scope.activeBackup = $scope.state.activeTask == null ? null : BackupList.lookup[$scope.state.activeTask.Item2];
        $scope.nextTask = ($scope.state.schedulerQueueIds == null || $scope.state.schedulerQueueIds.length == 0) ? null : BackupList.lookup[$scope.state.schedulerQueueIds[0].Item2];
        $scope.nextScheduledTask = ($scope.state.proposedSchedule == null || $scope.state.proposedSchedule.length == 0) ? null : BackupList.lookup[$scope.state.proposedSchedule[0].Item1];
        $scope.nextScheduledTime = ($scope.state.proposedSchedule == null || $scope.state.proposedSchedule.length == 0) ? null : $scope.state.proposedSchedule[0].Item2;
    };

    $scope.$on('backuplistchanged', updateActiveTask);
    $scope.$on('serverstatechanged', updateActiveTask);
    $scope.$on('serverstatechanged.pauseTimeRemain', function() { $timeout(function() {$scope.$digest(); }) });

    $scope.sendResume = function() {
    	ServerStatus.resume().then(function() {}, AppUtils.connectionError);
    };

    function updateStateDisplay() {
        var text = 'Running ...';
        var pg = -1;
        if ($scope.state.lastPgEvent != null)
        {
            text = ServerStatus.progress_state_text[$scope.state.lastPgEvent.Phase || ''] || $scope.state.lastPgEvent.Phase;


            if ($scope.state.lastPgEvent.Phase == 'Backup_ProcessingFiles') {
                if ($scope.state.lastPgEvent.StillCounting) {
                    text = 'Counting (' + $scope.state.lastPgEvent.TotalFileCount + ' files found, ' + AppUtils.formatSizeString($scope.state.lastPgEvent.TotalFileSize) + ')';
                    pg = 0;
                } else {
                    var filesleft = $scope.state.lastPgEvent.TotalFileCount - $scope.state.lastPgEvent.ProcessedFileCount;
                    var sizeleft = $scope.state.lastPgEvent.TotalFileSize - $scope.state.lastPgEvent.ProcessedFileSize;
                    pg = $scope.state.lastPgEvent.ProcessedFileSize / $scope.state.lastPgEvent.TotalFileSize;

                    if ($scope.state.lastPgEvent.ProcessedFileCount == 0)
                        pg = 0;
                    else if (pg >= 1)
                        pg = 0.95;

                    text = filesleft + ' files (' + AppUtils.formatSizeString(sizeleft) + ') to go';
                }
            }
            else if ($scope.state.lastPgEvent.Phase == 'Backup_WaitForUpload') {
                pg = 1;
            }
            else if ($scope.state.lastPgEvent.OverallProgress > 0) {
                pg = $scope.state.lastPgEvent.OverallProgress;
            }
        }

        $scope.StateText = text;
        $scope.Progress = pg;
    };

    $scope.$watch('state.lastPgEvent', updateStateDisplay, true);

    $scope.stopTask = function() {
        var taskId = $scope.state.activeTask.Item1;
        if ($scope.StopReqId == taskId) {
            AppService.post('/task/' + taskId + '/abort');
        } else {
            AppService.post('/task/' + taskId + '/stop');
        }

        $scope.StopReqId = taskId;
    };

    updateStateDisplay();
    updateActiveTask();


});