backupApp.service('SystemInfo', function($rootScope, $timeout, $cookies, AppService, AppUtils, gettextCatalog) {

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
                'openstack': gettextCatalog.getString('OpenStack Object Storage / Swift'),
                's3': gettextCatalog.getString('S3 Compatible'),
                'aftp': gettextCatalog.getString('FTP (Alternative)')
            },
            local: {'file': null},
            prop: {
                'e2':null,
                's3': null,
                'azure': null,
                'googledrive': null,
                'onedrive': null,
                'onedrivev2': null,
                'sharepoint': null,
                'msgroup': null,
                'cloudfiles': null,
                'gcs': null,
                'openstack': null,
                'b2': null,
                'mega': null,
                'idrive': null,
                'box': null,
                'od4b': null,
                'mssp': null,
                'dropbox': null,
                'sia': null,
                'storj': null,
                'tardigrade': null,
                'jottacloud': null,
				'rclone': null,
				'cos': null
            }
        };

        state.GroupTypes = this.GroupTypes = [
            gettextCatalog.getString('Local storage'),
            gettextCatalog.getString('Standard protocols'),
            gettextCatalog.getString('Proprietary'),
            gettextCatalog.getString('Others')
        ];

        reloadBackendConfig();

        if (state.GroupedBackendModules != null)
            for(var n in state.GroupedBackendModules)
                state.GroupedBackendModules[n].GroupType = this.GroupTypes[state.GroupedBackendModules[n].OrderKey];

        $rootScope.$broadcast('systeminfochanged');
    };

    reloadTexts();
    $rootScope.$on('gettextLanguageChanged', reloadTexts);


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

    function loadSystemInfo(reload) {
        AppService.get('/systeminfo').then(function(data) {
            angular.copy(data.data, state);
            
            if (reload !== true) {
                uiLanguage = $cookies.get('ui-locale');
                if ((uiLanguage || '').trim().length == 0) {
                    gettextCatalog.setCurrentLanguage(state.BrowserLocale.Code.replace("-", "_"));
                } else {
                    gettextCatalog.setCurrentLanguage(uiLanguage.replace("-", "_"));
                }
            }

            reloadTexts();
            reloadBackendConfig();
            $rootScope.$broadcast('systeminfochanged');
        }, AppUtils.connectionError)
    }
    
    loadSystemInfo();
    $rootScope.$on('ui_language_changed', function() {
        loadSystemInfo(true);
    });
});
