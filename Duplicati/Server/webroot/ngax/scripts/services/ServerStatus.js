backupApp.service('ServerStatus', function($rootScope, $timeout, AppService, AppUtils, gettextCatalog) {

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
    var self = this;

    function reloadTexts() {
        self.progress_state_text = {
            'Backup_Begin': gettextCatalog.getString('Starting backup …'),
            'Backup_PreBackupVerify': gettextCatalog.getString('Verifying backend data …'),
            'Backup_PostBackupTest': gettextCatalog.getString('Verifying remote data …'),
            'Backup_PreviousBackupFinalize': gettextCatalog.getString('Completing previous backup …'),
            'Backup_ProcessingFiles': null,
            'Backup_Finalize': gettextCatalog.getString('Completing backup …'),
            'Backup_WaitForUpload': gettextCatalog.getString('Waiting for upload to finish …'),
            'Backup_Delete': gettextCatalog.getString('Deleting unwanted files …'),
            'Backup_Compact': gettextCatalog.getString('Compacting remote data ...'),
            'Backup_VerificationUpload': gettextCatalog.getString('Uploading verification file …'),
            'Backup_PostBackupVerify': gettextCatalog.getString('Verifying backend data …'),
            'Backup_Complete': gettextCatalog.getString('Backup complete!'),
            'Restore_Begin': gettextCatalog.getString('Starting restore …'),
            'Restore_RecreateDatabase': gettextCatalog.getString('Rebuilding local database …'),
            'Restore_PreRestoreVerify': gettextCatalog.getString('Verifying remote data …'),
            'Restore_CreateFileList': gettextCatalog.getString('Building list of files to restore …'),
            'Restore_CreateTargetFolders': gettextCatalog.getString('Creating target folders …'),
            'Restore_ScanForExistingFiles': gettextCatalog.getString('Scanning existing files …'),
            'Restore_ScanForLocalBlocks': gettextCatalog.getString('Scanning for local blocks …'),
            'Restore_PatchWithLocalBlocks': gettextCatalog.getString('Patching files with local blocks …'),
            'Restore_DownloadingRemoteFiles': gettextCatalog.getString('Downloading files …'),
            'Restore_PostRestoreVerify': gettextCatalog.getString('Verifying restored files …'),
            'Restore_Complete': gettextCatalog.getString('Restore complete!'),
            'Recreate_Running': gettextCatalog.getString('Recreating database …'),
            'Vacuum_Running': gettextCatalog.getString('Vacuuming database …'),
            'Repair_Running': gettextCatalog.getString('Repairing database …'),
            'Verify_Running': gettextCatalog.getString('Verifying files …'),
            'BugReport_Running': gettextCatalog.getString('Creating bug report …'),
            'Delete_Listing': gettextCatalog.getString('Listing remote files …'),
            'Delete_Deleting': gettextCatalog.getString('Deleting remote files …'),
            'PurgeFiles_Begin,': gettextCatalog.getString('Listing remote files for purge …'),
            'PurgeFiles_Process,': gettextCatalog.getString('Purging files …'),
            'PurgeFiles_Compact,': gettextCatalog.getString('Compacting remote data …'),
            'PurgeFiles_Complete,': gettextCatalog.getString('Purging files complete!'),
            'Error': gettextCatalog.getString('Error!')
        };
    };

    reloadTexts();
    $rootScope.$on('gettextLanguageChanged', reloadTexts);

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

    var longpoll = function(fastcall) {
        if (longPollRetryTimer != null) {
            window.clearInterval(longPollRetryTimer);
            longPollRetryTimer = null;
        }

        if (state.connectionState != 'connected') {
            state.connectionState = 'connecting';
            $rootScope.$broadcast('serverstatechanged');
        }

        var url = '/serverstate/?lasteventid=' + parseInt(state.lastEventId) + '&longpoll=' + (((!fastcall) && (state.lastEventId > 0)) ? 'true' : 'false') + '&duration=' + parseInt((longpolltime-1000) / 1000) + 's';
        AppService.get(url, {timeout: state.lastEventId > 0 ? longpolltime : 5000}).then(
            function (response) {
                var oldEventId = state.lastEventId;
                var anychanged =
                    notifyIfChanged(response.data, 'LastEventID', 'lastEventId') |
                    notifyIfChanged(response.data, 'LastDataUpdateID', 'lastDataUpdateId') |
                    notifyIfChanged(response.data, 'LastNotificationUpdateID', 'lastNotificationUpdateId') |
                    notifyIfChanged(response.data, 'ActiveTask', 'activeTask') |
                    notifyIfChanged(response.data, 'ProgramState', 'programState') |
                    notifyIfChanged(response.data, 'EstimatedPauseEnd', 'estimatedPauseEnd') |
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


                longpoll(false);
            },

            function(response) {

                var oldxsfrstate = state.xsfrerror;
                state.failedConnectionAttempts++;
                state.xsfrerror = response.statusText.toLowerCase().indexOf('xsrf') >= 0;

                // First failure, we ignore
                if (state.connectionState == 'connected' && state.failedConnectionAttempts == 1) {

                    // Try again
                    longpoll(true);
                } else {

                    state.connectionState = 'disconnected';

                    //If we got a new XSRF token this time, quickly retry
                    if (state.xsfrerror && !oldxsfrstate) {
                        longpoll(true);
                    } else {
                        // Otherwise, start countdown to next try
                        countdownForForReLongPoll(function() { longpoll(true); });
                    }
                }

                // Notify
                $rootScope.$broadcast('serverstatechanged');

            }
        );
    };

    this.reconnect = function() { longpoll(true); };

    longpoll(true);
});
