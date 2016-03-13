backupApp.service('ServerStatus', function($rootScope, $timeout, AppService, AppUtils) {

    var longpolltime = 5 * 60 * 1000;

    var waitingfortask = {};

    var state = {
        lastEventId: -1,
        lastDataUpdateId: -1,
        lastNotificationUpdateId: -1,
        estimatedPauseEnd: new Date("0001-01-01T00:00:00"),
        activeTask: null,
        programState: null,
        lastErrorMessage: null,
        connectionState: 'connected',
        xsfrerror: false,
        connectionAttemptTimer: 0,
        failedConnectionAttempts: 0,
        lastPgEvent: null,
        updaterState: 'Waiting',
        updatedVersion: null,
        updateReady: false,
        updateDownloadProgress: 0,
        proposedSchedule: [],
        schedulerQueueIds: []
    };

    this.state = state;

    this.progress_state_text = {
        'Backup_Begin': 'Starting ...',
        'Backup_PreBackupVerify': 'Verifying backend data ...',
        'Backup_PostBackupTest': 'Verifying remote data ...',
        'Backup_PreviousBackupFinalize': 'Completing previous backup ...',
        'Backup_ProcessingFiles': null,
        'Backup_Finalize': 'Completing backup ...',
        'Backup_WaitForUpload': 'Waiting for upload ...',
        'Backup_Delete': 'Deleting unwanted files ...',
        'Backup_Compact': 'Compacting remote data ...',
        'Backup_VerificationUpload': 'Uploading verification file ...',
        'Backup_PostBackupVerify': 'Verifying backend data ...',
        'Backup_Complete': 'Finished!',
        'Restore_Begin': 'Starting ...',
        'Restore_RecreateDatabase': 'Rebuilding local database ...',
        'Restore_PreRestoreVerify': 'Verifying remote data ...',
        'Restore_CreateFileList': 'Building list of files to restore ...',
        'Restore_CreateTargetFolders': 'Creating target folders ...',
        'Restore_ScanForExistingFiles': 'Scanning existing files ...',
        'Restore_ScanForLocalBlocks': 'Scanning for local blocks ...',
        'Restore_PatchWithLocalBlocks': 'Patching files with local blocks ...',
        'Restore_DownloadingRemoteFiles': 'Downloading files ...',
        'Restore_PostRestoreVerify': 'Verifying restored files ...',
        'Restore_Complete': 'Finished!',
        'Recreate_Running': 'Recreating database ...',
        'Repair_Running': 'Reparing ...',
        'Verify_Running': 'Verifying ...',
        'BugReport_Running': 'Creating bug report ...',
        'Error': 'Error!'
    };

    this.watch = function(scope, m) {
        scope.$on('serverstatechanged', function() {
            $timeout(function() {
                if (m) m();
                scope.$digest();
            });
        });

        if (m) $timeout(m);
        return state;
    }

    this.resume = function() {
		return AppService.post('/serverstate/resume');
    };

	this.pause = function(duration) {
        return AppService.post('/serverstate/pause' + (duration == null ? '' : '?duration=' + duration));
    };

    this.callWhenTaskCompletes = function(taskid, callback) {
        if (waitingfortask[taskid] == null)
            waitingfortask[taskid] = [];
        waitingfortask[taskid].push(callback);
    };

    var lastTaskId = null;
    $rootScope.$on('serverstatechanged.activeTask', function() {
        var currentTaskId = state.activeTask == null ? null : state.activeTask.Item1;

        if (lastTaskId != null && currentTaskId != lastTaskId && waitingfortask[lastTaskId] != null) {
            for(var i in waitingfortask[lastTaskId])
                waitingfortask[lastTaskId][i]();
            delete waitingfortask[lastTaskId];
        }

        lastTaskId = currentTaskId;
    });

    var progressPollTimer = null;
    var progressPollInProgress = false;
    var progressPollWait = 2000;

    function startUpdateProgressPoll() {
        if (progressPollInProgress)
            return;

        if (state.activeTask == null) {
            if (progressPollTimer != null)
                clearTimeout(progressPollTimer);
            progressPollTimer = null;
            state.lastPgEvent = null;
        } else {
            progressPollInProgress = true;

            if (progressPollTimer != null)
                clearTimeout(progressPollTimer);
            progressPollTimer = null;

            AppService.get('/progressstate').then(
                function(resp) {
                    state.lastPgEvent = resp.data;
                    progressPollInProgress = false;
                    progressPollTimer = setTimeout(startUpdateProgressPoll, progressPollWait);
                },

                function(resp) {
                    progressPollInProgress = false;
                    progressPollTimer = setTimeout(startUpdateProgressPoll, progressPollWait);
                }
            );
        }
    };

    var longPollRetryTimer = null;
    var countdownForForReLongPoll = function(m) {
        if (longPollRetryTimer != null) {
            window.clearInterval(longPollRetryTimer);
            longPollRetryTimer = null;
        }

        var retryAt = new Date(new Date().getTime() + (state.xsfrerror ? 5000 : 15000));
        state.connectionAttemptTimer = new Date() - retryAt;
        $rootScope.$broadcast('serverstatechanged');

        longPollRetryTimer = window.setInterval(function() {
            state.connectionAttemptTimer = retryAt - new Date();
            if (state.connectionAttemptTimer <= 0)
                m();
            else {
                $rootScope.$broadcast('serverstatechanged');
            }

        }, 1000);
    };

    var updatepausetimer = null;
    function pauseTimerUpdater(skipNotify) {
        var prev = state.pauseTimeRemain;

        state.pauseTimeRemain = Math.max(0, AppUtils.parseDate(state.estimatedPauseEnd) - new Date());
        if (state.pauseTimeRemain > 0 && updatepausetimer == null) {
            updatepausetimer = setInterval(pauseTimerUpdater, 500);
        } else if (state.pauseTimeRemain <= 0 && updatepausetimer != null) {
            clearInterval(updatepausetimer);
            updatepausetimer = null;
        }

        if (prev != state.pauseTimeRemain && !skipNotify)
            $rootScope.$broadcast('serverstatechanged.pauseTimeRemain', state.pauseTimeRemain);

        return prev != state.pauseTimeRemain;
    }

    var notifyIfChanged = function (data, dataname, varname) {
        if (state[varname] != data[dataname]) {
            state[varname] = data[dataname];
            $rootScope.$broadcast('serverstatechanged.' + varname, state[varname]);
            return true;
        }

        return false;
    }

    var longpoll = function() {
        if (longPollRetryTimer != null) {
            window.clearInterval(longPollRetryTimer);
            longPollRetryTimer = null;
        }

        if (state.connectionState != 'connected') {
            state.connectionState = 'connecting';
            $rootScope.$broadcast('serverstatechanged');
        }

        var url = '/serverstate/?lasteventid=' + parseInt(state.lastEventId) + '&longpoll=' + (state.lastEventId > 0 ? 'true' : 'false') + '&duration=' + parseInt((longpolltime-1000) / 1000) + 's';
        AppService.get(url, {timeout: state.lastEventId > 0 ? longpolltime : 5000}).then(
            function (response) {
                var oldEventId = state.lastEventId;                
                var anychanged =
                    notifyIfChanged(response.data, 'LastEventID', 'lastEventId') |
                    notifyIfChanged(response.data, 'LastDataUpdateID', 'lastDataUpdateId') |
                    notifyIfChanged(response.data, 'LastNotificationUpdateID', 'lastNotificationUpdateId') |
                    notifyIfChanged(response.data, 'ActiveTask', 'activeTask') |
                    notifyIfChanged(response.data, 'ProgramState', 'programState') |
                    notifyIfChanged(response.data, 'EstimatedPauseEnd', 'estimatedPauseEnd')
                    notifyIfChanged(response.data, 'UpdaterState', 'updaterState') |
                    notifyIfChanged(response.data, 'UpdateReady', 'updateReady') |
                    notifyIfChanged(response.data, 'UpdatedVersion', 'updatedVersion')|
                    notifyIfChanged(response.data, 'UpdateDownloadProgress', 'updateDownloadProgress');


                if (!angular.equals(state.proposedSchedule, response.data.ProposedSchedule)) {
                    state.proposedSchedule.length = 0;
                    state.proposedSchedule.push.apply(state.proposedSchedule, response.data.ProposedSchedule);
                    $rootScope.$broadcast('serverstatechanged.proposedSchedule', state.proposedSchedule);
                    anychanged = true;
                }

                if (!angular.equals(state.schedulerQueueIds, response.data.SchedulerQueueIds)) {
                    state.schedulerQueueIds.length = 0;
                    state.schedulerQueueIds.push.apply(state.schedulerQueueIds, response.data.SchedulerQueueIds);
                    $rootScope.$broadcast('serverstatechanged.schedulerQueueIds', state.schedulerQueueIds);
                    anychanged = true;
                }

                // Clear error indicators
                state.failedConnectionAttempts = 0;
                state.xsfrerror = false;

                if (state.connectionState != 'connected') {
                    state.connectionState = 'connected';
                    $rootScope.$broadcast('serverstatechanged.connectionState', state.connectionState);
                    anychanged = true;

                    // Reload page, server restarted
                    if (oldEventId > state.lastEventId)
                        location.reload(true);
                }

                anychanged |= pauseTimerUpdater(true);

                if (anychanged)
                    $rootScope.$broadcast('serverstatechanged');

                if (state.activeTask != null)
                    startUpdateProgressPoll();


                longpoll();
            },

            function(response) {

                var oldxsfrstate = state.xsfrerror;
                state.failedConnectionAttempts++;
                state.xsfrerror = response.statusText.toLowerCase().indexOf('xsrf') >= 0;

                // First failure, we ignore
                if (state.connectionState == 'connected' && state.failedConnectionAttempts == 1) {

                    // Try again
                    longpoll();
                } else {

                    state.connectionState = 'disconnected';

                    //If we got a new XSRF token this time, quickly retry
                    if (state.xsfrerror && !oldxsfrstate) {
                        longpoll();    
                    } else {
                        // Otherwise, start countdown to next try
                        countdownForForReLongPoll(longpoll);
                    }
                }

                // Notify
                $rootScope.$broadcast('serverstatechanged');

            }
        );
    };

    this.reconnect = longpoll;

    longpoll();
});
