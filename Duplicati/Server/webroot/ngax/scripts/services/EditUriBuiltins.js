backupApp.service('EditUriBuiltins', function (AppService, AppUtils, SystemInfo, EditUriBackendConfig, DialogService, $http, gettextCatalog) {

    EditUriBackendConfig.mergeServerAndPath = function (scope) {
        if ((scope.Server || '') != '') {
            var p = scope.Path;
            scope.Path = scope.Server;
            if ((p || '') != '')
                scope.Path += '/' + p;

            delete scope.Server;
        }
    };

    // All backends with a custom UI must register here
    EditUriBackendConfig.templates['file']        = 'templates/backends/file.html';
    EditUriBackendConfig.templates['s3']          = 'templates/backends/s3.html';
    EditUriBackendConfig.templates['googledrive'] = 'templates/backends/oauth.html';
    EditUriBackendConfig.templates['onedrive']    = 'templates/backends/oauth.html';
    EditUriBackendConfig.templates['onedrivev2']  = 'templates/backends/oauth.html';
    EditUriBackendConfig.templates['sharepoint']  = 'templates/backends/sharepoint.html';
    EditUriBackendConfig.templates['msgroup']     = 'templates/backends/msgroup.html';
    EditUriBackendConfig.templates['openstack']   = 'templates/backends/openstack.html';
    EditUriBackendConfig.templates['azure']       = 'templates/backends/azure.html';
    EditUriBackendConfig.templates['gcs']         = 'templates/backends/gcs.html';
    EditUriBackendConfig.templates['b2']          = 'templates/backends/b2.html';
    EditUriBackendConfig.templates['mega']        = 'templates/backends/mega.html';
    EditUriBackendConfig.templates['jottacloud']  = 'templates/backends/oauth.html';
    EditUriBackendConfig.templates['idrive']      = 'templates/backends/idrive.html';
    EditUriBackendConfig.templates['box']         = 'templates/backends/oauth.html';
    EditUriBackendConfig.templates['dropbox']     = 'templates/backends/oauth.html';
    EditUriBackendConfig.templates['sia']         = 'templates/backends/sia.html';
    EditUriBackendConfig.templates['storj']       = 'templates/backends/storj.html';
    EditUriBackendConfig.templates['tardigrade']  = 'templates/backends/tardigrade.html';
	EditUriBackendConfig.templates['rclone']       = 'templates/backends/rclone.html';
	EditUriBackendConfig.templates['cos']       = 'templates/backends/cos.html';
    EditUriBackendConfig.templates['e2'] = 'templates/backends/e2.html';

    EditUriBackendConfig.testers['s3'] = function(scope, callback) {

        if (scope.s3_server != 's3.amazonaws.com')
        {
            callback();
            return;
        }

        var dlg = null;

        dlg = DialogService.dialog(gettextCatalog.getString('Testing permissions...'), gettextCatalog.getString('Testing permissions …'), [], null, function () {
            AppService.post('/webmodule/s3-iamconfig', {
                's3-operation': 'CanCreateUser',
                's3-username': scope.Username,
                's3-password': scope.Password
            }).then(function (data) {
                dlg.dismiss();

                if (data.data.Result.isroot == 'True') {
                    DialogService.dialog(gettextCatalog.getString('User has too many permissions'), gettextCatalog.getString('The user has too many permissions. Do you want to create a new limited user, with only permissions to the selected path?'), [gettextCatalog.getString('Cancel'), gettextCatalog.getString('No'), gettextCatalog.getString('Yes')], function (ix) {
                        if (ix == 0 || ix == 1) {
                            callback();
                        } else {
                            scope.s3_directCreateIAMUser(function () {
                                scope.s3_bucket_check_name = scope.Server;
                                scope.s3_bucket_check_user = scope.Username;

                                callback();
                            });
                        }
                    });
                } else {
                    callback();
                }
            }, function (data) {
                dlg.dismiss();
                AppUtils.connectionError(data);
            });
        });
    }

    // Loaders are a way for backends to request extra data from the server
    EditUriBackendConfig.loaders['s3'] = function (scope) {
        if (scope.s3_providers == null) {
            AppService.post('/webmodule/s3-getconfig', {'s3-config': 'Providers'}).then(function (data) {
                scope.s3_providers = data.data.Result;
                if (scope.s3_server == undefined && scope.s3_server_custom == undefined)
                    scope.s3_server = 's3.amazonaws.com';

            }, AppUtils.connectionError);
        } else {
            if (scope.s3_server == undefined && scope.s3_server_custom == undefined)
                scope.s3_server = 's3.amazonaws.com';
        }

        if (scope.s3_regions == null) {
            AppService.post('/webmodule/s3-getconfig', {'s3-config': 'Regions'}).then(function (data) {
                scope.s3_regions = data.data.Result;
                if (scope.s3_region == null && scope.s3_region_custom == undefined)
                    scope.s3_region = '';
            }, AppUtils.connectionError);
        } else {
            if (scope.s3_region == null && scope.s3_region_custom == undefined)
                scope.s3_region = '';
        }

        if (scope.s3_storageclasses == null) {
            AppService.post('/webmodule/s3-getconfig', {'s3-config': 'StorageClasses'}).then(function (data) {
                scope.s3_storageclasses = data.data.Result;
                if (scope.s3_storageclass == null && scope.s3_storageclass_custom == undefined)
                    scope.s3_storageclass = '';
            }, AppUtils.connectionError);
        } else {
            if (scope.s3_storageclass == null && scope.s3_storageclass_custom == undefined)
                scope.s3_storageclass = '';
        }

        scope.s3_bucket_check_name = null;
        scope.s3_bucket_check_user = null;

        scope.s3_directCreateIAMUser = function (callback) {

            var dlg = null;

            dlg = DialogService.dialog(gettextCatalog.getString('Creating user...'), gettextCatalog.getString('Creating new user with limited access …'), [], null, function () {
                path = (scope.Server || '') + '/' + (scope.Path || '');

                AppService.post('/webmodule/s3-iamconfig', {
                    's3-operation': 'CreateIAMUser',
                    's3-path': path,
                    's3-username': scope.Username,
                    's3-password': scope.Password
                }).then(function (data) {
                    dlg.dismiss();

                    scope.Username = data.data.Result.accessid;
                    scope.Password = data.data.Result.secretkey;

                    DialogService.dialog(gettextCatalog.getString('Created new limited user'), gettextCatalog.getString('New user name is {{user}}.\nUpdated credentials to use the new limited user', {user: data.data.Result.username}), [gettextCatalog.getString('OK')], callback);

                }, function (data) {
                    dlg.dismiss();
                    AppUtils.connectionError(data);
                });
            });
        };

        scope.s3_createIAMPolicy = function () {
            EditUriBackendConfig.validaters['s3'](scope, function () {
                path = (scope.Server || '') + '/' + (scope.Path || '');

                AppService.post('/webmodule/s3-iamconfig', {
                    's3-operation': 'GetPolicyDoc',
                    's3-path': path
                }).then(function (data) {
                    DialogService.dialog(gettextCatalog.getString('AWS IAM Policy'), data.data.Result.doc);
                }, AppUtils.connectionError);
            });
        };
        
        scope.s3_client = s3_client_options[0];
        scope.s3_client_options = s3_client_options;
    };
	
	EditUriBackendConfig.loaders['storj'] = function (scope) {
        if (scope.storj_satellites == null) {
            AppService.post('/webmodule/storj-getconfig', {'storj-config': 'Satellites'}).then(function (data) {
                scope.storj_satellites = data.data.Result;
                if (scope.storj_satellite == undefined && scope.storj_satellite_custom == undefined)
                    scope.storj_satellite = 'us1.storj.io:7777';

            }, AppUtils.connectionError);
        } else {
            if (scope.storj_satellite == undefined && scope.storj_satellite_custom == undefined)
                scope.storj_satellite = 'us1.storj.io:7777';
        }
		
		if (scope.storj_auth_methods == null) {
            AppService.post('/webmodule/storj-getconfig', {'storj-config': 'AuthenticationMethods'}).then(function (data) {
                scope.storj_auth_methods = data.data.Result;
                if (scope.storj_auth_method == undefined)
                    scope.storj_auth_method = 'API key';

            }, AppUtils.connectionError);
        } else {
            if (scope.storj_auth_method == undefined)
                scope.storj_auth_method = 'API key';
        }
    };
	
	EditUriBackendConfig.loaders['tardigrade'] = function (scope) {
        if (scope.tardigrade_satellites == null) {
            AppService.post('/webmodule/storj-getconfig', {'storj-config': 'Satellites'}).then(function (data) {
                scope.tardigrade_satellites = data.data.Result;
                if (scope.tardigrade_satellite == undefined && scope.tardigrade_satellite_custom == undefined)
                    scope.tardigrade_satellite = 'us1.storj.io:7777';

            }, AppUtils.connectionError);
        } else {
            if (scope.tardigrade_satellite == undefined && scope.tardigrade_satellite_custom == undefined)
                scope.tardigrade_satellite = 'us1.storj.io:7777';
        }
		
		if (scope.tardigrade_auth_methods == null) {
            AppService.post('/webmodule/storj-getconfig', {'storj-config': 'AuthenticationMethods'}).then(function (data) {
                scope.tardigrade_auth_methods = data.data.Result;
                if (scope.tardigrade_auth_method == undefined)
                    scope.tardigrade_auth_method = 'API key';

            }, AppUtils.connectionError);
        } else {
            if (scope.tardigrade_auth_method == undefined)
                scope.tardigrade_auth_method = 'API key';
        }
    };

    EditUriBackendConfig.loaders['oauth-base'] = function (scope) {
        scope.oauth_create_token = Math.random().toString(36).substr(2) + Math.random().toString(36).substr(2);
        scope.oauth_service_link = 'https://duplicati-oauth-handler.appspot.com/';
        scope.oauth_start_link = scope.oauth_service_link + '?type=' + scope.Backend.Key + '&token=' + scope.oauth_create_token;
        scope.oauth_in_progress = false;

        scope.oauth_start_token_creation = function () {

            scope.oauth_in_progress = true;

            var w = 450;
            var h = 600;

            var url = scope.oauth_start_link;

            var countDown = 100;
            var ft = scope.oauth_create_token;
            var left = (screen.width / 2) - (w / 2);
            var top = (screen.height / 2) - (h / 2);
            var wnd = window.open(url, '_blank', 'height=' + h + ',width=' + w + ',menubar=0,status=0,titlebar=0,toolbar=0,left=' + left + ',top=' + top)

            var recheck = function () {
                countDown--;
                if (countDown > 0 && ft == scope.oauth_create_token) {
                    $http.jsonp(scope.oauth_service_link + 'fetch?callback=JSON_CALLBACK', {params: {'token': ft}}).then(
                        function (response) {
                            if (response.data.authid) {
                                scope.AuthID = response.data.authid;
                                scope.oauth_in_progress = false;
                                wnd.close();
                            } else {
                                setTimeout(recheck, 3000);
                            }
                        },
                        function (response) {
                            setTimeout(recheck, 3000);
                        }
                    );
                } else {
                    scope.oauth_in_progress = false;
                    if (wnd != null)
                        wnd.close();
                }
            };

            setTimeout(recheck, 6000);

            return false;
        };
    };
  
    EditUriBackendConfig.loaders['googledrive'] = function() { return this['oauth-base'].apply(this, arguments); };
    EditUriBackendConfig.loaders['onedrive']    = function() { return this['oauth-base'].apply(this, arguments); };
    EditUriBackendConfig.loaders['onedrivev2']  = function() { return this['oauth-base'].apply(this, arguments); };
    EditUriBackendConfig.loaders['sharepoint']  = function() { return this['oauth-base'].apply(this, arguments); };
    EditUriBackendConfig.loaders['msgroup']     = function() { return this['oauth-base'].apply(this, arguments); };
    EditUriBackendConfig.loaders['jottacloud']  = function() { return this['oauth-base'].apply(this, arguments); };
    EditUriBackendConfig.loaders['box']         = function() { return this['oauth-base'].apply(this, arguments); };
    EditUriBackendConfig.loaders['dropbox']     = function() { return this['oauth-base'].apply(this, arguments); };

    EditUriBackendConfig.loaders['openstack'] = function (scope) {
        if (scope.openstack_providers == null) {
            AppService.post('/webmodule/openstack-getconfig', {'openstack-config': 'Providers'}).then(function (data) {
                scope.openstack_providers = data.data.Result;
                if (scope.openstack_server == undefined && scope.openstack_server_custom == undefined)
                    scope.openstack_server = 'https://identity.api.rackspacecloud.com/';

            }, AppUtils.connectionError);
        } else {
            if (scope.openstack_server == undefined && scope.openstack_server_custom == undefined)
                scope.openstack_server = 'https://identity.api.rackspacecloud.com/';
        }

        if (scope.openstack_versions == null) {
            AppService.post('/webmodule/openstack-getconfig', {'openstack-config': 'Versions'}).then(function (data) {
                scope.openstack_versions = data.data.Result;
                if (scope.openstack_version == undefined)
                    scope.openstack_version = 'v2.0';

            }, AppUtils.connectionError);
        } else {
            if (scope.openstack_version == undefined)
                scope.openstack_version = 'v2.0';
        }
    };


    EditUriBackendConfig.loaders['gcs'] = function (scope) {
        if (scope.gcs_locations == null) {
            AppService.post('/webmodule/gcs-getconfig', {'gcs-config': 'Locations'}).then(function (data) {
                scope.gcs_locations = data.data.Result;
                for (var k in scope.gcs_locations)
                    if (scope.gcs_locations[k] === null)
                        scope.gcs_locations[k] = '';

                if (scope.gcs_location == undefined && scope.gcs_location_custom == undefined)
                    scope.gcs_location = '';

            }, AppUtils.connectionError);
        }

        if (scope.gcs_storageclasses == null) {
            AppService.post('/webmodule/gcs-getconfig', {'gcs-config': 'StorageClasses'}).then(function (data) {
                scope.gcs_storageclasses = data.data.Result;
                for (var k in scope.gcs_storageclasses)
                    if (scope.gcs_storageclasses[k] === null)
                        scope.gcs_storageclasses[k] = '';

                if (scope.gcs_storageclass == undefined && scope.gcs_storageclass == undefined)
                    scope.gcs_storageclass = '';

            }, AppUtils.connectionError);
        }

        this['oauth-base'].apply(this, arguments);
    };

    // Parsers take a decomposed uri input and sets up the scope variables
    EditUriBackendConfig.parsers['file'] = function (scope, module, server, port, path, options) {
        if (scope.Path == null && scope.Server == null) {
            var ix = scope.uri.indexOf('?');
            if (ix < 0)
                ix = scope.uri.location;
            scope.Path = scope.uri.substr(0, ix);
        } else if (scope.Server.length == 1) {
            var ix = scope.uri.indexOf('?');
            if (ix < 0)
                ix = scope.uri.location;
            scope.Path = scope.uri.substring(scope.uri.indexOf('://') + 3, ix);
        } else {
            var dirsep = '/';
            var newScopePath = scope.Server;
            if (scope.Path.indexOf('\\') >= 0 || scope.Server.indexOf('\\') >= 0) {
                dirsep = '\\';
            }
            if (scope.Server.substr(scope.Server.length - 1) != dirsep) {
                newScopePath += dirsep;
            }
            if (scope.Path.length > 0) {
                newScopePath += scope.Path;
            }
            scope.Path = newScopePath;
        }
    };

    var s3_client_options = [{
        "name": "aws",
        "label": "Amazon AWS SDK"
    },
        {
            "name" : "minio",
            "label": "Minio SDK"
        }
    ];

    EditUriBackendConfig.parsers['s3'] = function (scope, module, server, port, path, options) {
        if (options['--aws-access-key-id'])
            scope.Username = options['--aws-access-key-id'];
        else if (options['--aws_access_key_id'])
            scope.Username = options['--aws_access_key_id'];
        if (options['--aws-secret-access-key'])
            scope.Password = options['--aws-secret-access-key'];
        else if (options['--aws_secret_access_key'])
            scope.Password = options['--aws_secret_access_key'];

        if (options['--s3-use-rrs'] && !options['--s3-storage-class']) {
            delete options['--s3-use-rrs'];
            options['--s3-storage-class'] = 'REDUCED_REDUNDANCY';
        }

        scope.s3_server = scope.s3_server_custom = options['--s3-server-name'];
        scope.s3_region = scope.s3_region_custom = options['--s3-location-constraint'];

        scope.s3_client_options = [{
            "name": "aws",
            "label": "Amazon AWS SDK"
        },
            {
                "name" : "minio",
                "label": "Minio SDK"
            }
        ];

        if ('--s3-client' in options) {
            var index = s3_client_options.map(function(e) {return e.name}).indexOf(options['--s3-client']);
            scope.s3_client = scope.s3_client_options[index];
        } else {
            scope.s3_client = scope.s3_client_options[0];
        }
        
        scope.s3_storageclass = scope.s3_storageclass_custom = options['--s3-storage-class'];

        var nukeopts = ['--aws-access-key-id', '--aws-secret-access-key', '--aws_access_key_id', '--aws_secret_access_key', '--s3-use-rrs', '--s3-server-name', '--s3-location-constraint', '--s3-storage-class', '--s3-client'];
        for (var x in nukeopts)
            delete options[nukeopts[x]];
    };

    EditUriBackendConfig.parsers['oauth-base'] = function (scope, module, server, port, path, options) {
        if (options['--authid'])
            scope.AuthID = options['--authid'];

        delete options['--authid'];

        EditUriBackendConfig.mergeServerAndPath(scope);
    };

    EditUriBackendConfig.parsers['googledrive'] = function() { return this['oauth-base'].apply(this, arguments); };
    EditUriBackendConfig.parsers['onedrive']    = function() { return this['oauth-base'].apply(this, arguments); };
    EditUriBackendConfig.parsers['onedrivev2']  = function() { return this['oauth-base'].apply(this, arguments); };
    EditUriBackendConfig.parsers['sharepoint']  = function() { return this['oauth-base'].apply(this, arguments); };
    EditUriBackendConfig.parsers['jottacloud']  = function() { return this['oauth-base'].apply(this, arguments); };
    EditUriBackendConfig.parsers['box']         = function() { return this['oauth-base'].apply(this, arguments); };
    EditUriBackendConfig.parsers['dropbox']     = function() { return this['oauth-base'].apply(this, arguments); };

    EditUriBackendConfig.parsers['openstack'] = function (scope, module, server, port, path, options) {
        scope.openstack_domainname = options['--openstack-domain-name'];
        scope.openstack_server = scope.openstack_server_custom = options['--openstack-authuri'];
        scope.openstack_version = options['--openstack-version'];
        scope.openstack_tenantname = options['--openstack-tenant-name'];
        scope.openstack_apikey = options['--openstack-apikey'];
        scope.openstack_region = options['--openstack-region'];

        var nukeopts = ['--openstack-domain-name', '--openstack-authuri', '--openstack-tenant-name', '--openstack-apikey', '--openstack-region', '--openstack-version'];
        for (var x in nukeopts)
            delete options[nukeopts[x]];

        EditUriBackendConfig.mergeServerAndPath(scope);
    };

    EditUriBackendConfig.parsers['azure'] = function (scope, module, server, port, path, options) {
        EditUriBackendConfig.mergeServerAndPath(scope);
    };

    EditUriBackendConfig.parsers['msgroup'] = function (scope, module, server, port, path, options) {

        scope.msgroup_group_email = options['--group-email'];

        var nukeopts = ['--group-email'];
        for (var x in nukeopts)
            delete options[nukeopts[x]];

        this['oauth-base'].apply(this, arguments);
    };

    EditUriBackendConfig.parsers['gcs'] = function (scope, module, server, port, path, options) {

        scope.gcs_location = scope.gcs_location_custom = options['--gcs-location'];
        scope.gcs_storageclass = scope.gcs_storageclass_custom = options['--gcs-storage-class'];
        scope.gcs_projectid = options['--gcs-project'];

        var nukeopts = ['--gcs-location', '--gcs-storage-class', '--gcs-project'];
        for (var x in nukeopts)
            delete options[nukeopts[x]];

        this['oauth-base'].apply(this, arguments);
    };

    EditUriBackendConfig.parsers['b2'] = function (scope, module, server, port, path, options) {
        if (options['--b2-accountid'])
            scope.Username = options['--b2-accountid'];
        if (options['--b2-applicationkey'])
            scope.Password = options['--b2-applicationkey'];

        var nukeopts = ['--b2-accountid', '--b2-applicationkey'];
        for (var x in nukeopts)
            delete options[nukeopts[x]];
    };

    EditUriBackendConfig.parsers['e2'] = function (scope, module, server, port, path, options) {
        if (options['--access-key-id'])
            scope.Username = options['--access-key-id'];
        if (options['--access-key-secret'])
            scope.Password = options['--access-key-secret'];

        var nukeopts = ['--access-key-id', '--access-key-secret'];
        for (var x in nukeopts)
            delete options[nukeopts[x]];
    };

    EditUriBackendConfig.parsers['mega'] = function (scope, module, server, port, path, options) {
        EditUriBackendConfig.mergeServerAndPath(scope);
    };

    EditUriBackendConfig.parsers['rclone'] = function (scope, module, server, port, path, options) {
        if (options['--rclone-local-repository'])
            scope.rclone_local_repository = options['--rclone-local-repository'];
        if (options['--rclone-remote-repository'])
            scope.rclone_remote_repository = options['--rclone-remote-repository'];
        if (options['--rclone-remote-path'])
            scope.rclone_remote_path = options['--rclone-remote-path'];

        var nukeopts = ['--rclone-option', '--rclone-executable', '--rclone-local-repository'];
        for (var x in nukeopts)
            delete options[nukeopts[x]];
    }


    EditUriBackendConfig.parsers['sia'] = function (scope, module, server, port, path, options) {
        if (options['--sia-targetpath'])
            scope.sia_targetpath = options['--sia-targetpath'];
        if (options['--sia-redundancy'])
            scope.sia_redundancy = options['--sia-redundancy'];
        if (options['--sia-password'])
            scope.sia_password = options['--sia-password'];

        var nukeopts = ['--sia-targetpath', '--sia-redundancy', '--sia-password'];
        for (var x in nukeopts)
            delete options[nukeopts[x]];
    }
	
    EditUriBackendConfig.parsers['storj'] = function (scope, module, server, port, path, options) {
        if (options['--storj-auth-method'])
            scope.storj_auth_method = options['--storj-auth-method'];
        if (options['--storj-satellite'])
            scope.storj_satellite = options['--storj-satellite'];
        if (options['--storj-api-key'])
            scope.storj_api_key = options['--storj-api-key'];
        if (options['--storj-secret'])
            scope.storj_secret = options['--storj-secret'];
        if (options['--storj-secret-verify'])
            scope.storj_secret_verify = options['--storj-secret-verify'];
        if (options['--storj-shared-access'])
            scope.storj_shared_access = options['--storj-shared-access'];
        if (options['--storj-bucket'])
            scope.storj_bucket = options['--storj-bucket'];
        if (options['--storj-folder'])
            scope.storj_folder = options['--storj-folder'];

        var nukeopts = ['--storj-auth-method','--storj-satellite', '--storj-api-key', '--storj-secret', '--storj-secret-verify', '--storj-shared-access', '--storj-bucket', '--storj-folder'];
        for (var x in nukeopts)
            delete options[nukeopts[x]];
    };
	
    EditUriBackendConfig.parsers['tardigrade'] = function (scope, module, server, port, path, options) {
        if (options['--tardigrade-auth-method'])
            scope.tardigrade_auth_method = options['--tardigrade-auth-method'];
        if (options['--tardigrade-satellite'])
            scope.tardigrade_satellite = options['--tardigrade-satellite'];
        if (options['--tardigrade-api-key'])
            scope.tardigrade_api_key = options['--tardigrade-api-key'];
        if (options['--tardigrade-secret'])
            scope.tardigrade_secret = options['--tardigrade-secret'];
        if (options['--tardigrade-secret-verify'])
            scope.tardigrade_secret_verify = options['--tardigrade-secret-verify'];
        if (options['--tardigrade-shared-access'])
            scope.tardigrade_shared_access = options['--tardigrade-shared-access'];
        if (options['--tardigrade-bucket'])
            scope.tardigrade_bucket = options['--tardigrade-bucket'];
        if (options['--tardigrade-folder'])
            scope.tardigrade_folder = options['--tardigrade-folder'];
		
        var nukeopts = ['--tardigrade-auth-method','--tardigrade-satellite', '--tardigrade-api-key', '--tardigrade-secret', '--tardigrade-secret-verify', '--tardigrade-shared-access', '--tardigrade-bucket', '--tardigrade-folder'];
        for (var x in nukeopts)
            delete options[nukeopts[x]];
    };


    EditUriBackendConfig.parsers['cos'] = function (scope, module, server, port, path, options) {
        if (options['--cos-app-id'])
            scope.cos_app_id = options['--cos-app-id'];
        if (options['--cos-region'])
            scope.cos_region = options['--cos-region'];
        if (options['--cos-secret-id'])
            scope.cos_secret_id = options['--cos-secret-id'];
		if (options['--cos-secret-key'])
            scope.cos_secret_key = options['--cos-secret-key'];
        if (options['--cos-bucket'])
            scope.cos_bucket = options['--cos-bucket'];

        var nukeopts = ['--cos-app-id', '--cos-region', '--cos-secret-id', '--cos-secret-key', '--cos-bucket'];
        for (var x in nukeopts)
            delete options[nukeopts[x]];
		
		EditUriBackendConfig.mergeServerAndPath(scope);
    }

    // Builders take the scope and produce the uri output
    EditUriBackendConfig.builders['s3'] = function (scope) {
        var opts = {
            's3-server-name': AppUtils.contains_value(scope.s3_providers, scope.s3_server) ? scope.s3_server : scope.s3_server_custom
        };

        if (scope.s3_region != null) {
            opts['s3-location-constraint'] = AppUtils.contains_value(scope.s3_regions, scope.s3_region) ? scope.s3_region : scope.s3_region_custom;}
        else if (scope.s3_region_custom != null) {
            opts['s3-location-constraint'] = scope.s3_region_custom;
        }
        
        if (scope.s3_storageclass != null)
            opts['s3-storage-class'] = AppUtils.contains_value(scope.s3_storageclasses, scope.s3_storageclass) ? scope.s3_storageclass : scope.s3_storageclass_custom;

        opts['s3-client']=scope.s3_client.name;
        
        EditUriBackendConfig.merge_in_advanced_options(scope, opts, true);

        var url = AppUtils.format('{0}{1}://{2}/{3}{4}',
            scope.Backend.Key,
            scope.UseSSL ? 's' : '',
            scope.Server || '',
            scope.Path || '',
            AppUtils.encodeDictAsUrl(opts)
        );

        return url;
    };


    EditUriBackendConfig.builders['file'] = function (scope) {
        var opts = {}
        EditUriBackendConfig.merge_in_advanced_options(scope, opts, true);
        var url = AppUtils.format('file://{0}{1}',
            scope.Path,
            AppUtils.encodeDictAsUrl(opts)
        );

        return url;
    };

    EditUriBackendConfig.builders['oauth-base'] = function (scope) {
        var opts = {
            'authid': scope.AuthID
        }
        EditUriBackendConfig.merge_in_advanced_options(scope, opts, false);

        var url = AppUtils.format('{0}{1}://{2}{3}',
            scope.Backend.Key,
            (scope.SupportsSSL && scope.UseSSL) ? 's' : '',
            scope.Path || '',
            AppUtils.encodeDictAsUrl(opts)
        );

        return url;
    };

    EditUriBackendConfig.builders['googledrive'] = function() { return this['oauth-base'].apply(this, arguments); };
    EditUriBackendConfig.builders['onedrive']    = function() { return this['oauth-base'].apply(this, arguments); };
    EditUriBackendConfig.builders['onedrivev2']  = function() { return this['oauth-base'].apply(this, arguments); };
    EditUriBackendConfig.builders['sharepoint']  = function() { return this['oauth-base'].apply(this, arguments); };
    EditUriBackendConfig.builders['msgroup']     = function() { return this['oauth-base'].apply(this, arguments); };
    EditUriBackendConfig.builders['jottacloud']  = function() { return this['oauth-base'].apply(this, arguments); };
    EditUriBackendConfig.builders['box']         = function() { return this['oauth-base'].apply(this, arguments); };
    EditUriBackendConfig.builders['dropbox']     = function() { return this['oauth-base'].apply(this, arguments); };

    EditUriBackendConfig.builders['openstack'] = function (scope) {
        var opts = {
            'openstack-domain-name': scope.openstack_domainname,
            'openstack-authuri': AppUtils.contains_value(scope.openstack_providers, scope.openstack_server) ? scope.openstack_server : scope.openstack_server_custom,
            'openstack-version': scope.openstack_version,
            'openstack-tenant-name': scope.openstack_tenantname,
            'openstack-apikey': scope.openstack_apikey,
            'openstack-region': scope.openstack_region
        };

        if ((opts['openstack-domain-name'] || '') == '')
            delete opts['openstack-domain-name'];
        if ((opts['openstack-tenant-name'] || '') == '')
            delete opts['openstack-tenant-name'];
        if ((opts['openstack-apikey'] || '') == '')
            delete opts['openstack-apikey'];
        if ((opts['openstack-region'] || '') == '')
            delete opts['openstack-region'];
        if ((opts['openstack-version'] || '') == '')
            delete opts['openstack-version'];

        EditUriBackendConfig.merge_in_advanced_options(scope, opts, false);

        var url = AppUtils.format('{0}://{1}{2}',
            scope.Backend.Key,
            scope.Path,
            AppUtils.encodeDictAsUrl(opts)
        );

        return url;
    };

    EditUriBackendConfig.builders['azure'] = function (scope) {
        var opts = {};

        EditUriBackendConfig.merge_in_advanced_options(scope, opts, true);

        // Slightly better error message
        scope.Folder = scope.Path;

        var url = AppUtils.format('{0}://{1}{2}',
            scope.Backend.Key,
            scope.Path,
            AppUtils.encodeDictAsUrl(opts)
        );

        return url;
    };

    EditUriBackendConfig.builders['msgroup'] = function (scope) {
        var opts = {
            'group-email': scope.msgroup_group_email,
            'authid': scope.AuthID,
        }

        if ((opts['group-email'] || '') == '')
            delete opts['group-email'];

        EditUriBackendConfig.merge_in_advanced_options(scope, opts, false);

        var url = AppUtils.format('{0}://{1}{2}',
            scope.Backend.Key,
            scope.Path || '',
            AppUtils.encodeDictAsUrl(opts)
        );

        return url;
    };

    EditUriBackendConfig.builders['gcs'] = function (scope) {
        var opts = {
            'gcs-location': AppUtils.contains_value(scope.gcs_locations, scope.gcs_location) ? scope.gcs_location : scope.gcs_location_custom,
            'gcs-storage-class': AppUtils.contains_value(scope.gcs_storageclasses, scope.gcs_storageclass) ? scope.gcs_storageclass : scope.gcs_storageclass_custom,
            'authid': scope.AuthID,
            'gcs-project': scope.gcs_projectid
        }

        if ((opts['gcs-location'] || '') == '')
            delete opts['gcs-location'];
        if ((opts['gcs-storage-class'] || '') == '')
            delete opts['gcs-storage-class'];
        if ((opts['gcs-project'] || '') == '')
            delete opts['gcs-project'];

        EditUriBackendConfig.merge_in_advanced_options(scope, opts, false);

        var url = AppUtils.format('{0}://{1}{2}',
            scope.Backend.Key,
            scope.Path || '',
            AppUtils.encodeDictAsUrl(opts)
        );

        return url;
    };

    EditUriBackendConfig.builders['b2'] = function (scope) {
        var opts = {};

        EditUriBackendConfig.merge_in_advanced_options(scope, opts, true);

        // Slightly better error message
        scope.Folder = scope.Server;

        var url = AppUtils.format('{0}://{1}/{2}{3}',
            scope.Backend.Key,
            scope.Server || '',
            scope.Path || '',
            AppUtils.encodeDictAsUrl(opts)
        );

        return url;
    };

    EditUriBackendConfig.builders['e2'] = function (scope) {
        var opts = {};

        EditUriBackendConfig.merge_in_advanced_options(scope, opts, true);

        // Slightly better error message
        scope.Folder = scope.Server;

        var url = AppUtils.format('{0}://{1}/{2}{3}',
            scope.Backend.Key,
            scope.Server || '',
            scope.Path || '',
            AppUtils.encodeDictAsUrl(opts)
        );

        return url;
    };

    EditUriBackendConfig.builders['mega'] = function (scope) {
        var opts = {};

        EditUriBackendConfig.merge_in_advanced_options(scope, opts, true);

        // Slightly better error message
        scope.Folder = scope.Path;

        var url = AppUtils.format('{0}://{1}{2}',
            scope.Backend.Key,
            scope.Path,
            AppUtils.encodeDictAsUrl(opts)
        );

        return url;
    };

    EditUriBackendConfig.builders['sia'] = function (scope) {
        var opts = {
            'sia-password': scope.sia_password,
            'sia-targetpath': scope.sia_targetpath,
            'sia-redundancy': scope.sia_redundancy
        };

        EditUriBackendConfig.merge_in_advanced_options(scope, opts, false);

        var url = AppUtils.format('{0}://{1}/{2}{3}',
            scope.Backend.Key,
            scope.Server || '',
            scope.sia_targetpath || '',
            AppUtils.encodeDictAsUrl(opts)
        );

        return url;
    }
	
	EditUriBackendConfig.builders['storj'] = function (scope) {
        var opts = {
            'storj-auth-method': scope.storj_auth_method,
            'storj-satellite': scope.storj_satellite,
            'storj-api-key': scope.storj_api_key,
            'storj-secret': scope.storj_secret,
            'storj-shared-access': scope.storj_shared_access,
            'storj-bucket': scope.storj_bucket,
            'storj-folder': scope.storj_folder
        };

        EditUriBackendConfig.merge_in_advanced_options(scope, opts, false);

        var url = AppUtils.format('{0}://storj.io/config{1}',
            scope.Backend.Key,
            AppUtils.encodeDictAsUrl(opts)
        );

        return url;
    };
	
	EditUriBackendConfig.builders['tardigrade'] = function (scope) {
        var opts = {
            'tardigrade-auth-method': scope.tardigrade_auth_method,
            'tardigrade-satellite': scope.tardigrade_satellite,
            'tardigrade-api-key': scope.tardigrade_api_key,
            'tardigrade-secret': scope.tardigrade_secret,
            'tardigrade-shared-access': scope.tardigrade_shared_access,
            'tardigrade-bucket': scope.tardigrade_bucket,
            'tardigrade-folder': scope.tardigrade_folder
        };

        EditUriBackendConfig.merge_in_advanced_options(scope, opts, false);

        var url = AppUtils.format('{0}://tardigrade.io/config{1}',
            scope.Backend.Key,
            AppUtils.encodeDictAsUrl(opts)
        );

        return url;
    };

    EditUriBackendConfig.builders['rclone'] = function (scope) {

        var opts = {
            'rclone-local-repository': scope.rclone_local_repository,
            'rclone-option': scope.rclone_option,
            'rclone-executable': scope.rclone_executable
        };

        if ((opts['rclone-executable'] || '') == '')
            delete opts['rclone-executable'];
        if ((opts['rclone-option'] || '') == '')
            delete opts['rclone-option'];


        EditUriBackendConfig.merge_in_advanced_options(scope, opts, false);

        var url = AppUtils.format('{0}://{1}/{2}{3}',
            scope.Backend.Key,
            scope.Server,
            scope.Path,
            AppUtils.encodeDictAsUrl(opts)
        );

        return url;


    }


    EditUriBackendConfig.builders['cos'] = function (scope) {
        var opts = {
            'cos-app-id': scope.cos_app_id,
            'cos-region': scope.cos_region,
            'cos-secret-id': scope.cos_secret_id,
			'cos-secret-key': scope.cos_secret_key,
			'cos-bucket': scope.cos_bucket
        };

        EditUriBackendConfig.merge_in_advanced_options(scope, opts, false);

        var url = AppUtils.format('{0}://{1}{2}',
            scope.Backend.Key,
            scope.Path || '',
            AppUtils.encodeDictAsUrl(opts)
        );
		
        return url;
    }

    EditUriBackendConfig.validaters['file'] = function (scope, continuation) {
        if (EditUriBackendConfig.require_path(scope))
            continuation();
    };

    EditUriBackendConfig.validaters['ftp'] = function (scope, continuation) {
        var res =
            EditUriBackendConfig.require_server(scope) &&
            EditUriBackendConfig.require_field(scope, 'Username', gettextCatalog.getString('Username'));

        if (res)
            EditUriBackendConfig.recommend_path(scope, function () {
                if ((scope.Password || '').trim().length == 0)
                    EditUriBackendConfig.show_warning_dialog(gettextCatalog.getString('It is possible to connect to some FTP without a password.\nAre you sure your FTP server supports password-less logins?'), continuation);
                else
                    continuation();
            });
    };

    EditUriBackendConfig.validaters['aftp'] = EditUriBackendConfig.validaters['ftp'];

    EditUriBackendConfig.validaters['ssh'] = function (scope, continuation) {
        var res =
            EditUriBackendConfig.require_server(scope) &&
            EditUriBackendConfig.require_username(scope);

        if (res)
            EditUriBackendConfig.recommend_path(scope, continuation);
    };

    EditUriBackendConfig.validaters['webdav'] = EditUriBackendConfig.validaters['ssh'];
    EditUriBackendConfig.validaters['cloudfiles'] = EditUriBackendConfig.validaters['ssh'];
    EditUriBackendConfig.validaters['tahoe'] = EditUriBackendConfig.validaters['ssh'];


    EditUriBackendConfig.validaters['authid-base'] = function (scope, continuation) {
        var res =
            EditUriBackendConfig.require_field(scope, 'AuthID', gettextCatalog.getString('AuthID'));

        if (res)
            EditUriBackendConfig.recommend_path(scope, continuation);
    };

    EditUriBackendConfig.validaters['googledrive'] = EditUriBackendConfig.validaters['authid-base'];
    EditUriBackendConfig.validaters['gcs']         = EditUriBackendConfig.validaters['authid-base'];
    EditUriBackendConfig.validaters['jottacloud']  = EditUriBackendConfig.validaters['authid-base'];
    EditUriBackendConfig.validaters['box']         = EditUriBackendConfig.validaters['authid-base'];
    EditUriBackendConfig.validaters['dropbox']     = EditUriBackendConfig.validaters['authid-base'];
    EditUriBackendConfig.validaters['onedrive']    = EditUriBackendConfig.validaters['authid-base'];
    EditUriBackendConfig.validaters['onedrivev2']  = EditUriBackendConfig.validaters['authid-base'];
    EditUriBackendConfig.validaters['sharepoint']  = EditUriBackendConfig.validaters['authid-base'];

    EditUriBackendConfig.validaters['msgroup'] = function (scope, continuation) {

        EditUriBackendConfig.validaters['authid-base'](scope, function () {
            var res =
                EditUriBackendConfig.recommend_field(scope, 'msgroup_group_email', gettextCatalog.getString('Group email'), gettextCatalog.getString(' unless you are explicitly specifying --group-id'), continuation);

            if (res)
                continuation();
        });
    };

    EditUriBackendConfig.validaters['msgroup'] = function (scope, continuation) {

        EditUriBackendConfig.validaters['authid-base'](scope, function () {
            var res =
                EditUriBackendConfig.recommend_field(scope, 'msgroup_group_email', gettextCatalog.getString('Group email'), gettextCatalog.getString(' unless you are explicitly specifying --group-id'), continuation);

            if (res)
                continuation();
        });
    };

    EditUriBackendConfig.validaters['azure'] = function (scope, continuation) {
        var res =
            EditUriBackendConfig.require_field(scope, 'Username', gettextCatalog.getString('Account name')) &&
            EditUriBackendConfig.require_field(scope, 'Password', gettextCatalog.getString('Access Key')) &&
            EditUriBackendConfig.require_field(scope, 'Path', gettextCatalog.getString('Container name'));

        if (res)
            continuation();
    };

    EditUriBackendConfig.validaters['openstack'] = function (scope, continuation) {
        var res =
            EditUriBackendConfig.require_field(scope, 'Username', gettextCatalog.getString('Username')) &&
            EditUriBackendConfig.require_field(scope, 'Path', gettextCatalog.getString('Bucket Name'));

        if (res && (scope['openstack_server'] || '').trim().length == 0 && (scope['openstack_server_custom'] || '').trim().length == 0)
            res = EditUriBackendConfig.show_error_dialog(gettextCatalog.getString('You must select or fill in the AuthURI'));

        if (((scope.openstack_version) || '').trim() == 'v3') {
            if (res && (scope.Password || '').trim().length == 0)
                res = EditUriBackendConfig.show_error_dialog(gettextCatalog.getString('You must enter a password to use v3 API'));

            if (res && ((scope.openstack_domainname) || '').trim().length == 0)
                res = EditUriBackendConfig.show_error_dialog(gettextCatalog.getString('You must enter a domain name to use v3 API'));

            if (res && ((scope.openstack_tenantname) || '').trim().length == 0)
                res = EditUriBackendConfig.show_error_dialog(gettextCatalog.getString('You must enter a tenant (aka project) name to use v3 API'));

            if (res && (scope.openstack_apikey || '').trim().length != 0)
                res = EditUriBackendConfig.show_error_dialog(gettextCatalog.getString('Openstack API Key are not supported in v3 keystone API.'));


        } else {

            if (((scope.openstack_apikey) || '').trim().length == 0) {

                if (res && (scope.Password || '').trim().length == 0)
                    res = EditUriBackendConfig.show_error_dialog(gettextCatalog.getString('You must enter either a password or an API Key'));

                if (res && ((scope.openstack_tenantname) || '').trim().length == 0)
                    res = EditUriBackendConfig.show_error_dialog(gettextCatalog.getString('You must enter a tenant name if you do not provide an API Key'));

            } else {
                if (res && (scope.Password || '').trim().length != 0)
                    res = EditUriBackendConfig.show_error_dialog(gettextCatalog.getString('You must enter either a password or an API Key, not both'));
            }
        }
        if (res)
            continuation();
    };

    EditUriBackendConfig.validaters['s3'] = function (scope, continuation) {
        var res =
            EditUriBackendConfig.require_field(scope, 'Server', gettextCatalog.getString('Bucket Name')) &&
            EditUriBackendConfig.require_field(scope, 'Username', gettextCatalog.getString('AWS Access ID')) &&
            EditUriBackendConfig.require_field(scope, 'Password', gettextCatalog.getString('AWS Access Key'));

        if (res && (scope['s3_server'] || '').trim().length == 0 && (scope['s3_server_custom'] || '').trim().length == 0)
            res = EditUriBackendConfig.show_error_dialog(gettextCatalog.getString('You must select or fill in the server'));


        if (res) {

            function checkUsernamePrefix() {
                if (scope.Server.toLowerCase().indexOf(scope.Username.toLowerCase()) != 0 && (scope.s3_bucket_check_name != scope.Server || scope.s3_bucket_check_user != scope.Username)) {
                    DialogService.dialog(gettextCatalog.getString('Adjust bucket name?'), gettextCatalog.getString('The bucket name should start with your username, prepend automatically?'), [gettextCatalog.getString('Cancel'), gettextCatalog.getString('No'), gettextCatalog.getString('Yes')], function (ix) {
                        if (ix == 2)
                            scope.Server = scope.Username.toLowerCase() + '-' + scope.Server;
                        if (ix == 1 || ix == 2) {
                            scope.s3_bucket_check_name = scope.Server;
                            scope.s3_bucket_check_user = scope.Username;
                            continuation();
                        }
                    });
                } else {
                    continuation();
                }
            }

            function checkLowerCase() {
                if (scope.Server.toLowerCase() != scope.Server) {
                    DialogService.dialog(gettextCatalog.getString('Adjust bucket name?'), gettextCatalog.getString('The bucket name should be all lower-case, convert automatically?'), [gettextCatalog.getString('Cancel'), gettextCatalog.getString('No'), gettextCatalog.getString('Yes')], function (ix) {
                        if (ix == 2)
                            scope.Server = scope.Server.toLowerCase();

                        if (ix == 1 || ix == 2)
                            checkUsernamePrefix();
                    });
                } else {
                    checkUsernamePrefix();
                }
            };

            checkLowerCase();
        }
    };

    EditUriBackendConfig.validaters['b2'] = function (scope, continuation) {
        var res =
            EditUriBackendConfig.require_field(scope, 'Server', gettextCatalog.getString('Bucket Name')) &&
            EditUriBackendConfig.require_field(scope, 'Username', gettextCatalog.getString('B2 Cloud Storage Account ID')) &&
            EditUriBackendConfig.require_field(scope, 'Password', gettextCatalog.getString('B2 Cloud Storage Application Key'));

        if (res) {
            var re = new RegExp('[^A-Za-z0-9-]');
            var bucketname = scope['Server'] || '';
            var ix = bucketname.search(/[^A-Za-z0-9-]/g);

            if (ix >= 0) {
                EditUriBackendConfig.show_error_dialog(gettextCatalog.getString('The \'{{fieldname}}\' field contains an invalid character: {{character}} (value: {{value}}, index: {{pos}})', {
                    value: bucketname[ix].charCodeAt(),
                    pos: ix,
                    character: bucketname[ix],
                    fieldname: gettextCatalog.getString('Bucket Name')
                }));
                res = false;
            }
        }

        if (res) {
            var pathname = scope['Path'] || '';
            for (var i = pathname.length - 1; i >= 0; i--) {
                var char = pathname.charCodeAt(i);

                if (char == '\\'.charCodeAt(0) || char == 127 || char < 32) {
                    EditUriBackendConfig.show_error_dialog(gettextCatalog.getString('The \'{{fieldname}}\' field contains an invalid character: {{character}} (value: {{value}}, index: {{pos}})', {
                        value: char,
                        pos: i,
                        character: pathname[i],
                        fieldname: gettextCatalog.getString('Path')
                    }));
                    res = false;
                    break;
                }
            }
        }

        if (res)
            continuation();
    };

    EditUriBackendConfig.validaters['mega'] = function (scope, continuation) {
        scope.Path = scope.Path || '';
        var res =
            EditUriBackendConfig.require_field(scope, 'Username', gettextCatalog.getString('Username')) &&
            EditUriBackendConfig.require_field(scope, 'Password', gettextCatalog.getString('Password'));

        if (res)
            continuation();
    };

    EditUriBackendConfig.validaters['sia'] = function (scope, continuation) {
        var res =
            EditUriBackendConfig.require_field(scope, 'Server', gettextCatalog.getString('Server'));

        var re = new RegExp('^(([a-zA-Z0-9-])|(\/(?!\/)))*$');
        if (res && !re.test(scope['sia_targetpath'])) {
            res = EditUriBackendConfig.show_error_dialog(gettextCatalog.getString('Invalid characters in path'));
        }

        if (res && (scope['sia_redundancy'] || '').trim().length == 0 || parseFloat(scope['sia_redundancy']) < 1.0)
            res = EditUriBackendConfig.show_error_dialog(gettextCatalog.getString('Minimum redundancy is 1.0'));

        if (res)
            continuation();
    };
	
	EditUriBackendConfig.validaters['storj'] = function (scope, continuation) {
		var res = true;
		
		if(res && !scope['storj_auth_method']){
			res = EditUriBackendConfig.require_field(scope, 'storj_auth_method', gettextCatalog.getString('Authentication method'));
		}

		if(res && scope['storj_auth_method'] == 'Access grant'){
			res = EditUriBackendConfig.require_field(scope, 'storj_shared_access', gettextCatalog.getString('storj_shared_access')) &&
				  EditUriBackendConfig.require_field(scope, 'storj_bucket', gettextCatalog.getString('Bucket'));
		}
		
		if(res && scope['storj_auth_method'] == 'API key'){
			res = EditUriBackendConfig.require_field(scope, 'storj_api_key', gettextCatalog.getString('API key')) &&
				  EditUriBackendConfig.require_field(scope, 'storj_secret', gettextCatalog.getString('Encryption passphrase')) &&
				  EditUriBackendConfig.require_field(scope, 'storj_bucket', gettextCatalog.getString('Bucket'));
		}
		
		if(res && scope['storj_auth_method'] == 'API key' && !scope['storj_satellite']){
			res = EditUriBackendConfig.require_field(scope, 'storj_satellite_custom', gettextCatalog.getString('Custom Satellite'));
		}

		if(res && scope['storj_auth_method'] == 'API key' && scope['storj_secret'] != scope['storj_secret_verify'])
			res = EditUriBackendConfig.show_error_dialog(gettextCatalog.getString('The encryption passphrases do not match'));
		
		var re = new RegExp('^([a-z0-9]+([a-z0-9\-][a-z0-9])*)+(.[a-z0-9]+([a-z0-9\-][a-z0-9])*)*$');
		if(res && scope['storj_bucket'] && (!re.test(scope['storj_bucket']) || !(scope['storj_bucket'].length > 2 && scope['storj_bucket'].length < 64))){
			res = EditUriBackendConfig.show_error_dialog(gettextCatalog.getString('Bucket name can only be between 3 and 63 characters long and contain only lower-case characters, numbers, periods and dashes'));
		}
		
		if (res)
            continuation();
    };
	
	EditUriBackendConfig.validaters['tardigrade'] = function (scope, continuation) {
		var res = true;
		
		if(res && !scope['tardigrade_auth_method']){
			res = EditUriBackendConfig.require_field(scope, 'tardigrade_auth_method', gettextCatalog.getString('Authentication method'));
		}

		if(res && scope['tardigrade_auth_method'] == 'Access grant'){
			res = EditUriBackendConfig.require_field(scope, 'tardigrade_shared_access', gettextCatalog.getString('tardigrade_shared_access')) &&
				  EditUriBackendConfig.require_field(scope, 'tardigrade_bucket', gettextCatalog.getString('Bucket'));
		}
		
		if(res && scope['tardigrade_auth_method'] == 'API key'){
			res = EditUriBackendConfig.require_field(scope, 'tardigrade_api_key', gettextCatalog.getString('API key')) &&
				  EditUriBackendConfig.require_field(scope, 'tardigrade_secret', gettextCatalog.getString('Encryption passphrase')) &&
				  EditUriBackendConfig.require_field(scope, 'tardigrade_bucket', gettextCatalog.getString('Bucket'));
		}
		
		if(res && scope['tardigrade_auth_method'] == 'API key' && !scope['tardigrade_satellite']){
			res = EditUriBackendConfig.require_field(scope, 'tardigrade_satellite_custom', gettextCatalog.getString('Custom Satellite'));
		}

		if(res && scope['tardigrade_auth_method'] == 'API key' && scope['tardigrade_secret'] != scope['tardigrade_secret_verify'])
			res = EditUriBackendConfig.show_error_dialog(gettextCatalog.getString('The encryption passphrases do not match'));
		
		var re = new RegExp('^([a-z0-9]+([a-z0-9\-][a-z0-9])*)+(.[a-z0-9]+([a-z0-9\-][a-z0-9])*)*$');
		if(res && scope['tardigrade_bucket'] && (!re.test(scope['tardigrade_bucket']) || !(scope['tardigrade_bucket'].length > 2 && scope['tardigrade_bucket'].length < 64))){
			res = EditUriBackendConfig.show_error_dialog(gettextCatalog.getString('Bucket name can only be between 3 and 63 characters long and contain only lower-case characters, numbers, periods and dashes'));
		}
		
		if (res)
            continuation();
    };

    EditUriBackendConfig.validaters['rclone'] = function (scope, continuation) {
        var res =
            EditUriBackendConfig.require_field(scope, 'Server', gettextCatalog.getString('Remote Repository')) &&
            EditUriBackendConfig.require_field(scope, 'rclone_local_repository', gettextCatalog.getString('Local Repository')) &&
            EditUriBackendConfig.require_field(scope, 'Path', gettextCatalog.getString('Remote Path'));

        if (res)
            continuation();
    };

	EditUriBackendConfig.validaters['cos'] = function (scope, continuation) {
		var res =
            EditUriBackendConfig.require_field(scope, 'cos_app_id', gettextCatalog.getString('cos_app_id')) &&
            EditUriBackendConfig.require_field(scope, 'cos_secret_id', gettextCatalog.getString('cos_secret_id')) &&
            EditUriBackendConfig.require_field(scope, 'cos_secret_key', gettextCatalog.getString('cos_secret_key')) &&
            EditUriBackendConfig.require_field(scope, 'cos_region', gettextCatalog.getString('cos_region')) &&
            EditUriBackendConfig.require_field(scope, 'cos_bucket', gettextCatalog.getString('cos_bucket'));
			
		if (res)
            continuation();
    };

    EditUriBackendConfig.validaters['e2'] = function (scope, continuation) {
        var res =
                EditUriBackendConfig.require_field(scope, 'Username', gettextCatalog.getString('Idrivee2 Access Key Id')) &&
            EditUriBackendConfig.require_field(scope, 'Password', gettextCatalog.getString('Idrivee2 Access Key Secret')) &&
            EditUriBackendConfig.require_field(scope, 'Server', gettextCatalog.getString('Bucket Name'));

        if (res) {
            var re = new RegExp('[^A-Za-z0-9-]');
            var bucketname = scope['Server'] || '';
            var ix = bucketname.search(/[^A-Za-z0-9-]/g);

            if (ix >= 0) {
                EditUriBackendConfig.show_error_dialog(gettextCatalog.getString('The \'{{fieldname}}\' field contains an invalid character: {{character}} (value: {{value}}, index: {{pos}})', {
                    value: bucketname[ix].charCodeAt(),
                    pos: ix,
                    character: bucketname[ix],
                    fieldname: gettextCatalog.getString('Bucket Name')
                }));
                res = false;
            }
        }

        if (res) {
            var pathname = scope['Path'] || '';
            for (var i = pathname.length - 1; i >= 0; i--) {
                var char = pathname.charCodeAt(i);

                if (char == '\\'.charCodeAt(0) || char == 127 || char < 32) {
                    EditUriBackendConfig.show_error_dialog(gettextCatalog.getString('The \'{{fieldname}}\' field contains an invalid character: {{character}} (value: {{value}}, index: {{pos}})', {
                        value: char,
                        pos: i,
                        character: pathname[i],
                        fieldname: gettextCatalog.getString('Path')
                    }));
                    res = false;
                    break;
                }
            }
        }

        if (res)
            continuation();
    };
});
