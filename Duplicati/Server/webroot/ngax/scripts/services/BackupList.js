backupApp.service('BackupList', function($rootScope, $timeout, AppService) {
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

            $rootScope.$broadcast('backuplistchanged');
        });
    };

    $rootScope.$on('serverstatechanged.lastDataUpdateId', reload);

    reload();
});