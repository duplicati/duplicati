backupApp.service('SystemInfo', function($rootScope, $timeout, AppService, AppUtils) {

	var state = {};
	this.state = state;

	var backendgroups = {
	    std: { 'ftp': null, 'ssh': null, 'webdav': null, 'openstack': 'OpenStack Object Storage / Swift', 's3': 'S3 Compatible', 'aftp': 'FTP (Alternative)'},
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
            'mssp': null
        }
	};

	this.backendgroups = backendgroups;

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

		var tmp = angular.copy(state.BackendModules);
    	state.GroupedBackendModules = [];

    	var push_with_type = function(m, type, order, alternate) {
			m = angular.copy(m);
			m.GroupType = type;
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

    		if (backendgroups.local[m.Key] !== undefined)
    			used |= push_with_type(m, 'Local storage', 0, backendgroups.local[m.Key]);

    		if (backendgroups.std[m.Key] !== undefined)
    			used |= push_with_type(m, 'Standard protocols', 1, backendgroups.std[m.Key]);

    		if (backendgroups.prop[m.Key] !== undefined)
    			used |= push_with_type(m, 'Proprietary', 2, backendgroups.prop[m.Key]);

    		if (!used)
    			push_with_type(m, 'Others', 3);
    	}

    	$rootScope.$broadcast('systeminfochanged');

    }, AppUtils.connectionError)
});