/*
 * Primary app code
 */

// Global app settings

APP_DATA = null;
APP_EVENTS= {};
APP_UTIL = {
    parseBoolOption: function(val, def) {
        if (val == null) {
            if (def === undefined)
                return true;
            else
                return def == true;
        }

        var str = (val + '').toLowerCase();
        return str == '' || str == 'true' || str == '1' || str == 'yes' || str == 'on';
    },

    isValidBoolOption: function(val) {
        if (val == null)
            return true;

        var t = (val + '').toLowerCase();
        return val == '' || t == '1' || t == 'true' || t == 'on' || t == 'yes' || t == '0' || t == 'false' || t == 'off' || t == 'no';
    },

    parseOptionStrings: function(val, dict, validateCallback) {
        dict = dict || {};
        var lines = replace_all(val || '', '\r', '\n').split('\n');
        for(var i in lines) {
            var line = lines[i].trim();
            if (line != '' && line[0] != '#') {
                if (line.indexOf('--') == 0) {
                    line = line.substr(2);
                }

                var eqpos = line.indexOf('=');
                var key = line;
                var value = true;
                if (eqpos > 0) {
                    key = line.substr(0, eqpos).trim();
                    value = line.substr(eqpos + 1).trim();
                    if (value == '')
                        value = true;
                }

                if (validateCallback)
                    if (!validateCallback(dict, key, value))
                        return null;

                dict['--' + key] = value;
            }
        }

        return dict;
    },

    fill_form: function(form, data, map, extra) {
        map = map || {};
        data = data || {};

        for(var k in data) {
            var key = k;
            var m = map[key];
            var v = data[k];

            if (m !== false) {
                if (m && typeof(m) == typeof(''))
                    key = m;

                if (m && typeof(m) == typeof(function() {})) {
                    m(data, key, v, extra);
                } else {
                    var n = form.find('#' + key);
                    if (n.attr('type') == 'checkbox') {
                        n.attr('checked', APP_UTIL.parseBoolOption(v));
                    } else {
                        n.val(v);
                    }

                    n.change();
                }
            }
        }
    },

    read_form: function(form, map, values, extra) {
        values = values || {};
        map = map || {};

        form.find('select').each(function(i, e) {
            var key = e.id;
            var m = map[e.id];

            if (m !== false) {
                if (m && typeof(m) == typeof(function() {})) {
                    m(values, key, e, extra);
                } else {
                    if (m && typeof(m) == typeof(''))
                        key = m;

                    values[key] = $(e).val();
                }
            }
        });

        form.find('textarea').each(function(i, e) {
            var key = e.id;
            var m = map[e.id];

            if (m !== false) {
                if (m && typeof(m) == typeof(function() {})) {
                    m(values, key, e, extra);
                } else {
                    if (m && typeof(m) == typeof(''))
                        key = m;

                    values[key] = $(e).val();
                }
            }
        });

        form.find('input').each(function(i, e) {
            var key = e.id;
            var m = map[e.id];

            if (m !== false) {
                if (m && typeof(m) == typeof(function() {})) {
                    m(values, key, e, extra);
                } else {
                    if (m && typeof(m) == typeof(''))
                        key = m;

                if (e.type == 'checkbox')
                    values[key] = $(e).is(':checked');
                else
                    values[key] = $(e).val();
                }
            }
        });

        return values;
    }
};

