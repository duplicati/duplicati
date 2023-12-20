backupApp.service('BackupList', function($rootScope, $timeout, AppService, AppUtils, ServerStatus) {
    var list = [];
    var lookup = {};

    this.list = list;
    this.lookup = lookup;

    this.watch = function(scope, m) {
        scope.$on('backuplistchanged', function() {
            $timeout(function() {
                if (m) m();
                scope.$digest();
            });
        });

        if (m) $timeout(m);

        return list;
    }

    function updateNextRunStamp() {
        var schedule = ServerStatus.state.proposedSchedule || [];

        for(var n in list) {
            if (list[n].Backup.Metadata == null)
                list[n].Backup.Metadata = {};
            delete list[n].Backup.Metadata["NextScheduledRun"];
        }

        for(var n in schedule)
            if (lookup[schedule[n].Item1])
                lookup[schedule[n].Item1].Backup.Metadata["NextScheduledRun"] = schedule[n].Item2;

        $rootScope.$broadcast('backuplistchanged');
    };

    var reload = function() {
        AppService.get('/backups').then(function(data) {
            list.length = 0;

            for (var prop in lookup)
                if (lookup.hasOwnProperty(prop))
                    delete lookup[prop];

            for (var i = 0; i < data.data.length; i++) {
                list.push(data.data[i]);
                lookup[data.data[i].Backup.ID] = data.data[i];
            }

            updateNextRunStamp();
        });
    };


    $rootScope.$on('serverstatechanged.lastDataUpdateId', reload);
    $rootScope.$on('serverstatechanged.proposedSchedule', updateNextRunStamp);
});
