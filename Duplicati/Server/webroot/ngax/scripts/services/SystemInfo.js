backupApp.service('SystemInfo', function($rootScope, $timeout, Localization, AppService, AppUtils) {

    var state = {};
    this.state = state;


    function reloadBackendConfig() {
        if (state.BackendModules == null)
            return;

        var tmp = angular.copy(state.BackendModules);
        state.GroupedBackendModules = [];

        var push_with_type = function(m, order, alternate) {
            m = angular.copy(m);
            m.GroupType = state.GroupTypes[order];
            if (alternate != null)
                m.DisplayName = alternate;
            m.OrderKey = order;
            state.GroupedBackendModules.push(m);
            return true;
        };

        for(var i in state.BackendModules)
        {
            var m = state.BackendModules[i];
            var used = false;

            if (state.backendgroups.local[m.Key] !== undefined)
                used |= push_with_type(m, 0, state.backendgroups.local[m.Key]);

            if (state.backendgroups.std[m.Key] !== undefined)
                used |= push_with_type(m, 1, state.backendgroups.std[m.Key]);

            if (state.backendgroups.prop[m.Key] !== undefined)
                used |= push_with_type(m, 2, state.backendgroups.prop[m.Key]);

            if (!used)
                push_with_type(m, 3);
        }
    };


    function reloadTexts() {
        state.backendgroups = this.backendgroups = {
            std: { 
                'ftp': null, 
                'ssh': null, 
                'webdav': null, 
                'openstack': Localization.localize('OpenStack Object Storage / Swift'), 
                's3': Localization.localize('S3 Compatible'), 
                'aftp': Localization.localize('FTP (Alternative)')
            },
            local: {'file': null},
            prop: {
                's3': null,
                'azure': null,
                'googledrive': null,
                'onedrive': null,
                'cloudfiles': null,
                'gcs': null,
                'openstack': null,
                'hubic': null,
                'amzcd': null,
                'b2': null,
                'mega': null,
                'box': null,
                'od4b': null,
                'mssp': null,
                'dropbox': null
            }
        };

        state.GroupTypes = this.GroupTypes = [
            Localization.localize('Local storage'),
            Localization.localize('Standard protocols'),
            Localization.localize('Proprietary'),
            Localization.localize('Others')
        ];

        reloadBackendConfig();

        if (state.GroupedBackendModules != null)
            for(var n in state.GroupedBackendModules)
                state.GroupedBackendModules[n].GroupType = this.GroupTypes[state.GroupedBackendModules[n].OrderKey];

        $rootScope.$broadcast('systeminfochanged');
    };

    reloadTexts();
    Localization.watch($rootScope, reloadTexts);

    this.watch = function(scope, m) {
        scope.$on('systeminfochanged', function() {
            if (m) m();

            $timeout(function() {
                scope.$digest();
            });
        });

        if (m) $timeout(m);
        return state;
    };

    this.notifyChanged = function() {
        $rootScope.$broadcast('systeminfochanged');
    };

    AppService.get('/systeminfo').then(function(data) {

        angular.copy(data.data, state);

        reloadTexts();
        reloadBackendConfig();
        $rootScope.$broadcast('systeminfochanged');        

    }, AppUtils.connectionError)
});
