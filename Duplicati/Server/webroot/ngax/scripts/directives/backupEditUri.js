backupApp.directive('backupEditUri', function() {
  return {
    restrict: 'E',
    scope: {
    	uri: '=uri',
    	hide: '=hide'
    },
    templateUrl: 'templates/edituri.html',
    controller: function($scope, AppService, AppUtils, SystemInfo, EditUriBackendConfig, $injector) {

		var scope = $scope;

		// Dynamically load extensions
 		$injector.get('EditUriBuiltins');

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
                var hasTriedMozroots = false;

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

                var appendApprovedCert = function(hash)
                {
                	for(var n in scope.AdvancedOptions) {
                		if (scope.AdvancedOptions[n].indexOf('--accept-specified-ssl-hash=') == 0)
                		{
            				var certs = scope.AdvancedOptions[n].substr('--accept-specified-ssl-hash='.length).split(',');
            				for(var i in certs)
            					if (certs[i] == hash)
            						return;

            				scope.AdvancedOptions[n] += ',' + hash;
        					return;
                		}
                	}

                    scope.AdvancedOptions.push('--accept-specified-ssl-hash=' + hash);
                }


                var askApproveCert = function(hash) {
                    if (confirm('The server certificate could not be validated.\nDo you want to approve the SSL certificate with the hash: ' + hash + '?' )) {
                    	appendApprovedCert(hash);

                        testConnection();
                    }
                };

                var hasCertApproved = function(hash)
                {
                	for(var n in scope.AdvancedOptions) {
                		if (scope.AdvancedOptions[n].indexOf('--accept-specified-ssl-hash=') == 0)
                		{
            				var certs = scope.AdvancedOptions[n].substr('--accept-specified-ssl-hash='.length).split(',');
            				for(var i in certs)
            					if (certs[i] == hash)
            						return true;

                			break;
                		}
                	}

            		return false;
                }

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
                        if (hasCertApproved(hash)) {
	                    	if (data.data != null && data.data.Message != null)
	                    		message = data.data.Message;
	                    	
	                        alert('Failed to connect: ' + message);
							return;                        	
                        }

                    	if ($scope.SystemInfo.MonoVersion != null && !hasTriedMozroots) {
                    		
                    		hasTriedMozroots = true;

							AppService.post('/webmodule/check-mono-ssl', {'mono-ssl-config': 'List'}).then(function(data) {
								if (data.data.Result.count == 0) {
									if (confirm('You appear to be running Mono with no SSL certificates loaded.\nDo you want to import the list of trusted certificates from Mozilla?'))
									{
										scope.Testing = true;
										AppService.post('/webmodule/check-mono-ssl', {'mono-ssl-config': 'Install'}).then(function(data) {
											scope.Testing = false;
											if (data.data.Result.count == 0) {
												alert('Import completed, but no certificates were found after the import');
											} else {
												testConnection();
											}
										}, function(resp) {

											scope.Testing = false;
											message = resp.statusText;
					                    	if (data.data != null && data.data.Message != null)
					                    		message = data.data.Message;
					                    	
					                        alert('Failed to import: ' + message);

										});
									}
									else
									{
										askApproveCert(hash);
									}
								} else {
									askApproveCert(hash);
								}

							}, AppUtils.connectionError);

                    	}
                    	else
                    	{
                    		askApproveCert(hash);
	                    }
                    }
                    else
                    {
                    	if (data.data != null && data.data.Message != null)
                    		message = data.data.Message;
                    	
                        alert('Failed to connect: ' + message);
                    }
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

			if (EditUriBackendConfig.loaders[scope.Backend.Key] == null)
				return;

			EditUriBackendConfig.loaders[scope.Backend.Key](scope);
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
			scope.AdvancedOptions = AppUtils.serializeAdvancedOptionsToArray(parts);
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