$(document).ready(function() {

    // Flag as loaded in case we have plugins
    APP_DATA = {
        plugins: {
            backend: {},
            compression: {},
            encryption: {},
            primary: {}

        }
    };

    var PRIVATE_DATA = {};

    $('#main-appname').text(APP_CONFIG.branded_name);

    window.document.title = APP_CONFIG.branded_name;

    if ((APP_CONFIG.branded_subtitle || '').length > 0) {
        var subdiv = $('<div id="main-appname-subtitle">');
        subdiv.text(APP_CONFIG.branded_subtitle);
        $('#main-appname').append(subdiv);
        $('#main-appname').addClass('has-subtitle');
    }

    $('.button').button();

    $('#main-list-container > div.main-backup-entry').remove();
    $('#loading-dialog').dialog({modal: true}).show();

    jQuery.timeago.settings.allowFuture = true;
    jQuery.timeago.settings.allowPast = true;

    $.noty.defaults.layout = 'bottomCenter';

    // Register a global function for password strength
    $.passwordStrength = function(password, callback) {
        if (callback == null)
            return;

        var onUpdate = function(res) {
            try { callback(res); }
            catch (e) { }
        };

        try { onUpdate(zxcvbn(password)); }
        catch (e) {
            // Not loaded, try this:
            $.getScript('/scripts/zxcvbn.js', function() {
                try {
                    onUpdate(zxcvbn(password));
                }
                catch (e) {
                    onUpdate(null);
                }
            });
        }
    };

    var formatSizes = ['TB', 'GB', 'MB', 'KB'];
    $.formatSizeString = function(val) {
        val = parseInt(val || 0);
        var max = formatSizes.length;
        for(var i = 0; i < formatSizes.length; i++) {
            var m = Math.pow(1024, max - i);
            if (val > m)
                return (val / m).toFixed(2) + ' ' + formatSizes[i];
        }

        return val + ' ' + bytes;
    };

    pwz = function(i, c) {
        i += '';
        while(i.length < c)
            i = '0' + i;
        return i;
    }

    $.toDisplayDateAndTime = function(dt) {
        return pwz(dt.getFullYear(), 4) + '-' + pwz(dt.getMonth() + 1, 2) + '-' + pwz(dt.getDate(), 2) + ' ' + pwz(dt.getHours(), 2) + ':' + pwz(dt.getMinutes(), 2);
    };

    $.parseDate = function(dt) {
        if (typeof(dt) == typeof('')) {
            var msec = Date.parse(dt);
            if (isNaN(msec)) {
                if (dt.length == 16 && dt[8] == 'T' && dt[15] == 'Z') {
                    dt = dt.substr(0, 4) + '-' + dt.substr(4, 2) + '-' + dt.substr(6, 2) + 'T' +
                              dt.substr(9, 2) + ':' + dt.substr(11, 2) + ':' + dt.substr(13, 2) + 'Z';
                }
                return new Date(dt);
            } else {
                return new Date(msec);
            }
        }
        else
            return new Date(dt);
    };

    var serverWithCallback = function(data, callback, errorhandler, refreshMethod) {
        if (typeof(data) == typeof(''))
            data = { action: data };

        var method = 'GET';
        if (data.HTTP_METHOD) {
            method = data.HTTP_METHOD;
            delete data.HTTP_METHOD;
        }

        $.ajax({
            url: APP_CONFIG.server_url,
            type: method,
            dataType: 'json',
            data: data
        })
        .done(function(data) {
            if (refreshMethod)
                refreshMethod(data, true, null);

            if (callback != null)
                callback(data, true, null);
        })
        .fail(function(data, status) {
            var msg = data.statusText;
            if (data && data.responseJSON && data.responseJSON.Message)
                msg = data.responseJSON.Message;

            if (refreshMethod)
                refreshMethod(data, false, msg);

            if (errorhandler)
                errorhandler(data, false, msg);
        });
    };

    PRIVATE_DATA.server_state = {
        polling: false,
        eventId: -1,
        state: null,
        failed: false,
        dataEventId: -1,
        notificationId: -1,

        retryTimer: null,
        pauseUpdateTimer: null,
        activeTask: null,
        scheduled: []
    };

    PRIVATE_DATA.progress_state_text = {
        'Backup_Begin': 'Starting ...',
        'Backup_PreBackupVerify': 'Verifying backend data ...',
        'Backup_PostBackupTest': 'Verifying remote data ...',
        'Backup_PreviousBackupFinalize': 'Completing previous backup ...',
        'Backup_ProcessingFiles': null,
        'Backup_Finalize': 'Completing backup ...',
        'Backup_WaitForUpload': 'Waiting for upload ...',
        'Backup_Delete': 'Deleting unwanted files ...',
        'Backup_Compact': 'Compacting remote data ...',
        'Backup_VerificationUpload': 'Uploadind verification file ...',
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
        'Error': 'Error!'
    };

    PRIVATE_DATA.update_progress_and_schedules = function() {
        var scheduledMap = {};
        var backupMap = {};
        var state = PRIVATE_DATA.server_state;
        var current = state.activeTask == null ? false : 'backup-' + state.activeTask.Item2;

        if (state.activeTask == null) {
            $('#main-status-area-cancel-button').hide();
            $('#main-status-area-progress-outer').hide();

            //TODO: state.scheduled is just the current queue, we need to find the next based on "Time"


            if (PRIVATE_DATA.backup_list == null || PRIVATE_DATA.backup_list.length == 0) {
                $('#main-status-area-text').text('No scheduled backups waiting');
            } else {
                var bk = null;
                var mindate = null;
                for(var n in PRIVATE_DATA.backup_list) {
                    if (PRIVATE_DATA.backup_list[n].Schedule == null || PRIVATE_DATA.backup_list[n].Schedule.Time == null)
                        continue;

                    var selfdate = new Date(PRIVATE_DATA.backup_list[n].Schedule.Time);
                    if (mindate == null || selfdate < mindate) {
                        bk = PRIVATE_DATA.backup_list[n].Backup;
                        mindate = selfdate;
                    }
                }

                if (bk == null) {
                    $('#main-status-area-text').text('No scheduled backups waiting');
                } else {
                    $('#main-status-area-text').text('Waiting to start "' + bk.Name + '" at ' + $.toDisplayDateAndTime(mindate));
                }
            }
        } else {
            $('#main-status-area-cancel-button').show();
            $('#main-status-area-progress-outer').show();

            var id = state.activeTask.Item2;
            var bk = null;

            for(var n in PRIVATE_DATA.backup_list)
                if (PRIVATE_DATA.backup_list[n].Backup.ID == id) {
                    bk = PRIVATE_DATA.backup_list[n].Backup;
                    break;
                }

            if (bk == null)
                $('#main-status-area-text').text('Unknown backup');
            else
                $('#main-status-area-text').text(bk.Name);

            var txt = 'Running ...';
            var pg = -1;
            if (PRIVATE_DATA.server_progress.lastEvent != null) {

                if (PRIVATE_DATA.server_progress.lastEvent.Phase == 'Backup_ProcessingFiles') {

                    if (PRIVATE_DATA.server_progress.lastEvent.StillCounting) {
                        txt = 'Counting (' + PRIVATE_DATA.server_progress.lastEvent.TotalFileCount + ' files found, ' + $.formatSizeString(PRIVATE_DATA.server_progress.lastEvent.TotalFileSize) + ')';
                        pg = 0;
                    } else {
                        var filesleft = PRIVATE_DATA.server_progress.lastEvent.TotalFileCount - PRIVATE_DATA.server_progress.lastEvent. ProcessedFileCount;
                        var sizeleft = PRIVATE_DATA.server_progress.lastEvent.TotalFileSize - PRIVATE_DATA.server_progress.lastEvent.ProcessedFileSize;
                        pg = PRIVATE_DATA.server_progress.lastEvent.ProcessedFileSize / PRIVATE_DATA.server_progress.lastEvent.TotalFileSize;

                        if (PRIVATE_DATA.server_progress.lastEvent.ProcessedFileCount == 0)
                            pg = 0;
                        else if (pg == 1)
                            pg = 0.95;

                        txt = filesleft + ' files (' + $.formatSizeString(sizeleft) + ') to go';
                    }
                } else {
                    txt = PRIVATE_DATA.progress_state_text[PRIVATE_DATA.server_progress.lastEvent.Phase] || txt;
                    if (PRIVATE_DATA.server_progress.lastEvent.Phase == 'Backup_WaitForUpload')
                        pg = 1;

                }

                if (pg == -1) {
                    $('#main-status-area-progress-outer').addClass('backup-progress-indeterminate');
                    $('#main-status-area-progress-bar').css('width', '0%');
                } else {
                    $('#main-status-area-progress-outer').removeClass('backup-progress-indeterminate');
                    $('#main-status-area-progress-bar').css('width', parseInt(pg*100) + '%');
                }
                $('#main-status-area-progress-text').text(txt);

            }
        }

        if (state.scheduled != null)
            for(var n in state.scheduled)
                if (scheduledMap['backup-' + state.scheduled[n].Item2] == null)
                    scheduledMap['backup-' + state.scheduled[n].Item2] = parseInt(n) + 1;

        for(var n in PRIVATE_DATA.backup_list)
            backupMap['backup-' + PRIVATE_DATA.backup_list[n].Backup.ID] = PRIVATE_DATA.backup_list[n];

        $('#main-list').find('.main-backup-entry').each(function(i, e) {
            var el = $(e);

            var lastStarted = $.parseDate(backupMap[e.id].Backup.Metadata.LastBackupStarted);
            var lastError = $.parseDate(backupMap[e.id].Backup.Metadata.LastErrorDate);

            if (isNaN(lastStarted)) {
                el.find('.last-run-time').hide();
            } else {
                el.find('.last-run-time').show();
            }

            if (!isNaN(lastStarted) && !isNaN(lastError) && lastError > lastStarted) {
                //var msg = backupMap[e.id].Backup.Metadata.LastErrorMessage;
                el.find('.last-run-time').addClass('last-run-failed');
            } else {
                el.find('.last-run-time').removeClass('last-run-failed');
            }


            // Scheduled items
            if (scheduledMap[e.id]) {
                el.find('.backup-next-run').text('#' + parseInt(scheduledMap[e.id]) + ' in queue');
                el.find('.next-run-time').show();
            } else if (e.id == current) {
                el.find('.backup-next-run').text('Running now');
                el.find('.next-run-time').show();
            } else if (backupMap[e.id] && backupMap[e.id].Schedule) {
                var targetDate = $.parseDate(backupMap[e.id].Schedule.Time);
                el.find('.backup-next-run').text($.toDisplayDateAndTime(targetDate));
                el.find('.next-run-time').show();
            } else {
                el.find('.next-run-time').hide();
            }
        });
    };

    PRIVATE_DATA.long_poll_for_status = function() {
        var state = PRIVATE_DATA.server_state;
        if (state.polling)
            return;

        if (state.retryTimer != null) {
            $(document).trigger('server-state-connecting');
            clearInterval(state.retryTimer);
            state.retryTimer = null;
        }

        state.polling = true;

        var req = { action: 'get-current-state', longpoll: true, lastEventId: state.eventId };
        var timeout = 240000;
        if (state.failed || state.eventId == -1) {
            req.longpoll = false;
            timeout = 5000
        }

        req.duration = parseInt((timeout-1000) / 1000) + 's';

        $.ajax({
            url: APP_CONFIG.server_url,
            type: 'GET',
            timeout: timeout,
            dataType: 'json',
            data: req
        })
        .done(function(data) {
            state.polling = false;
            state.state = data;
            var oldDataId = state.dataEventId;
            var oldState = state.programState;
            var oldEventId = state.eventId;
            var oldNotificationId = state.notificationId;

            state.eventId = data.LastEventID;
            state.dataEventId = data.LastDataUpdateID;
            state.notificationId = data.LastNotificationUpdateID;
            state.programState = data.ProgramState;
            state.scheduled = data.SchedulerQueueIds || [];

            if (state.failed) {
                state.failed = false;

                // If the server was restarted, we refresh
                if (oldEventId > state.eventId)
                    location.reload(true);

                $(document).trigger('server-state-restored');
            }

            //If there is an active backup, (re)start the progress monitor
            if (data.ActiveTask != null) {
                state.activeTask = data.ActiveTask;
                PRIVATE_DATA.poll_for_progress();
            } else {
                state.activeTask = null;
            }

            $(document).trigger('server-state-updated', data);
            if (oldDataId != state.dataEventId)
                $(document).trigger('server-state-data-updated', data);
            
            if (oldNotificationId != state.notificationId)
                PRIVATE_DATA.refresh_notifications();

            if (oldState != state.programState) {
                if (state.pauseUpdateTimer != null) {
                    clearInterval(state.pauseUpdateTimer);
                    state.pauseUpdateTimer = null;
                }

                $(document).trigger('server-state-changed', state.programState);

                if (state.programState == 'Running') {
                    $(document).trigger('server-state-running');
                } else if (state.programState == 'Paused') {
                    $(document).trigger('server-state-paused');

                    var estimatedEnd = Date.parse(data.EstimatedPauseEnd);
                    if (estimatedEnd > 0) {
                        estimatedEnd = new Date(estimatedEnd);
                        var updateTimer = function() {
                            var left = Math.max(0, estimatedEnd - new Date());
                            $(document).trigger('server-state-pause-countdown', {millisecondsLeft: left});
                            if (left == 0) {
                                clearInterval(state.pauseUpdateTimer);
                                state.pauseUpdateTimer = null;
                            }

                        }

                        state.pauseUpdateTimer = setInterval(updateTimer, 500);
                        updateTimer();
                    }
                }
            }

            PRIVATE_DATA.update_progress_and_schedules();
            PRIVATE_DATA.long_poll_for_status();
        })
        .fail(function(data) {
            if (data.status == 401)
                window.location = '/login.html';

            state.polling = false;
            if (!state.failed) {
                state.failed = true;
                $(document).trigger('server-state-lost');
            }

            state.retryStartTime = new Date();
            var updateTimer = function() {
                var n = new Date() - state.retryStartTime;
                var left = Math.max(0, 15000 - n);
                $(document).trigger('server-state-countdown', {millisecondsLeft: left});
                if (left <= 0)
                    PRIVATE_DATA.long_poll_for_status();
            }

            state.retryTimer = setInterval(updateTimer, 500);
        });
    };

    PRIVATE_DATA.notifications = {
        lastId: -1,
        pending: []
    };

    PRIVATE_DATA.refresh_notifications = function() {
        serverWithCallback({'action': 'get-notifications'},
            function(data) {
                // Append new notifications to pending
                var pendingmap = {};
                for(var n in data)
                    pendingmap[data[n].ID] = data[n];

                var toremove = [];
                for(var n in PRIVATE_DATA.notifications.pending)
                    if (pendingmap[PRIVATE_DATA.notifications.pending[n].ID] == null) {
                        toremove.push(PRIVATE_DATA.notifications.pending[n]);
                    } else {
                        delete pendingmap[PRIVATE_DATA.notifications.pending[n].ID];
                    }

                // Remove missing notifications, and dismiss any shown noty's
                for(var n in toremove) {
                    if (toremove[n].noty)
                        toremove[n].noty.close();

                    for(var n in PRIVATE_DATA.notifications.pending)
                        if (PRIVATE_DATA.notifications.pending[n] == toremove[n]) {
                            PRIVATE_DATA.notifications.pending.splice(n, 1);
                            break;
                        }
                }

                // Append new pending notifications
                for(var n in pendingmap) {
                    PRIVATE_DATA.notifications.pending.push(pendingmap[n]);
                }

                // Setup noty's for new notifications
                for(var n in PRIVATE_DATA.notifications.pending) {
                    if (!PRIVATE_DATA.notifications.pending[n].noty) {
                        var self = PRIVATE_DATA.notifications.pending[n];

                        var buttons = [{
                            text: 'Dismiss',
                            onClick: function($noty) { $noty.close(); }
                        }];

                        if (self.Action == 'backup:show-log') {
                            buttons.push({
                                text: 'Show',
                                onClick: function($noty) { $.showBackupLog(self.BackupID); }
                            });
                        } else if (self.Action == 'update:new') {
                            buttons.push({
                                text: 'Show',
                                onClick: function($noty) { APP_DATA.showChangelog(true); }
                            });

                            buttons.push({
                                text: 'Install',
                                onClick: function($noty) { APP_DATA.installUpdate(); $noty.close(); }
                            });
                        } else if (self.Action != null && self.Action.indexOf('bug-report:created:') == 0) {
                            var id = self.Action.substr('bug-report:created:'.length);
                            buttons.push({
                                text: 'Download',
                                onClick: function($noty) { APP_DATA.downloadBugReport(id); $noty.close(); }
                            });
                        }


                        self.noty = noty({
                            text: self.Title + ': ' + self.Message,
                            type: self.Type.toLowerCase(),
                            callback: {
                                afterClose: function() {
                                    APP_DATA.callServer({'action': 'dismiss-notification', 'id': self.ID});                                    
                                }
                            },
                            buttons: buttons
                        });
                    }
                }

            },
            function(a,b,message) {

            }
        );
    };

    PRIVATE_DATA.server_progress = {
        polling: false,
        eventId: -1,
        throttleTimer: null,
        lastEvent: null,
        updateFreq: 2000
    };

    PRIVATE_DATA.poll_for_progress = function() {
        var state = PRIVATE_DATA.server_progress;
        if (state.polling)
            return;

        if (state.throttleTimer != null) {
            clearTimeout(state.throttleTimer);
            state.throttleTimer = null;
        }

        state.polling = true;

        var req = { action: 'get-progress-state', longpoll: false };
        state.requestStart = new Date();

        var restart_poll = function() {
            if (state.throttleTimer != null) {
                clearTimeout(state.throttleTimer);
                state.throttleTimer = null;
            }

            var timeSinceLast = new Date() - state.requestStart;
            if (timeSinceLast < state.updateFreq) {
                state.throttleTimer = setTimeout(function(){
                    if (PRIVATE_DATA.server_state.activeTask != null)
                        PRIVATE_DATA.poll_for_progress();

                }, Math.max(500, state.updateFreq - timeSinceLast));
            } else {
                if (PRIVATE_DATA.server_state.activeTask != null)
                    PRIVATE_DATA.poll_for_progress();
            }
        };

        $.ajax({
            url: APP_CONFIG.server_url,
            type: 'GET',
            timeout: 5000,
            dataType: 'json',
            data: req
        })
        .done(function(data) {
            state.polling = false;
            state.eventId = data.LastEventID;
            state.lastEvent = data;
            PRIVATE_DATA.update_progress_and_schedules();

            $(document).trigger('server-progress-updated', data);

            restart_poll();
        })
        .fail(function(data, a, b, c, d) {
            state.polling = false;
            if (PRIVATE_DATA.server_state.failed) {
                // We will be restarted on re-connect
            } else {
                // Bad poll request, just retry
                restart_poll();
            }
        });
    };

    PRIVATE_DATA.refresh_server_settings = function(callback, errorhandler) {
        serverWithCallback(
            'system-info',
            callback,
            errorhandler,
            function(data, success) {
                if (success && data != null) {
                    PRIVATE_DATA.server_config = data;
                    window.document.title = APP_CONFIG.branded_name + ' - ' + data.MachineName;
                }
            }
        );
    };

    PRIVATE_DATA.refresh_backup_list = function(callback, errorhandler) {
        serverWithCallback(
            'list-backups',
            callback,
            errorhandler,
            function(data, success) {
                if (success && data != null) {
                    PRIVATE_DATA.backup_list = data;

                    // Clear existing stuff
                    $('#main-list-container > div.main-backup-entry').remove();

                    if ($('#backup-item-template').length > 0 && data.length > 0) {

                        // Pre-processing of data
                        for(var n in data)
                            data[n].Backup.Metadata = data[n].Backup.Metadata || {};

                        if (APP_DATA.plugins.primary['backup-list-preprocess'])
                            APP_DATA.plugins.primary['backup-list-preprocess'](data);

                        //Fill with jQuery template
                        $.tmpl($('#backup-item-template'), data).prependTo($('#main-list-container'));

                        var decodeid = function(e) {
                            var p = e.delegateTarget.id.split('-');
                            return parseInt(p[p.length - 1]);
                        };

                        // Post processing of data
                        for(var n in data) {
                            var id = data[n].Backup.ID;

                            // Setup context menu trigger
                            $('#backup-control-' + id).click(function(e) {
                                var id = decodeid(e);
                                APP_DATA.showContextMenu(id, $('#backup-control-' + id));
                            });
                        }

                        if (APP_DATA.plugins.primary['backup-list-postrocess'])
                            APP_DATA.plugins.primary['backup-list-postrocess']($('#main-list-container'), $('#main-list-container > div.main-backup-entry'), data);
                    }
                }

                PRIVATE_DATA.update_progress_and_schedules();
            }
        );
    };

    APP_DATA.getBackupName = function(id) {
        if (PRIVATE_DATA.backup_list)
            for(var n in PRIVATE_DATA.backup_list)
                if (PRIVATE_DATA.backup_list[n].Backup.ID == id)
                    return PRIVATE_DATA.backup_list[n].Backup.Name;
        return null;
    };

    APP_DATA.getServerConfig = function(callback, errorhandler) {
        if (PRIVATE_DATA.server_config == null) {
            PRIVATE_DATA.refresh_server_settings(callback, errorhandler);
        } else {
            callback(PRIVATE_DATA.server_config);
        }
    };

    APP_DATA.validatePath = function(path, callback) {
        serverWithCallback({ action: 'validate-path', path: path }, callback, callback);
    };
    APP_DATA.getLabels = function(callback) {
        serverWithCallback('list-tags', callback, callback);
    };

    APP_DATA.getBackupDefaults = function(callback, errorhandler) {
        serverWithCallback('get-backup-defaults', callback, errorhandler);
    };
    APP_DATA.getBackupData = function(id, callback, errorhandler) {
        serverWithCallback({ action: 'get-backup', id: id}, callback, errorhandler);
    };

    APP_DATA.addBackup = function(cfg, callback, errorhandler, extra_options) {
        serverWithCallback(
            $.extend(
                {}, 
                extra_options,
                { action: 'add-backup', HTTP_METHOD: 'POST', data: JSON.stringify(cfg)}
            ),
            callback,
            errorhandler
        );
    };

    APP_DATA.updateBackup = function(cfg, callback, errorhandler) {
        serverWithCallback(
            { action: 'update-backup', HTTP_METHOD: 'POST', data: JSON.stringify(cfg)},
            callback,
            errorhandler
        );
    };

    APP_DATA.locateUriDb = function(targeturl, callback, errorhandler) {
        serverWithCallback(
            { action: 'locate-uri-db', HTTP_METHOD: 'POST', uri: targeturl},
            callback,
            errorhandler
        );
    };

    APP_DATA.editNewBackup = function() {
        APP_DATA.editBackup();
    };

    APP_DATA.deleteBackup = function(id, callback, errorhandler) {
        serverWithCallback(
            { action: 'delete-backup', id: id },
            callback,
            errorhandler
        );
    };

    APP_DATA.editBackup = function(id) {
        APP_DATA.getServerConfig(function(data) {

            var callback = function(data) {
                $("#edit-dialog").dialog('open');

                // Bug-fix, this will remove style="width: auto", which breaks Chrome a bit
                $("#edit-dialog").css('width', '');

                // Send data to the dialog
                $("#edit-dialog").trigger('setup-dialog', data.data);
            };

            var errorhandler = function() {
                alert('Failed to get server setup...')
            };

            if (id == undefined || parseInt(id) < 0)
                APP_DATA.getBackupDefaults(callback, errorhandler);
            else
                APP_DATA.getBackupData(id, callback, errorhandler);
        },
        function() {
            alert('Failed to get server setup...')
        });

    };

    APP_DATA.runBackup = function(id) {
        serverWithCallback(
            { action: 'send-command', command: 'run-backup', id: id },
            function() {},
            function(d,s,m) { alert('Failed to start backup: ' + m); }
        );
    };

    APP_DATA.isBackupActive = function(id, callback) {
        serverWithCallback(
            { action: 'send-command', command: 'is-backup-active', id: id },
            function(data) { callback(data.Active); },
            function(d,s,m) { alert('Failed to query backup: ' + m); }
        );
    };

    APP_DATA.restoreBackup = function(id) {
        APP_DATA.isBackupActive(id, function(active) {
            if (active) {
                alert('Cannot start restore while the backup is active.');
            } else {
                $('#restore-dialog').dialog('open');
                $('#restore-dialog').trigger('setup-data', id);
            }
        });

    };

    APP_DATA.pauseServer = function(duration) {
        serverWithCallback({action: 'send-command', command: 'pause', duration: duration});
    };

    APP_DATA.resumeServer = function() {
        serverWithCallback({action: 'send-command', command: 'resume'});
    };

    APP_DATA.stopTask = function(taskId, force) {
        serverWithCallback(
            { action: 'send-command', command: force ? 'abort' : 'stop', taskid: taskId },
            function() {},
            function(d,s,m) { alert('Failed to send stop command: ' + m); }
        );
    }

    APP_DATA.runVerify = function(id) {
        serverWithCallback(
            { action: 'send-command', command: 'run-verify', id: id },
            function() {},
            function(d,s,m) { alert('Failed to start verification: ' + m); }
        );
    };

    APP_DATA.runRepair = function(id) {
        serverWithCallback(
            { action: 'send-command', command: 'run-repair', id: id },
            function() {},
            function(d,s,m) { alert('Failed to start repair: ' + m); }
        );
    };

    APP_DATA.createReport = function(id) {
        serverWithCallback(
            { action: 'send-command', command: 'create-report', id: id },
            function() {},
            function(d,s,m) { alert('Failed to create bug report: ' + m); }
        );
    };

    APP_DATA.deleteLocalData = function(id) {
        serverWithCallback(
            { action: 'delete-local-data', id: id },
            function() {},
            function(d,s,m) { alert('Failed to delete local data: ' + m); }
        );
    };

    APP_DATA.hasLoadedAbout = false;
    APP_DATA.hasLoadedChangelog = false;
    APP_DATA.hasLoadedAcknowledgements = false;

    APP_DATA.showAbout = function() {

        var licenses = {
            'MIT': 'http://www.linfo.org/mitlicense.html',
            'Apache': 'https://www.apache.org/licenses/LICENSE-2.0.html',
            'Apache 2': 'https://www.apache.org/licenses/LICENSE-2.0.html',
            'Apache 2.0': 'https://www.apache.org/licenses/LICENSE-2.0.html',
            'Public Domain': 'https://creativecommons.org/licenses/publicdomain/',
            'GPL': 'https://www.gnu.org/copyleft/gpl.html',
            'LGPL': 'https://www.gnu.org/copyleft/lgpl.html',
            'MS-PL': 'http://opensource.org/licenses/MS-PL',
            'Microsoft Public': 'http://opensource.org/licenses/MS-PL',
            'New BSD': 'http://opensource.org/licenses/BSD-3-Clause'
        };

        if (!APP_DATA.hasLoadedAcknowledgements) {
            serverWithCallback(
                { action: 'get-acknowledgements' },
                function(data) {
                    $('#about-dialog-tab-general-acknowledgements').empty();
                    $('#about-dialog-tab-general-acknowledgements').html(nl2br(replace_all(data.Acknowledgements, '  ', '&nbsp;&nbsp;')));
                    APP_DATA.hasLoadedAcknowledgements = true;
                }, function(a,b,message) {
                    alert('Failed to load acknowledgements: ' + message);
                }
            );
            
            
        }

        if (!APP_DATA.hasLoadedChangelog) {
            serverWithCallback(
                { action: 'get-changelog', 'from-update': 'false' },
                function(data) {
                    $('#about-dialog-tab-changelog').empty();
                    $('#about-dialog-tab-changelog').html(nl2br(replace_all(data.Changelog, '  ', '&nbsp;&nbsp;')));
                    APP_DATA.hasLoadedChangelog = true;
                }, function(a,b,message) {
                    alert('Failed to load changelog: ' + message);
                }
            );
        }

        if (!APP_DATA.hasLoadedAbout) {
            serverWithCallback('get-license-data',
                function(data) {
                    var d = [];
                    for(var n in data) {
                        try {
                            var r = JSON.parse(data[n].Jsondata);
                            if (r != null) {
                                r.licenselink = r.licenselink || licenses[r.license] || '#';
                                d.push(r);
                            }
                        } catch (e) {}
                    }

                    $('#about-dialog-current-version').text(PRIVATE_DATA.server_config.ServerVersionName);

                    $('#about-dialog-tab-thirdparty-list').empty();
                    $('#about-dialog-tab-thirdparty-list').append($.tmpl($('#about-dialog-template'), d));
                    APP_DATA.hasLoadedAbout = true;
                    APP_DATA.showAbout();
                }, function(msg) {
                    alert('Failed to load data: ' + msg);
                });            
        } else {
            $('#about-dialog').dialog('open');
        }
    };

    APP_DATA.checkForUpdates = function() {
        serverWithCallback(
            { action: 'send-command', command: 'check-update' },
            function() {},
            function(d,s,m) { alert('Failed to check for updates: ' + m); }
        );
    };

    APP_DATA.installUpdate = function() {            
        serverWithCallback(
            { action: 'send-command', command: 'install-update' },
            function() {
                // Remove notifications for updates when we install
                for(var n in PRIVATE_DATA.notifications.pending)
                    if (PRIVATE_DATA.notifications.pending[n].Action == 'backup:new' && PRIVATE_DATA.notifications.pending[n].noty)
                        PRIVATE_DATA.notifications.pending[n].noty.close();
            },
            function(d,s,m) { alert('Failed to install for update: ' + m); }
        );
    };

    APP_DATA.activateUpdate = function() {
        serverWithCallback(
            { action: 'send-command', command: 'activate-update' },
            function() {},
            function(d,s,m) { alert('Failed to activate update: ' + m); }
        );
    };

    APP_DATA.showChangelog = function(from_update) {
        serverWithCallback(
            { action: 'get-changelog', 'from-update': from_update ? 'true' : '' },
            function(data) {
                var dlg = $('<div></div>').attr('title', 'Changelog');

                var pgtxt = $('<div class="change-log-data"></div>');
                pgtxt.html(nl2br(replace_all(data.Changelog, '  ', '&nbsp;&nbsp;')));
                dlg.append(pgtxt);  

                dlg.dialog({
                    autoOpen: true,
                    width: $('body').width > 450 ? 430 : 600,
                    height: 500, 
                    modal: true,
                    closeOnEscape: true,
                    buttons: [
                        { text: 'Close', click: function(event, ui) {
                            dlg.dialog('close');
                            dlg.remove();
                        }}
                    ]
                });

            },
            function(d,s,m) { alert('Failed to get changelog: ' + m); }
        );      
    };

    APP_DATA.downloadBugReport = function(id) {
        var sendobj = {
            'action': 'download-bug-report', 
            'id': id
        };

        var completed = false;
        var url = APP_CONFIG.server_url;
        url += (url.indexOf('?') > -1) ? '&' : '?';
        url += $.param(sendobj);

        var iframe = $('<iframe>')
                    .hide()
                    .prop('src', url)
                    .appendTo('body');                

        setTimeout(function() { iframe.remove(); }, 5000);
    };

    APP_DATA.callServer = serverWithCallback;

    $.showPopupMenu = function(menu, anchor, offset) {
        var pos = anchor.offset();
        var menuwidth = menu.outerWidth();
        var menuheight = menu.outerHeight();

        offset = offset || {};
        offset.x = offset.x || 0;
        offset.y = offset.y || 0;

        menu.toggle();

        var left = pos.left + offset.x;
        var top = pos.top + offset.y;
        if (left + menuwidth > $(document).outerWidth())
            left -= menuwidth;
        if (top + menuheight > $(document).outerHeight())
            top -= menuheight;

        if (menu.is(':visible')) {
            menu.css({
                position: 'absolute',
                top: top + 'px',
                left: left + 'px'
            });
            $('#click-intercept').show();
        }
    };

    $('#main-settings').click(function() {
        var pos = $('#main-settings').offset();
        var buttonwidth = $('#main-settings').outerWidth();
        $.showPopupMenu($('#main-control-menu'), $('#main-settings'), {y: $('#main-topbar').outerHeight() - pos.top });

        $('#main-control-menu').css({ left: "", right: ($(document).outerWidth() - (pos.left + buttonwidth)) + 'px' });
    });

    $('#click-intercept').click(function() {
        $('#click-intercept').hide();
        $('.menu').hide();
    });

    $('#main-donate').click(function() {

    });

    $('#main-control').click(function() {
    });

    $('#main-newbackup').click(function() {
        APP_DATA.editNewBackup();

    });

    $('#edit-dialog').tabs({ active: 0 });
    $('#edit-dialog').dialog({
        minWidth: 320,
        width: $('body').width > 600 ? 320 : 600,
        minHeight: 480,
        height: 500,
        modal: true,
        autoOpen: false,
        closeOnEscape: true,
        buttons: [
            { text: '< Previous', disabled: true, click: function(event, ui) {
                var cur = parseInt($('#edit-dialog').tabs( "option", "active"));
                cur = Math.max(cur-1, 0);
                $('#edit-dialog').tabs( "option", "active", cur);
            }},
            { text: 'Next >', click: function(event, ui) {
                var cur = parseInt($('#edit-dialog').tabs( "option", "active"));
                var max = $('#edit-dialog').parent().find('[role=tablist] > li').size() - 1;

                if (!EDIT_BACKUP.validate_tab(cur))
                    return;

                event.curPage = cur;
                event.currentTarget.curPage = cur;
                cur = Math.min(cur+1, max);
                $('#edit-dialog').tabs( "option", "active", cur);
            }}
        ]
    });

    $(document).on('server-state-updated', function() {
        $('#loading-dialog').dialog("close");
    });

    $(document).on('server-state-lost', function() {
        $('#connection-lost-dialog').dialog('open');
        $('#connection-lost-dialog-text').text('Server connection lost...');
        var button = $('#connection-lost-dialog').parent().find('.ui-dialog-buttonpane').find('.ui-button').first();
        button.attr("disabled",false).removeClass( 'ui-button-disabled ui-state-disabled' );

    });

    $(document).on('server-state-restored', function() {
        $('#connection-lost-dialog').dialog('close');
    });

    $(document).on('server-state-countdown', function(e, f) {
        var s = parseInt(f.millisecondsLeft / 1000) + 1;
        $('#connection-lost-dialog-text').html('Server connection lost, <br/>retry in ' + s + ' seconds');
        var button = $('#connection-lost-dialog').parent().find('.ui-dialog-buttonpane').find('.ui-button').first();
        button.attr("disabled",false).removeClass( 'ui-button-disabled ui-state-disabled' );
    });

    $(document).on('server-state-connecting', function(e) {
         $('#connection-lost-dialog-text').text('Retrying...');
        var button = $('#connection-lost-dialog').parent().find('.ui-dialog-buttonpane').find('.ui-button').first();
        button.attr("disabled",true).addClass( 'ui-button-disabled ui-state-disabled' );
    });

    $(document).on('server-state-running', function() {
        $('#main-control').removeClass('main-icon-run').addClass('main-icon-pause');
    });

    $(document).on('server-state-paused', function() {
        $('#main-control').removeClass('main-icon-pause').addClass('main-icon-run');
    });

    $(document).on('server-state-pause-countdown', function(e, f) {
        var s = parseInt(f.millisecondsLeft / 1000) + 1;
    });

    $('#main-control').click(function() {
        if ($('#main-control').hasClass('main-icon-run'))
            APP_DATA.resumeServer();
        else
            APP_DATA.pauseServer();
    });

    $('#connection-lost-dialog').dialog({
        modal: true,
        autoOpen: false,
        closeOnEscape: false,
        buttons: [
            {
                text: 'Retry now',
                disabled: true,
                click: function(event, ui) {
                    PRIVATE_DATA.long_poll_for_status();
                }
            }
        ]
    });

    $('#connection-lost-dialog').parent().find('.ui-dialog-titlebar-close').remove().first().remove();

    $('#connection-lost-dialog').on('dialogbeforeclose', function( event, ui ) {
        return !PRIVATE_DATA.server_state.failed;
    });


    $(document).on('server-state-data-updated', function() {
        PRIVATE_DATA.refresh_backup_list();
    });

    PRIVATE_DATA.long_poll_for_status();
    PRIVATE_DATA.refresh_server_settings();
    PRIVATE_DATA.refresh_backup_list();

    $('#main-control-menu').menu();
    $('#main-control-menu').removeClass('ui-widget-content');
    $('#main-control-menu-pause-submenu').removeClass('ui-widget-content');
    $('#main-control-menu-settings').hide().next().hide();
    $('#main-control-menu-throttle').hide();

    $('#main-control-menu-about').click(function() { APP_DATA.showAbout(); })

    $('#main-control-menu-pause-submenu-5m').click(function() { APP_DATA.pauseServer('5m'); });
    $('#main-control-menu-pause-submenu-10m').click(function() { APP_DATA.pauseServer('10m'); });
    $('#main-control-menu-pause-submenu-15m').click(function() { APP_DATA.pauseServer('15m'); });
    $('#main-control-menu-pause-submenu-30m').click(function() { APP_DATA.pauseServer('30m'); });
    $('#main-control-menu-pause-submenu-1h').click(function() { APP_DATA.pauseServer('1h'); });

    $('#main-control-menu-import').click(function() { $('#import-dialog').dialog('open'); });

    var updaterState = {
        state: 'Waiting',
        version: null,
        installed: false,
        restarted: false,
        simplestate: null,
        noty: null,
        noty_type: null,

        closeNoty: function() {
            if (this.noty != null) {
                this.noty.close();
                this.noty = null;
                this.noty_type = null;
            }
        },
        checkingNoty: function() {
            this.closeNoty();
            this.noty = noty({
                text: 'Checking for updates ...',
            });
            this.noty_type = 'checking';
        },
        downloadingNoty: function() {
            this.closeNoty();
            var self = this;
            this.noty = noty({
                text: 'Downloading update ' + self.version,
            });
            this.noty_type = 'downloading';
        },
        setDownloadProgress: function(pg) {
            if (this.noty_type == 'downloading' && this.noty != null)
                this.noty.setText('Downloading update ' + this.version + ' (' + parseInt(pg*100) + '%)');

        },
        foundupdateNoty: function() {
            this.closeNoty();
        },
        updateinstalledNoty: function() {
            this.closeNoty();
            var self = this;
            this.noty = noty({
                text: 'Update <div class="noty-update-changelog-link">' + self.version + '</div> installed',
                type: 'information',
                buttons: [{
                    text: 'Not now',
                    onClick: function() {
                        self.closeNoty();
                    }
                },{
                    text: 'Restart',
                    onClick: function() {
                        self.activateUpdate();
                        self.closeNoty();
                    }
                }]
            });
            this.noty_type = 'installed';
            $('.noty-update-changelog-link').click(function() { APP_DATA.showChangelog(true); });            
        },
        activateUpdate: function() {
            if (confirm('Restart ' + APP_CONFIG.branded_name + ' and activate update?'))
            {
                APP_DATA.activateUpdate();
                updaterState.restarted = true;
            }
        },
        noUpdatesNoty: function() {
            noty({
                text: 'No updates found',
                timeout: 5000
            });
        }
    };

    $(document).on('server-state-updated', function(eventargs, data) {
        var prevstate = updaterState.simplestate;

        updaterState.state = data.UpdaterState;
        updaterState.version = data.UpdatedVersion;
        updaterState.installed = data.UpdateReady;

        if (updaterState.restarted && updaterState.version == null)
            location.reload(true);

        if (updaterState.state == 'Waiting') {
            if (updaterState.version == null)
                updaterState.simplestate = null;
            else if (!updaterState.installed) 
                updaterState.simplestate = 'found';
            else if (updaterState.installed)
                updaterState.simplestate = 'installed';
            else
                updaterState.simplestate = null;

        } else if (updaterState.state == 'Checking') {
            updaterState.simplestate = 'check';
        } else if (updaterState.state == 'Downloading') {
            updaterState.simplestate = 'download';
        } else {
            updaterState.simplestate = null;
        }

        if (updaterState.simplestate == null) {
            $('#main-control-menu-updates > a').text('Check for updates');
            $('#main-control-menu-updates').removeClass('ui-state-disabled');
            $('#main-control-menu-check-updates').hide();
        } else if (updaterState.simplestate == 'found') {
            $('#main-control-menu-updates > a').text('Install update');
            $('#main-control-menu-updates').removeClass('ui-state-disabled');
            $('#main-control-menu-check-updates').show();
        } else if (updaterState.simplestate == 'installed') {
            $('#main-control-menu-updates > a').text('Restart with update');
            $('#main-control-menu-updates').removeClass('ui-state-disabled');
            $('#main-control-menu-check-updates').hide();
        } else if (updaterState.simplestate == 'check') {
            $('#main-control-menu-updates > a').text('Checking for update ...');
            $('#main-control-menu-updates').addClass('ui-state-disabled');
            $('#main-control-menu-check-updates').hide();
        } else if (updaterState.simplestate == 'download') {
            $('#main-control-menu-updates > a').text('Downloading update ...');
            $('#main-control-menu-updates').addClass('ui-state-disabled');
            $('#main-control-menu-check-updates').hide();
        } else {
            $('#main-control-menu-updates > a').text('Unknown state ...');
            $('#main-control-menu-updates').addClass('ui-state-disabled');
            $('#main-control-menu-check-updates').show();
        }

        if (updaterState.simplestate != prevstate) {
            if (updaterState.simplestate == 'found')
                updaterState.foundupdateNoty();
            else if (updaterState.simplestate == 'installed')
                updaterState.updateinstalledNoty();                
            else if (updaterState.simplestate == 'check')
                updaterState.checkingNoty();                
            else if (updaterState.simplestate == 'download')
                updaterState.downloadingNoty();
            else
            {
                updaterState.closeNoty();
                if (prevstate == 'check')
                    updaterState.noUpdatesNoty();
            }
        } else if (updaterState.simplestate == 'download') {
            updaterState.setDownloadProgress(data.UpdateDownloadProgress);
        }

    });

    $('#main-control-menu-check-updates').click(function() {
        if (updaterState.state != 'Waiting')
            return;

        APP_DATA.checkForUpdates();
    });

    $('#main-control-menu-updates').click(function() {
        if (updaterState.state != 'Waiting')
            return;

        if (updaterState.version == null) {
            APP_DATA.checkForUpdates();
        } else if (!updaterState.installed) {
            APP_DATA.checkForUpdates();
            APP_DATA.installUpdate();
        } else if (updaterState.installed) {
            updaterState.activateUpdate();
        }
    });

    $('#main-control-menu-log').click(function() { $.showAppLog(); });

    $('#main-control-menu').find('li').click(function() {  $('#click-intercept').trigger('click'); });

    var pausenoty = {
        n: null,
        prevstate: null
    };

    $(document).on('server-state-changed', function(e, state) {
        if (pausenoty.prevstate != state && pausenoty.prevstate != null) {
            if (pausenoty.n != null)
                pausenoty.n.close();

            if (state == 'Running') {
                pausenoty.n = noty({
                    text: 'Server is now resumed',
                    timeout: 5000
                });
            } else {
                pausenoty.n = noty({
                    text: 'Server is paused',
                    buttons: [{
                        text: 'Resume',
                        onClick: function() {
                            APP_DATA.resumeServer();
                        }
                    }]
                });
            }
        }

        pausenoty.prevstate = state;
    });

    $(document).on('server-state-pause-countdown', function(e, data) {
        if (pausenoty.prevstate != 'Paused' || pausenoty.n == null) {
            if (pausenoty.n != null)
                pausenoty.n.close();
            pausenoty.n = noty({
                text: 'Server is paused',
                buttons: [{
                    text: 'Resume',
                    onClick: function() {
                        APP_DATA.resumeServer();
                    }
                }]
            });
        }
        var seconds = parseInt(data.millisecondsLeft / 1000);
        var hours = parseInt(seconds / 3600);
        seconds -= (hours * 3600);
        var minutes = parseInt(seconds / 60)
        seconds -= (minutes * 60);
        seconds = seconds + '';
        if (seconds.length == 1)
            seconds = '0' + seconds;

        var timestr = '';
        if (hours > 0)
            timestr += hours + 'h ';

        timestr += minutes + ':' + seconds;

        pausenoty.n.setText('Server is paused, resuming in ' + timestr);
    });

    $('#about-dialog').tabs({ active: 0 });
    $("#about-dialog").dialog({
        minWidth: 320,
        width: $('body').width > 600 ? 320 : 600,
        minHeight: 480,
        height: 500,
        modal: true,
        autoOpen: false,
        closeOnEscape: true,
        buttons: [
            { text: 'Close', disabled: false, click: function(event, ui) {
                $('#about-dialog').dialog('close');
            }}
        ]
    });

    setInterval(function() {
        $('#main-list').find('.backup-last-run').each(function(i, e) {
          $(e).text($.timeago($.parseDate($(e).attr('alt'))));
        })
    }, 60*1000);

    APP_DATA.getCurrentBackupId = function() {
        return PRIVATE_DATA.server_state.activeTask == null ? null : PRIVATE_DATA.server_state.activeTask.Item2;
    };

    APP_DATA.getCurrentTaskId = function() {
        return PRIVATE_DATA.server_state.activeTask == null ? null : PRIVATE_DATA.server_state.activeTask.Item1;
    };

    APP_DATA.contextMenuId = null;
    APP_DATA.showContextMenu = function(id, anchor) {
        APP_DATA.contextMenuId = id;
        $.showPopupMenu($('#backup-context-menu'), anchor, {x: $(anchor).outerWidth() + 10});
    };

    $('#backup-details-run').click(function(e) {
        APP_DATA.runBackup(APP_DATA.contextMenuId);
    });

    $('#backup-details-restore').click(function(e) {
        APP_DATA.restoreBackup(APP_DATA.contextMenuId);
    });

    $('#backup-details-edit').click(function(e) {
        APP_DATA.editBackup(APP_DATA.contextMenuId);
    });

    $('#backup-details-delete').click(function(e) {
        var name = APP_DATA.getBackupName(APP_DATA.contextMenuId);

        if (name && confirm('Really delete ' + name + '?'))
            APP_DATA.deleteBackup(APP_DATA.contextMenuId);
    });

    $('#backup-details-show-log').click(function(e) {
        $.showBackupLog(APP_DATA.contextMenuId);
    });

    $('#backup-details-verify').click(function(e) {
        APP_DATA.runVerify(APP_DATA.contextMenuId);
    });

    $('#backup-details-send-report').click(function(e) {
        APP_DATA.createReport(APP_DATA.contextMenuId);
    });

    $('#backup-details-repair').click(function(e) {
        APP_DATA.runRepair(APP_DATA.contextMenuId);
    });

    $('#backup-details-export').click(function(e) {
        $('#export-dialog').dialog('open');
        $('#export-dialog').data('backupid', APP_DATA.contextMenuId);
    });

    $('#backup-details-copy').click(function(e) {
        alert('Function is not implemented yet');
    });

    $('#backup-details-delete-local').click(function(e) {
        if (confirm('Do you really want to delete the local database?'))
            APP_DATA.deleteLocalData(APP_DATA.contextMenuId);
    });

    $('#backup-details-delete-remote').click(function(e) {
        alert('Function is not implemented yet');
    });

    $('#backup-context-menu').menu().removeClass('ui-widget-content');
    $('#backup-context-menu').find('li').click(function() {  $('#click-intercept').trigger('click'); });


    var cancelnoty = {
        n: null,
        taskId: null,
        onRequest: function() {
            var current = APP_DATA.getCurrentTaskId();

            if (current == null)
                return;

            // Seems too harsh to use the double-stop = force logic
            /*if (this.cancel_task_id == current) {
                APP_DATA.stopTask(current, true);
            } else*/ {
                APP_DATA.stopTask(current, false);
                if (this.cancel_task_id != current) {
                    this.cancel_task_id = current;

                    if (this.n != null) {
                        this.n.close();
                        this.n = null;
                    }

                    this.n = noty({
                            text: 'Task stopped, waiting for task to clean up',
                            buttons: [{
                                text: 'Force stop',
                                onClick: function() {
                                    APP_DATA.stopTask(current, true);
                                }
                            }]
                        });
                }
            }
        },
        onUpdate: function() {
            if (this.n == null)
                return;

            var current = APP_DATA.getCurrentTaskId();
            if (current != this.cancel_task_id) {
                this.n.close();
                this.n = null;
                this.cancel_task_id = null;
            }

        }
    };


    $(document).on('server-state-updated', function(e, data) {
        cancelnoty.onUpdate();
    });

    $(document).on('server-progress-updated', function(e, data) {
        cancelnoty.onUpdate();
    });

    $('#main-status-area-cancel-button').click(function() {
        var dlg = $('<div></div>').attr('title', 'Warning');
        dlg.dialog({
            autoOpen: true,
            width: $('body').width > 450 ? 430 : 600,
             modal: true,
            closeOnEscape: true,
            buttons: [
                { text: 'Stop now', click: function(event, ui) {
                    var current = APP_DATA.getCurrentTaskId();
                    if (current != null)
                        APP_DATA.stopTask(current, true);

                    dlg.dialog('close');
                    dlg.remove();
                }},
                { text: 'Stop after upload', click: function(event, ui) {
                    cancelnoty.onRequest();
                    dlg.dialog('close');
                    dlg.remove();
                }},
                { text: 'Cancel', click: function(event, ui) {
                    dlg.dialog('close');
                    dlg.remove();
                }}
            ]
        });

        var pgtxt = $('<div></div>');
        pgtxt.text('Stop the current task?');
        dlg.append(pgtxt);

    });

    $('#import-dialog').dialog({
        autoOpen: false,
        width: $('body').width > 320 ? 320 : 600,
        height: 250, 
        modal: true,
        closeOnEscape: true,
        buttons: [
            { text: 'Cancel', click: function(event, ui) {
                $(this).dialog('close');
            }},
            { text: 'Import', click: function(event, ui) {
                var uid = 'submit-frm-' + Math.random();
                var callback = 'callback-' + Math.random();
                var completed = false;

                var iframe = $('<iframe>')
                                .hide()
                                .prop('id', uid)
                                .prop('name', uid)
                                .appendTo('body');

                window[callback] = function(message) {
                    completed = true;
                    if (message == 'OK')
                        $('#import-dialog').dialog('close');
                    else
                        alert(message);
                };

                $('#import-dialog-form')
                    .attr('action', APP_CONFIG.server_url)
                    .attr('target', uid);

                $('#import-dialog-callback').val(callback);

                $('#import-dialog-form').submit();

                setTimeout(function() { 
                    if (!completed)
                        alert('Import failed, see the log for details');
                    delete window[callback]; 
                    iframe.remove(); 
                }, 5000);
            }}
        ]
    });
    $('#import-dialog').on('dialogopen', function() {
        $('#import-dialog-form').each(function(i,e) { 
            e.reset(); 
        });
    });


    $('#export-dialog').dialog({
        autoOpen: false,
        width: $('body').width > 320 ? 320 : 600,
        height: 250, 
        modal: true,
        closeOnEscape: true,
        buttons: [
            { text: 'Cancel', click: function(event, ui) {
                $(this).dialog('close');
            }},
            { text: 'Export', click: function(event, ui) {

                var dlgself = this;
                if ($('#export-use-encryption').is(':checked') && $('#export-encryption-password').val().trim() == '') {
                    alert('Empty passphrase not allowed, de-select encryption or enter a passphrase');
                    return;
                }

                var sendobj = {
                    'action': 'export-backup', 
                    'id': $('#export-dialog').data('backupid'),
                    'cmdline': $('#export-type-commandline').is(':checked'),
                    'passphrase': $('#export-encryption-password').val()
                };

                if ($('#export-type-file').is(':checked')) {

                    var completed = false;
                    var url = APP_CONFIG.server_url;
                    url += (url.indexOf('?') > -1) ? '&' : '?';
                    url += $.param(sendobj);

                    var iframe = $('<iframe>')
                                .hide()
                                .prop('src', url)
                                .appendTo('body');                

                    setTimeout(function() { iframe.remove(); }, 5000);

                    $(dlgself).dialog('close');

                } else {
                    APP_DATA.callServer(sendobj, 
                    function(data) {
                        var dlg = $('<div></div>').attr('title', 'Commandline');
                        dlg.dialog({
                            autoOpen: true,
                            width: $('body').width > 450 ? 430 : 600,
                            modal: true,
                            closeOnEscape: true,
                            buttons: [
                                { text: 'Close', click: function(event, ui) {
                                    dlg.dialog('close');
                                    dlg.remove();
                                }}
                            ]
                        });

                        var pgtxt = $('<div></div>');
                        pgtxt.text(data.Command);
                        dlg.append(pgtxt);

                        $(dlgself).dialog('close');
                    }, 
                    function(data, succes, status) {
                        alert('Could not export: ' + status);                    
                    });
                }


            }}
        ]
    });

    var exportTypeChange = function() {
        $('#export-use-encryption').attr('disabled', !$('#export-type-file').is(':checked'));
        $('#export-use-encryption').change();
    };

    $('#export-dialog').on('dialogopen', function() {
        $('#export-type-commandline').attr('checked', true);
        $('#export-use-encryption').attr('checked', false);        
        $('#export-encryption-password').val('');
        exportTypeChange();
    });

    $('#export-type-commandline').change(exportTypeChange);
    $('#export-type-file').change(exportTypeChange);
    $('#export-use-encryption').change(function() {
        $('#export-encryption-password').attr('disabled', !($('#export-use-encryption').is(':checked') && $('#export-type-file').is(':checked')));
    });

});