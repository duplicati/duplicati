backupApp.service('ServerStatus', function($http, $rootScope, $timeout, AppService) {

    var polltime = 5 * 60 * 1000;

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
        connectionAttemptTimer: 0
    };

    this.state = state;

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

    var retryTimer = null;

    var countdownForForRePoll = function(m) {
        if (retryTimer != null) {
            window.clearInterval(retryTimer);
            retryTimer = null;
        }

        var retryAt = new Date(new Date().getTime() + (state.xsfrerror ? 5000 : 15000));
        state.connectionAttemptTimer = new Date() - retryAt;
        $rootScope.$broadcast('serverstatechanged');

        retryTimer = window.setInterval(function() {
            state.connectionAttemptTimer = retryAt - new Date();
            if (state.connectionAttemptTimer <= 0)
                m();
            else {
                $rootScope.$broadcast('serverstatechanged');
            }

        }, 500);
    };

    var notifyIfChanged = function (data, dataname, varname) {
        if (data[dataname] != null) {
            if (state[varname] != data[dataname]) {
                state[varname] = data[dataname];
                $rootScope.$broadcast('serverstatechanged.' + varname, state[varname]);
                return true;
            }
        }

        return false;
    }

    var poll = function() {
        if (retryTimer != null) {
            window.clearInterval(retryTimer);
            retryTimer = null;
        }

        if (state.connectionState != 'connected') {
            state.connectionState = 'connecting';
            $rootScope.$broadcast('serverstatechanged');
        }

        var url = '/serverstate/?lasteventid=' + parseInt(state.lastEventId) + '&longpoll=' + (state.lastEventId > 0 ? 'true' : 'false') + '&duration=' + parseInt((polltime-1000) / 1000) + 's';
        AppService.get(url, {timeout: state.lastEventId > 0 ? polltime : 5000}).then(
            function (response) {
                var anychanged =
                    notifyIfChanged(response.data, 'LastEventID', 'lastEventId') |
                    notifyIfChanged(response.data, 'LastDataUpdateID', 'lastDataUpdateId') |
                    notifyIfChanged(response.data, 'LastNotificationUpdateID', 'lastNotificationUpdateId') |
                    notifyIfChanged(response.data, 'ActiveTask', 'activeTask') |
                    notifyIfChanged(response.data, 'ProgramState', 'programState');


                if (state.connectionState != 'connected') {
                    state.connectionState = 'connected';
                    $rootScope.$broadcast('serverstatechanged.connectionState', state.connectionState);
                    anychanged = true;
                }

                if (anychanged)
                    $rootScope.$broadcast('serverstatechanged');

                poll();
            },

            function(respone) {
                if (state.connectionState == 'connected') {
                    // First failure, we ignore
                    state.connectionState = 'connecting';

                    // Try again
                    poll();
                } else {

                    // TODO: First request should not long-poll ....

                    // Real failure, start countdown
                    state.connectionState = 'disconnected';

                    countdownForForRePoll(poll);
                }

                // Notify
                $rootScope.$broadcast('serverstatechanged');

            }
        );
    };

    poll();
});