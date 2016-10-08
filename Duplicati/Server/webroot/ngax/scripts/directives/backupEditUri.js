backupApp.directive('backupEditUri', function() {
  return {
    restrict: 'E',
    scope: {
        uri: '=uri',
        hide: '=hide'
    },
    templateUrl: 'templates/edituri.html',
    controller: function($scope, AppService, AppUtils, SystemInfo, EditUriBackendConfig, DialogService, EditUriBuiltins) {

        var scope = $scope;
        scope.AppUtils = AppUtils;

        var builduri = function(callback) {

            function validationCompleted() {
                if (EditUriBackendConfig.builders[scope.Backend.Key] == null)
                    callback(EditUriBackendConfig.defaultbuilder(scope));
                else
                    callback(EditUriBackendConfig.builders[scope.Backend.Key](scope));
            };

            if (EditUriBackendConfig.validaters[scope.Backend.Key] == null)
                EditUriBackendConfig.defaultvalidater(scope, validationCompleted);
            else
                EditUriBackendConfig.validaters[scope.Backend.Key](scope, validationCompleted);
        }

        function performConnectionTest(uri) {

            var hasTriedCreate = false;
            var hasTriedCert = false;
            var hasTriedMozroots = false;
            var hasTriedHostkey = false;
            var dlg = null;

            var testConnection = function() {
                scope.Testing = true;
                if (dlg != null)
                    dlg.dismiss();

                dlg = DialogService.dialog('Testing ...', 'Testing connection ...', [], null, function() {
                    AppService.post('/remoteoperation/test', uri).then(function() {
                        scope.Testing = false;
                        dlg.dismiss();

                        if (EditUriBackendConfig.testers[scope.Backend.Key] != null)
                            EditUriBackendConfig.testers[scope.Backend.Key](scope, function() {
                                DialogService.dialog('Success', 'Connection worked!');
                            });
                        else
                            DialogService.dialog('Success', 'Connection worked!');

                    }, handleError);
                });             

            };

            var createFolder = function() {
                hasTriedCreate = true;
                scope.Testing = true;
                AppService.post('/remoteoperation/create', uri).then(testConnection, handleError);
            };

            var appendApprovedCert = function(hash)
            {
                hasTriedCert = true;
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
                DialogService.dialog('Trust server certificate?', 'The server certificate could not be validated.\nDo you want to approve the SSL certificate with the hash: ' + hash + '?', ['No', 'Yes'], function(ix) {
                    if (ix == 1) {
                        appendApprovedCert(hash);
                        builduri(function(res) {
                            uri = res;
                            testConnection();
                        });
                    }
                });
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
                if (dlg != null)
                    dlg.dismiss();

                var message = data.statusText;

                if (!hasTriedCreate && message == 'missing-folder')
                {
                    var folder = scope.Folder;
                    if ((folder || "") == "")
                        folder = scope.Path;
                    if ((folder || "") == "")
                        folder = '';

                    DialogService.dialog('Create folder?', 'The folder ' + folder + ' does not exist\nCreate it now?', ['No', 'Yes'], function(ix) {
                        if (ix == 1)
                            createFolder();
                    });
                }
                else if (!hasTriedCert && message.indexOf('incorrect-cert:') == 0)
                {
                    var hash = message.substr('incorrect-cert:'.length);
                    if (hasCertApproved(hash)) {
                        if (data.data != null && data.data.Message != null)
                            message = data.data.Message;
                        
                        DialogService.dialog('Error', 'Failed to connect: ' + message);
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
                                            DialogService.dialog('Import failed', 'Import completed, but no certificates were found after the import');
                                        } else {
                                            testConnection();
                                        }
                                    }, function(resp) {

                                        scope.Testing = false;
                                        message = resp.statusText;
                                        if (data.data != null && data.data.Message != null)
                                            message = data.data.Message;
                                        
                                        DialogService.dialog('Error', 'Failed to import: ' + message);

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
                else if (!hasTriedHostkey && message.indexOf('incorrect-host-key:') == 0)
                {
                    var re = /incorrect-host-key\s*:\s*"([^"]*)"(,\s*accepted-host-key\s*:\s*"([^"]*)")?/;
                    var m = re.exec(message);
                    var key = null;
                    var prev = null;
                    if (m != null) {
                        key = m[1] || '';
                        prev = m[3] || '';
                    }

                    if ((key || '').trim().length == 0 || key == prev) {
                        if (data.data != null && data.data.Message != null)
                            message = data.data.Message;
                        
                        DialogService.dialog('Error', 'Failed to connect: ' + message);
                    } 
                    else 
                    {
                        var message = ((prev || '').trim().length == 0) ? 
                            ('No certificate was specified previously, please verify with the server administrator that the key is correct: ' + key + ' \n\nDo you want to approve the reported host key?')
                            : 
                            ('The host key has changed, please check with the server administrator if this is correct, otherwise you could be the victim of a MAN-IN-THE-MIDDLE attack.\n\nDo you want to REPLACE your CURRENT host key "' + prev + '" with the REPORTED host key: ' + key + '?');

                        DialogService.dialog('Trust host certificate?', message, ['No', 'Yes'], function(ix) {
                            if (ix == 1) {
                                hasTriedHostkey = true;
                                for(var n in scope.AdvancedOptions) {
                                    if (scope.AdvancedOptions[n].indexOf('--ssh-fingerprint=') == 0) {
                                        scope.AdvancedOptions.splice(n, 1);
                                        break;
                                    }
                                }

                                scope.AdvancedOptions.push('--ssh-fingerprint=' + key);
                                builduri(function(res) {
                                    uri = res;
                                    testConnection();
                                });
                            }
                        });

                    }
                }
                else
                {
                    if (data.data != null && data.data.Message != null)
                        message = data.data.Message;
                    
                    DialogService.dialog('Error', 'Failed to connect: ' + message);
                }
            }

            testConnection();
        };

        $scope.testConnection = function() {
            builduri(performConnectionTest);
        };

        $scope.save = function() {
            builduri(function(res) {
                scope.uri = res;
                scope.hide();
            });
        };

        $scope.contains_value = AppUtils.contains_value;

        $scope.$watch('Backend', function() {
            if (scope.Backend == null) {
                $scope.TemplateUrl = null;
                $scope.AdvanceOptionList = null;
                return;
            }

            var opts = angular.copy(scope.Backend.Options || []);
            for(var n in opts)
                opts[n].Category = scope.Backend.DisplayName;

            for(var m in SystemInfo.state.ConnectionModules)
            {
                var t = angular.copy(SystemInfo.state.ConnectionModules[m].Options);
                for(var n in t)
                    t[n].Category = SystemInfo.state.ConnectionModules[m].DisplayName;
                opts.push.apply(opts, t);
            }

            $scope.AdvanceOptionList = opts;
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

            for(var n in scope.SystemInfo.GroupedBackendModules) {
                if (scope.SystemInfo.GroupedBackendModules[n].Key == parts['backend-type']) {
                    scope.Backend = $scope.SystemInfo.GroupedBackendModules[n];
                    break;
                }

                if ((scope.SystemInfo.GroupedBackendModules[n].Key + 's') == parts['backend-type']) {
                    var hasssl = false;
                    var bk = scope.SystemInfo.GroupedBackendModules[n];

                    for(var o in bk.Options) {
                        if (bk.Options[o].Name == 'use-ssl') {
                            hasssl = true;
                            break;
                        }
                    }

                    if (hasssl) {
                        scope.Backend = bk;
                        parts['--use-ssl'] = true;
                        break;
                    }
                }
            }


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
