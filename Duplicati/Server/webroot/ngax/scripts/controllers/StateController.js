backupApp.controller('StateController', function($scope, $timeout, ServerStatus, BackupList, AppService, AppUtils, gettextCatalog) {
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
        var text = gettextCatalog.getString('Running ...');
        var pg = -1;
        if ($scope.state.lastPgEvent != null)
        {
            text = ServerStatus.progress_state_text[$scope.state.lastPgEvent.Phase || ''] || $scope.state.lastPgEvent.Phase;


            if ($scope.state.lastPgEvent.Phase == 'Backup_ProcessingFiles') {
                if ($scope.state.lastPgEvent.StillCounting) {
                    text = gettextCatalog.getString('Counting ({{files}} files found, {{size}})', { files: $scope.state.lastPgEvent.TotalFileCount, size: AppUtils.formatSizeString($scope.state.lastPgEvent.TotalFileSize) });
                    pg = 0;
                } else {
                    var filesleft = $scope.state.lastPgEvent.TotalFileCount - $scope.state.lastPgEvent.ProcessedFileCount;
                    var sizeleft = $scope.state.lastPgEvent.TotalFileSize - $scope.state.lastPgEvent.ProcessedFileSize - $scope.state.lastPgEvent.CurrentFileoffset;
                    pg = ($scope.state.lastPgEvent.ProcessedFileSize + $scope.state.lastPgEvent.CurrentFileoffset) / $scope.state.lastPgEvent.TotalFileSize;

                    if ($scope.state.lastPgEvent.ProcessedFileCount == 0)
                        pg = 0;
                    else if (pg >= 0.90)
                        pg = 0.90;

                    text = gettextCatalog.getString('{{files}} files ({{size}}) to go', { files: filesleft, size: AppUtils.formatSizeString(sizeleft) });
                }
            }
            else if ($scope.state.lastPgEvent.Phase == 'Backup_Finalize' || $scope.state.lastPgEvent.Phase == 'Backup_WaitForUpload')
            {
                pg = 0.90;
            } 
            else if ($scope.state.lastPgEvent.Phase == 'Backup_Delete' || $scope.state.lastPgEvent.Phase == 'Backup_Compact')
            {
                pg = 0.95;
            } 
            else if ($scope.state.lastPgEvent.Phase == 'Backup_VerificationUpload' || $scope.state.lastPgEvent.Phase == 'Backup_PostBackupVerify')
            {
                pg = 0.98;
            } 
            else if ($scope.state.lastPgEvent.Phase == 'Backup_Complete' || $scope.state.lastPgEvent.Phase == 'Backup_WaitForUpload')
            {
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
