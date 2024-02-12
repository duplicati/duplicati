backupApp.directive('backupEditUri', function(gettextCatalog) {
  return {
    restrict: 'E',
    scope: {
        uri: '=uri',
        setBuilduriFn: '&'
    },
    templateUrl: 'templates/edituri.html',
    controller: function($scope, AppService, AppUtils, SystemInfo, EditUriBackendConfig, DialogService, EditUriBuiltins, $http) {

        var scope = $scope;
        scope.AppUtils = AppUtils;

        // called before a user action (callback=test or oauth) needs the backend uri to be up-to-date
        var builduri = function(callback) {

            function validationCompleted() {
                if (EditUriBackendConfig.builders[scope.Backend.Key] == null)
                    callback(EditUriBackendConfig.defaultbuilder(scope));
                else
                    callback(EditUriBackendConfig.builders[scope.Backend.Key](scope));
            }

            if (EditUriBackendConfig.validaters[scope.Backend.Key] == null)
                EditUriBackendConfig.defaultvalidater(scope, validationCompleted);
            else
                EditUriBackendConfig.validaters[scope.Backend.Key](scope, validationCompleted);
        };

        $scope.setBuilduriFn({ builduriFn: builduri });

        function performConnectionTest(uri) {

            var hasTriedCreate = false;
            var hasTriedCert = false;
            var hasTriedMozroots = false;
            var hasTriedHostkey = false;
            var dlg = null;
            const ur = new URL(uri)
            var oauthUrl = ur.searchParams.get("oauth-url");
            if (oauthUrl == null || oauthUrl.length == 0) {
                // no oauth server set, add the global one.
                ur.searchParams.append('oauth-url', scope.oauth_global_server + 'refresh');
                uri = ur.toString();
            }

            var testConnection = function() {
                scope.Testing = true;
                if (dlg != null)
                    dlg.dismiss();

                dlg = DialogService.dialog(gettextCatalog.getString('Testing ...'), gettextCatalog.getString('Testing connection ...'), [], null, function() {
                    AppService.post('/remoteoperation/test', uri).then(function() {
                        scope.Testing = false;
                        dlg.dismiss();

                        if (EditUriBackendConfig.testers[scope.Backend.Key] != null)
                            EditUriBackendConfig.testers[scope.Backend.Key](scope, function() {
                                DialogService.dialog(gettextCatalog.getString('Success'), gettextCatalog.getString('Connection worked!'));
                            });
                        else
                            DialogService.dialog(gettextCatalog.getString('Success'), gettextCatalog.getString('Connection worked!'));

                    }, handleError);
                });             

            };

            var createFolder = function() {
                hasTriedCreate = true;
                scope.Testing = true;
                uri += '&autocreate=true';
                AppService.post('/remoteoperation/test', uri).then(testConnection, handleError);
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
            };

            var askApproveCert = function(hash) {
                DialogService.dialog(gettextCatalog.getString('Trust server certificate?'), gettextCatalog.getString('The server certificate could not be validated.\nDo you want to approve the SSL certificate with the hash: {{hash}}?', { hash: hash }), [gettextCatalog.getString('No'), gettextCatalog.getString('Yes')], function(ix) {
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
            };

            var handleError = function(data) {

                scope.Testing = false;
                if (dlg != null)
                    dlg.dismiss();

                var message = AppService.responseErrorMessage(data);

                if (!hasTriedCreate && message == 'missing-folder')
                {
                    var folder = scope.Folder;
                    if ((folder || "") == "")
                        folder = scope.Path;
                    if ((folder || "") == "")
                        folder = '';

                    DialogService.dialog(gettextCatalog.getString('Create folder?'), gettextCatalog.getString('The folder {{folder}} does not exist.\nCreate it now?', { folder: folder }), [gettextCatalog.getString('No'), gettextCatalog.getString('Yes')], function(ix) {
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
                        
                        DialogService.dialog(gettextCatalog.getString('Error'), gettextCatalog.getString('Failed to connect: ') + message);
                        return;                         
                    }

                    if ($scope.SystemInfo.MonoVersion != null && !hasTriedMozroots) {
                        
                        hasTriedMozroots = true;

                        AppService.post('/webmodule/check-mono-ssl', {'mono-ssl-config': 'List'}).then(function(data) {
                            if (data.data.Result.count == 0) {
                                if (confirm(gettextCatalog.getString('You appear to be running Mono with no SSL certificates loaded.\nDo you want to import the list of trusted certificates from Mozilla?')))
                                {
                                    scope.Testing = true;
                                    AppService.post('/webmodule/check-mono-ssl', {'mono-ssl-config': 'Install'}).then(function(data) {
                                        scope.Testing = false;
                                        if (data.data.Result.count == 0) {
                                            DialogService.dialog(gettextCatalog.getString('Import failed'), gettextCatalog.getString('Import completed, but no certificates were found after the import'));
                                        } else {
                                            testConnection();
                                        }
                                    }, function(resp) {

                                        scope.Testing = false;
                                        message = AppService.responseErrorMessage(resp);
                                        
                                        DialogService.dialog(gettextCatalog.getString('Error'), gettextCatalog.getString('Failed to import: ') + message);

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
                        
                        DialogService.dialog(gettextCatalog.getString('Error'), gettextCatalog.getString('Failed to connect: ') + message);
                    } 
                    else 
                    {
                        var message = ((prev || '').trim().length == 0) ? 
                            (gettextCatalog.getString('No certificate was specified previously, please verify with the server administrator that the key is correct: {{key}} \n\nDo you want to approve the reported host key?', { key: key }))
                            : 
                            (gettextCatalog.getString('The host key has changed, please check with the server administrator if this is correct, otherwise you could be the victim of a MAN-IN-THE-MIDDLE attack.\n\nDo you want to REPLACE your CURRENT host key "{{prev}}" with the REPORTED host key: {{key}}?', { prev: prev, key: key }));

                        DialogService.dialog(gettextCatalog.getString('Trust host certificate?'), message, [gettextCatalog.getString('No'), gettextCatalog.getString('Yes')], function(ix) {
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
                    
                    DialogService.dialog(gettextCatalog.getString('Error'), gettextCatalog.getString('Failed to connect: ') + message);
                }
            };

            testConnection();
        }

        $scope.testConnection = function() {
            builduri(performConnectionTest);
        };

        function perform_oauth_start_token_creation (uri) {
            const ur = new URL(uri)
            // check for the oAuth server in the backend specific parameters
            var oauthUrl = ur.searchParams.get("oauth-url");
            if (oauthUrl != null && oauthUrl.length > 0) {
                if (oauthUrl.endsWith("/refresh"))
                    oauthUrl = oauthUrl.substr(0, oauthUrl.length - 7);
                $scope.oauth_service_link = oauthUrl;
            }
            const token = ur.searchParams.get("authid");
            if (token != null) $scope.oauth_create_token = token;
            scope.oauth_start_link = scope.oauth_service_link + '?type=' + scope.Backend.Key + '&token=' + scope.oauth_create_token;

            $scope.oauth_in_progress = true;

            var w = 450;
            var h = 600;

            var url = $scope.oauth_start_link;

            var countDown = 100;
            var ft = $scope.oauth_create_token;
            var left = (screen.width / 2) - (w / 2);
            var top = (screen.height / 2) - (h / 2);
            var wnd = window.open(url, '_blank', 'height=' + h + ',width=' + w + ',menubar=0,status=0,titlebar=0,toolbar=0,left=' + left + ',top=' + top)

            // check the OAuth server
            var recheck = function () {
                countDown--;
                if (countDown > 0 && ft == $scope.oauth_create_token) {
                    $http.jsonp($scope.oauth_service_link + 'fetch?callback=JSON_CALLBACK', {params: {'token': ft}}).then(
                        function (response) {
                            if (response.data.authid) {
                                $scope.AuthID = response.data.authid;
                                $scope.oauth_in_progress = false;
                                if (wnd != null) {
                                   wnd.close();
                                   wnd = null;
                                }
                                countDown = 0;
                            } else {
                                setTimeout(recheck, 3000);
                            }
                        },
                        function (response) {
                            if (response.status == 404) {
                                $scope.oauth_in_progress = false;
                                if (wnd != null) {
                                   wnd.close();
                                   wnd = null;
                                }
                               countDown = 0;
                            } else
                                setTimeout(recheck, 3000);
                        }
                    );
                } else {
                    $scope.oauth_in_progress = false;
                    if (wnd != null) {
                        wnd.close();
                        wnd = null;
                    }
                }
            };
            if (wnd != null)
                setTimeout(recheck, 6000);
            return false;
        };

        $scope.oauth_start_token_creation = function () {
            builduri(perform_oauth_start_token_creation);
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
