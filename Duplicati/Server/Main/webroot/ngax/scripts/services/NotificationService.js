backupApp.service('NotificationService', function($rootScope, $timeout, AppService, ServerStatus) {
    
    // Track repeated refresh requests
    var isRefreshing = false;
    var needsRefresh = false;

    var notifications = [];

    this.notifications = notifications;

    function refresh_notifications() {

        if (isRefreshing) {
            needsRefresh = true;
            return;
        }

        needsRefresh = false;
        isRefreshing = true;

        AppService.get('/notifications').then(
            function(resp) {
                
                var idmap = {};
                for(var n in resp.data)
                    idmap[resp.data[n].ID] = resp.data[n];

                // Sync map and list
                for (var i = notifications.length - 1; i >= 0; i--)
                    if (!idmap[notifications[i].ID])
                        notifications.splice(i, 1);
                    else
                        delete idmap[notifications[i].ID];

                // Then add all new items
                for (var n in idmap)
                    notifications.push(idmap[n]);

                notifications.sort(function(a, b) { 
                    if (a.ID < b.ID)
                        return -1;
                    else if (a.ID > b.ID)
                        return 1;
                    else
                        return 0;
                });
                
                $rootScope.$broadcast('notificationschanged');

                isRefreshing = false;
                if (needsRefresh)
                    refresh_notifications();

            },
            function(resp) {
                isRefreshing = false;
            }
        );
    };

    this.watch = function(scope, m) {
        scope.$on('notificationschanged', function() {
            $timeout(function() {
                if (m) m();
                scope.$digest();
            });
        });

        if (m) $timeout(m);
        return notifications;
    }    

    var first = ServerStatus.state.lastNotificationUpdateId == -1;
    $rootScope.$on('serverstatechanged.lastNotificationUpdateId', function() { 
        // We always refresh, so no need to use the initial event
        if (first)
            first = false;
        else
            refresh_notifications();
    });

    this.refresh_notifications = refresh_notifications;

    refresh_notifications();
});
