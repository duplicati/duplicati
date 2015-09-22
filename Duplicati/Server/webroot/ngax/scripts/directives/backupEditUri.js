backupApp.directive('backupEditUri', function() {
  return {
    restrict: 'E',
    scope: {
    	uri: '=uri',
    	hide: '=hide'
    },
    templateUrl: 'templates/edituri.html',
    controller: function($scope, AppService, AppUtils, SystemInfo, EditUriBackendConfig) {

		var scope = $scope;
		var backenddataloaders = angular.copy(EditUriBackendConfig.loaders);

		var builduri = function() {

			if (EditUriBackendConfig.validaters[scope.Backend.Key] == null) {
				
				if (!EditUriBackendConfig.defaultvalidater(scope))
					return;

			} else {
				
				if (!EditUriBackendConfig.validaters[scope.Backend.Key](scope))
					return;
			}

			if (EditUriBackendConfig.builders[scope.Backend.Key] == null)
				return EditUriBackendConfig.defaultbuilder(scope);
			else
				return EditUriBackendConfig.builders[scope.Backend.Key](scope);
		}

		$scope.testConnection = function() {
			var res = builduri();
			if (res) {

                var hasTriedCreate = false;
                var hasTriedCert = false;

                var testConnection = function() {
                	scope.Testing = true;
                	AppService.post('/remoteoperation/test', res).then(function() {
                		scope.Testing = false;
                		alert('Connection worked!');
                	}, handleError);
                };

                var createFolder = function() {
                	scope.Testing = true;
                	AppService.post('/remoteoperation/create', res).then(testConnection, handleError);
                };

                var handleError = function(data) {

                	scope.Testing = false;
                	var message = data.statusText;

                    if (!hasTriedCreate && message == 'missing-folder')
                    {
                        if (confirm('The folder ' + scope.Folder + ' does not exist\nCreate it now?')) {
                            createFolder();
                        }
                    }
                    else if (!hasTriedCert && message.indexOf('incorrect-cert:') == 0)
                    {
                        var hash = message.substr('incorrect-cert:'.length);
                        if (confirm('The server certificate could not be validated.\nDo you want to approve the SSL certificate with the hash: ' + hash + '?' )) {

                            hasTriedCert = true;
                            scope.AdvancedOptions += '\r\n--accept-specified-ssl-hash=' + hash;

                            testConnection();
                        }
                    }
                    else
                        alert('Failed to connect: ' + message);
                }

                testConnection();
			}
		};

		$scope.save = function() {

			var res = builduri();
			if (res) {
				scope.uri = res;
				scope.hide();
			}
		};

		$scope.contains_value = AppUtils.contains_value;

		$scope.$watch('Backend', function() {
			if (scope.Backend == null) {
				$scope.TemplateUrl = null;
				return;
			}

			scope.SupportsSSL = false;
			for(var n in scope.Backend.Options)
				if (scope.Backend.Options[n].Name == 'use-ssl')
					scope.SupportsSSL = true;

			scope.TemplateUrl = EditUriBackendConfig.templates[scope.Backend.Key];
			if (scope.TemplateUrl == null)
				scope.TemplateUrl = EditUriBackendConfig.defaulttemplate;

			if (backenddataloaders[scope.Backend.Key] == null)
				return;

			backenddataloaders[scope.Backend.Key](scope);
		});

		var reparseuri = function() {
			scope.Backend = scope.DefaultBackend;

			var parts = AppUtils.decode_uri(scope.uri);

			for(var n in scope.SystemInfo.GroupedBackendModules)
				if (scope.SystemInfo.GroupedBackendModules[n].Key == parts['backend-type'])
					scope.Backend = $scope.SystemInfo.GroupedBackendModules[n];

			scope.Username = parts['--auth-username'];
			scope.Password = parts['--auth-password'];
			scope.UseSSL = parts['--use-ssl'];
			scope.Port = parts['server-port'];
			scope.Server = parts['server-name'];
			scope.Path = parts['server-path'];

			if (scope.Backend != null && scope.uri != null && EditUriBackendConfig.parsers[scope.Backend.Key])
				EditUriBackendConfig.parsers[scope.Backend.Key](scope, parts['backend-type'], parts['server-name'], parts['server-path'], parts['server-port'], parts);

			delete parts['--auth-username'];
			delete parts['--auth-password'];
			delete parts['--use-ssl'];
			scope.AdvancedOptions = AppUtils.serializeAdvancedOptions(parts);
		};

		$scope.SystemInfo = SystemInfo.watch($scope, function() {
			for(var n in scope.SystemInfo.GroupedBackendModules)
				if (scope.SystemInfo.GroupedBackendModules[n].Key == EditUriBackendConfig.defaultbackend)
					scope.DefaultBackend = scope.SystemInfo.GroupedBackendModules[n];

			reparseuri();
		});
		$scope.$watch('uri', reparseuri);
	}
  };
});