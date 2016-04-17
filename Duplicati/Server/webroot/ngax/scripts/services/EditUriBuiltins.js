backupApp.service('EditUriBuiltins', function(AppService, AppUtils, SystemInfo, EditUriBackendConfig, DialogService, $http) {

	EditUriBackendConfig.mergeServerAndPath = function(scope) {
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
	EditUriBackendConfig.templates['hubic']       = 'templates/backends/oauth.html';
	EditUriBackendConfig.templates['onedrive']    = 'templates/backends/oauth.html';
	EditUriBackendConfig.templates['amzcd']       = 'templates/backends/oauth.html';
	EditUriBackendConfig.templates['openstack']   = 'templates/backends/openstack.html';
	EditUriBackendConfig.templates['azure']       = 'templates/backends/azure.html';
	EditUriBackendConfig.templates['gcs']         = 'templates/backends/gcs.html';
	EditUriBackendConfig.templates['b2']          = 'templates/backends/b2.html';
	EditUriBackendConfig.templates['mega']        = 'templates/backends/mega.html';
	EditUriBackendConfig.templates['box']         = 'templates/backends/oauth.html';

	// Loaders are a way for backends to request extra data from the server
	EditUriBackendConfig.loaders['s3'] = function(scope) {
		if (scope.s3_providers == null) {
			AppService.post('/webmodule/s3-getconfig', {'s3-config': 'Providers'}).then(function(data) {
				scope.s3_providers = data.data.Result;
				if (scope.s3_server == undefined && scope.s3_server_custom == undefined)
					scope.s3_server = 's3.amazonaws.com';

			}, AppUtils.connectionError);
		} else {
			if (scope.s3_server == undefined && scope.s3_server_custom == undefined)
				scope.s3_server = 's3.amazonaws.com';
		}

		if (scope.s3_regions == null) {
			AppService.post('/webmodule/s3-getconfig', {'s3-config': 'Regions'}).then(function(data) {
				scope.s3_regions = data.data.Result;
				if (scope.s3_region == null  && scope.s3_region_custom == undefined)
					scope.s3_region = '';
			}, AppUtils.connectionError);
		} else {
			if (scope.s3_region == null  && scope.s3_region_custom == undefined)
				scope.s3_region = '';
		}

		if (scope.s3_storageclasses == null) {
			AppService.post('/webmodule/s3-getconfig', {'s3-config': 'StorageClasses'}).then(function(data) {
				scope.s3_storageclasses = data.data.Result;
				if (scope.s3_storageclass == null  && scope.s3_storageclass_custom == undefined)
					scope.s3_storageclass = '';
			}, AppUtils.connectionError);
		} else {
			if (scope.s3_storageclass == null  && scope.s3_storageclass_custom == undefined)
				scope.s3_storageclass = '';
		}

	};

	EditUriBackendConfig.loaders['oauth-base'] = function(scope) {
		scope.oauth_create_token = Math.random().toString(36).substr(2) + Math.random().toString(36).substr(2);
		scope.oauth_service_link = 'https://duplicati-oauth-handler.appspot.com/';
		scope.oauth_start_link = scope.oauth_service_link + '?type=' + scope.Backend.Key + '&token=' + scope.oauth_create_token;
		scope.oauth_in_progress = false;

		scope.oauth_start_token_creation = function() {

            scope.oauth_in_progress = true;

            var w = 450;
            var h = 600;

            var url = scope.oauth_start_link;

            var countDown = 100;
            var ft = scope.oauth_create_token;
            var left = (screen.width/2)-(w/2);
            var top = (screen.height/2)-(h/2);
            var wnd = window.open(url, '_blank', 'height=' + h +',width=' + w + ',menubar=0,status=0,titlebar=0,toolbar=0,left=' + left + ',top=' + top)

            var recheck = function() {
                countDown--;
                if (countDown > 0 && ft == scope.oauth_create_token) {
                    $http.jsonp(scope.oauth_service_link + 'fetch?callback=JSON_CALLBACK', { params: {'token': ft} }).then(
                    	function(response) {
                    		if (response.data.authid) {
                    			scope.AuthID = response.data.authid;
		                    	scope.oauth_in_progress = false;
                    			wnd.close();
                    		} else {
                    			setTimeout(recheck, 3000);
                    		}
                    	},
                    	function(response) {
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
	EditUriBackendConfig.loaders['hubic']       = function() { return this['oauth-base'].apply(this, arguments); };
	EditUriBackendConfig.loaders['onedrive']    = function() { return this['oauth-base'].apply(this, arguments); };
	EditUriBackendConfig.loaders['amzcd']       = function() { return this['oauth-base'].apply(this, arguments); };
	EditUriBackendConfig.loaders['box']         = function() { return this['oauth-base'].apply(this, arguments); };

	EditUriBackendConfig.loaders['openstack'] = function(scope) {
		if (scope.openstack_providers == null) {
			AppService.post('/webmodule/openstack-getconfig', {'openstack-config': 'Providers'}).then(function(data) {
				scope.openstack_providers = data.data.Result;

			}, AppUtils.connectionError);
		}
	};

	EditUriBackendConfig.loaders['gcs'] = function(scope) {
		if (scope.gcs_locations == null) {
			AppService.post('/webmodule/gcs-getconfig', {'gcs-config': 'Locations'}).then(function(data) {
				scope.gcs_locations = data.data.Result;
				for(var k in scope.gcs_locations)
					if (scope.gcs_locations[k] === null)
						scope.gcs_locations[k] = '';

				if (scope.gcs_location == undefined && scope.gcs_location_custom == undefined)
					scope.gcs_location = '';

			}, AppUtils.connectionError);
		}

		if (scope.gcs_storageclasses == null) {
			AppService.post('/webmodule/gcs-getconfig', {'gcs-config': 'StorageClasses'}).then(function(data) {
				scope.gcs_storageclasses = data.data.Result;
				for(var k in scope.gcs_storageclasses)
					if (scope.gcs_storageclasses[k] === null)
						scope.gcs_storageclasses[k] = '';

				if (scope.gcs_storageclass == undefined && scope.gcs_storageclass == undefined)
					scope.gcs_storageclass = '';

			}, AppUtils.connectionError);
		}

		this['oauth-base'].apply(this, arguments);
	};


	// Parsers take a decomposed uri input and sets up the scope variables
	EditUriBackendConfig.parsers['file'] = function(scope, module, server, port, path, options) {
		var dirsep = '/';
		if (scope.Path == null && scope.Server == null)	{
			var ix = scope.uri.indexOf('?');
			if (ix < 0)
				ix = scope.uri.location;
			scope.Path = scope.uri.substr(0, ix);
		} else if (scope.Server.length == 1) {
			var ix = scope.uri.indexOf('?');
			if (ix < 0)
				ix = scope.uri.location;
			scope.Path = scope.uri.substr(scope.uri.indexOf('://') + 3, ix);

		} else {
			if (scope.Path != null && scope.Path.indexOf('\\') >= 0)
				dirsep = '\\';

			if (scope.Server != null && dirsep == '/')
				scope.Path = scope.Server + '/' + scope.Path;
		}
	};


	EditUriBackendConfig.parsers['s3'] = function(scope, module, server, port, path, options) {
		if (options['--aws_access_key_id'])
			scope.Username = options['--aws_access_key_id'];
		if (options['--aws_secret_access_key'])
			scope.Password = options['--aws_secret_access_key'];

		if (options['--s3-use-rrs'] && !options['--s3-storage-class']) {
			delete options['--s3-use-rrs'];
			options['--s3-storage-class'] = 'REDUCED_REDUNDANCY';
		}

		scope.s3_server = scope.s3_server_custom = options['--s3-server-name'];
		scope.s3_region = scope.s3_region_custom = options['--s3-location-constraint'];
		scope.s3_storageclass = scope.s3_storageclass_custom = options['--s3-storage-class'];

		var nukeopts = ['--aws_access_key_id', '--aws_secret_access_key', '--s3-use-rrs', '--s3-server-name', '--s3-location-constraint', '--s3-storage-class'];
		for(var x in nukeopts)
			delete options[nukeopts[x]];
	};

	EditUriBackendConfig.parsers['oauth-base'] = function(scope, module, server, port, path, options) {
		if (options['--authid'])
			scope.AuthID = options['--authid'];

		delete options['--authid'];

		EditUriBackendConfig.mergeServerAndPath(scope);
	};

	EditUriBackendConfig.parsers['googledrive'] = function() { return this['oauth-base'].apply(this, arguments); };
	EditUriBackendConfig.parsers['hubic']       = function() { return this['oauth-base'].apply(this, arguments); };
	EditUriBackendConfig.parsers['onedrive']    = function() { return this['oauth-base'].apply(this, arguments); };
	EditUriBackendConfig.parsers['amzcd']       = function() { return this['oauth-base'].apply(this, arguments); };
	EditUriBackendConfig.parsers['box']         = function() { return this['oauth-base'].apply(this, arguments); };

	EditUriBackendConfig.parsers['openstack'] = function(scope, module, server, port, path, options) {
		scope.openstack_server = scope.openstack_server_custom = options['--openstack-authuri'];
		scope.openstack_tenantname = options['--openstack-tenant-name'];
		scope.openstack_apikey = options['--openstack-apikey'];
		scope.openstack_region = options['--openstack-region'];

		var nukeopts = ['--openstack-authuri', '--openstack-tenant-name', '--openstack-apikey', '--openstack-region'];
		for(var x in nukeopts)
			delete options[nukeopts[x]];

		EditUriBackendConfig.mergeServerAndPath(scope);
	};

	EditUriBackendConfig.parsers['azure'] = function(scope, module, server, port, path, options) {
		EditUriBackendConfig.mergeServerAndPath(scope);
	};

	EditUriBackendConfig.parsers['gcs'] = function(scope, module, server, port, path, options) {

		scope.gcs_location = scope.gcs_location_custom = options['--gcs-location'];
		scope.gcs_storageclass = scope.gcs_storageclass_custom = options['--gcs-storage-class'];
		scope.gcs_projectid = options['--gcs-project'];

		var nukeopts = ['--gcs-location', '--gcs-storage-class', '--gcs-project'];
		for(var x in nukeopts)
			delete options[nukeopts[x]];

		this['oauth-base'].apply(this, arguments);
	};

	EditUriBackendConfig.parsers['b2'] = function(scope, module, server, port, path, options) {
		if (options['--b2-accountid'])
			scope.Username = options['--b2-accountid'];
		if (options['--b2-applicationkey'])
			scope.Password = options['--b2-applicationkey'];

		var nukeopts = ['--b2-accountid', '--b2-applicationkey'];
		for(var x in nukeopts)
			delete options[nukeopts[x]];
	};

	EditUriBackendConfig.parsers['mega'] = function(scope, module, server, port, path, options) {
		EditUriBackendConfig.mergeServerAndPath(scope);
	};

	// Builders take the scope and produce the uri output
	EditUriBackendConfig.builders['s3'] = function(scope) {
		var opts = {
			's3-server-name': AppUtils.contains_value(scope.s3_providers, scope.s3_server) ? scope.s3_server : scope.s3_server_custom
		};

		if (scope.s3_region != null)
			opts['s3-location-constraint'] = AppUtils.contains_value(scope.s3_regions, scope.s3_region) ? scope.s3_region : scope.s3_region_custom;
		if (scope.s3_storageclass != null)
			opts['s3-storage-class'] = AppUtils.contains_value(scope.s3_storageclasses, scope.s3_storageclass) ? scope.s3_storageclass : scope.s3_storageclass_custom;

		EditUriBackendConfig.merge_in_advanced_options(scope, opts);

		var url = AppUtils.format('{0}{1}://{2}/{3}{4}',
			scope.Backend.Key,
			scope.UseSSL ? 's' : '',
			scope.Server || '',
			scope.Path || '',
			AppUtils.encodeDictAsUrl(opts)
		);

		return url;
	};


	EditUriBackendConfig.builders['file'] = function(scope) {
		var opts = {}
		EditUriBackendConfig.merge_in_advanced_options(scope, opts);
		var url = AppUtils.format('file://{0}{1}',
			scope.Path,
			AppUtils.encodeDictAsUrl(opts)
		);

		return url;
	};

	EditUriBackendConfig.builders['oauth-base'] = function(scope) {
		var opts = {
			'authid': scope.AuthID
		}
		EditUriBackendConfig.merge_in_advanced_options(scope, opts);

		var url = AppUtils.format('{0}{1}://{2}{3}',
			scope.Backend.Key,
			(scope.SupportsSSL && scope.UseSSL) ? 's' : '',
			scope.Path || '',
			AppUtils.encodeDictAsUrl(opts)
		);

		return url;
	};

	EditUriBackendConfig.builders['googledrive'] = function() { return this['oauth-base'].apply(this, arguments); };
	EditUriBackendConfig.builders['hubic']       = function() { return this['oauth-base'].apply(this, arguments); };
	EditUriBackendConfig.builders['onedrive']    = function() { return this['oauth-base'].apply(this, arguments); };
	EditUriBackendConfig.builders['amzcd']       = function() { return this['oauth-base'].apply(this, arguments); };
	EditUriBackendConfig.builders['box']         = function() { return this['oauth-base'].apply(this, arguments); };


	EditUriBackendConfig.builders['openstack'] = function(scope) {
		var opts = {
			'openstack-authuri': AppUtils.contains_value(scope.openstack_providers, scope.openstack_server) ? scope.openstack_server : scope.openstack_server_custom,
			'openstack-tenant-name': scope.openstack_tenantname,
			'openstack-apikey': scope.openstack_apikey,
			'openstack-region': scope.openstack_region
		};

		if ((opts['openstack-tenant-name'] || '') == '')
			delete opts['openstack-tenant-name'];
		if ((opts['openstack-apikey'] || '') == '')
			delete opts['openstack-apikey'];
		if ((opts['openstack-region'] || '') == '')
			delete opts['openstack-region'];

		EditUriBackendConfig.merge_in_advanced_options(scope, opts);

		var url = AppUtils.format('{0}://{1}{2}',
			scope.Backend.Key,
			scope.Path,
			AppUtils.encodeDictAsUrl(opts)
		);

		return url;
	};

	EditUriBackendConfig.builders['azure'] = function(scope) {
		var opts = { };

		EditUriBackendConfig.merge_in_advanced_options(scope, opts);

		// Slightly better error message
		scope.Folder = scope.Path;

		var url = AppUtils.format('{0}://{1}{2}',
			scope.Backend.Key,
			scope.Path,
			AppUtils.encodeDictAsUrl(opts)
		);

		return url;
	};

	EditUriBackendConfig.builders['gcs'] = function(scope) {
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

		EditUriBackendConfig.merge_in_advanced_options(scope, opts);

		var url = AppUtils.format('{0}://{1}{2}',
			scope.Backend.Key,
			scope.Path || '',
			AppUtils.encodeDictAsUrl(opts)
		);

		return url;
	};

	EditUriBackendConfig.builders['b2'] = function(scope) {
		var opts = { };

		EditUriBackendConfig.merge_in_advanced_options(scope, opts);

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

	EditUriBackendConfig.builders['mega'] = function(scope) {
		var opts = { };

		EditUriBackendConfig.merge_in_advanced_options(scope, opts);

		// Slightly better error message
		scope.Folder = scope.Path;

		var url = AppUtils.format('{0}://{1}{2}',
			scope.Backend.Key,
			scope.Path,
			AppUtils.encodeDictAsUrl(opts)
		);

		return url;
	};

	EditUriBackendConfig.validaters['file'] = function(scope, continuation) {
		if (EditUriBackendConfig.require_path(scope))
			continuation();
	};

	EditUriBackendConfig.validaters['ftp'] = function (scope, continuation) {
	    var res =
			EditUriBackendConfig.require_server(scope) &&
			EditUriBackendConfig.require_field(scope, 'Username', 'username');

	    if (res)
	        EditUriBackendConfig.recommend_path(scope, function () {
	            if ((scope.Password || '').trim().length == 0)
	                EditUriBackendConfig.show_warning_dialog('It is possible to connect to some FTP without a password.\nAre you sure your FTP server supports password-less logins?', continuation);
	            else
	                continuation();
	        });
	};

	EditUriBackendConfig.validaters['aftp'] = function (scope, continuation) {
	    var res =
			EditUriBackendConfig.require_server(scope) &&
			EditUriBackendConfig.require_field(scope, 'Username', 'username');

	    if (res)
	        EditUriBackendConfig.recommend_path(scope, function () {
	            if ((scope.Password || '').trim().length == 0)
	                EditUriBackendConfig.show_warning_dialog('It is possible to connect to some FTP without a password.\nAre you sure your FTP server supports password-less logins?', continuation);
	            else
	                continuation();
	        });
	};

	EditUriBackendConfig.validaters['ssh'] = function(scope, continuation) {
		var res =
			EditUriBackendConfig.require_server(scope) &&
			EditUriBackendConfig.require_username_and_password(scope);

		if (res)
			EditUriBackendConfig.recommend_path(scope, continuation);
	};

	EditUriBackendConfig.validaters['webdav'] = EditUriBackendConfig.validaters['ssh'];
	EditUriBackendConfig.validaters['cloudfiles'] = EditUriBackendConfig.validaters['ssh'];
	EditUriBackendConfig.validaters['tahoe'] = EditUriBackendConfig.validaters['ssh'];


	EditUriBackendConfig.validaters['onedrive'] = function(scope, continuation) {
		var res =
			EditUriBackendConfig.require_field(scope, 'AuthID', 'AuthID');

		if (res)
			EditUriBackendConfig.recommend_path(scope, continuation);
	};

	EditUriBackendConfig.validaters['hubic']       = function(scope, continuation) {

		var prefix = 'HubiC-DeskBackup_Duplicati/';

		EditUriBackendConfig.validaters['onedrive'](scope, function() {

			var p = (scope.Path || '').trim();

			if (p.length > 0 && p.indexOf('default/') != 0 && p.indexOf(prefix) != 0) {
				DialogService.dialog('Adjust path name?', 'The path should start with "' + prefix + '" or "default", otherwise you will not be able to see the files in the HubiC web interface.\n\nDo you want to add the prefix to the path automatically?', ['Cancel', 'No', 'Yes'], function(ix) {
					if (ix == 2) {
						while (p.indexOf('/') == 0)
							p = p.substr(1);

						scope.Path = prefix + p;
					}
					if (ix == 1 || ix == 2)
						continuation();
				});
			} else {
				continuation();
			}
		});

	};

	EditUriBackendConfig.validaters['googledrive'] = EditUriBackendConfig.validaters['onedrive'];
	EditUriBackendConfig.validaters['gcs']         = EditUriBackendConfig.validaters['onedrive'];
	EditUriBackendConfig.validaters['amzcd']       = EditUriBackendConfig.validaters['onedrive'];
	EditUriBackendConfig.validaters['box']         = EditUriBackendConfig.validaters['onedrive'];

	EditUriBackendConfig.validaters['azure'] = function(scope, continuation) {
		var res =
			EditUriBackendConfig.require_field(scope, 'Username', 'Account name') &&
			EditUriBackendConfig.require_field(scope, 'Password', 'Access Key') &&
			EditUriBackendConfig.require_field(scope, 'Path', 'Container name');

		if (res)
			continuation();
	};

	EditUriBackendConfig.validaters['openstack'] = function(scope, continuation) {
		var res =
			EditUriBackendConfig.require_field(scope, 'Username', 'username') &&
			EditUriBackendConfig.require_field(scope, 'Path', 'bucket name');

		if (res && (scope['openstack_server'] || '').trim().length == 0 && (scope['openstack_server_custom'] || '').trim().length == 0)
			res = EditUriBackendConfig.show_error_dialog('You must select or fill in the AuthURI');

		if (((scope.openstack_apikey) || '').trim().length == 0) {

			if (res && (scope.Password || '').trim().length == 0)
				res = EditUriBackendConfig.show_error_dialog('You must enter either a password or an API Key');

			if (res && ((scope.openstack_tenantname) || '').trim().length == 0)
				res = EditUriBackendConfig.show_error_dialog('You must enter a tenant name if you do not provide an API Key');

		} else {
			if (res && (scope.Password || '').trim().length != 0)
				res = EditUriBackendConfig.show_error_dialog('You must enter either a password or an API Key, not both');
		}

		if (res)
			continuation();
	};

	EditUriBackendConfig.validaters['s3'] = function(scope, continuation) {
		var res =
			EditUriBackendConfig.require_field(scope, 'Server', 'bucket name') &&
			EditUriBackendConfig.require_field(scope, 'Username', 'AWS Access ID') &&
			EditUriBackendConfig.require_field(scope, 'Password', 'AWS Access Key');

		if (res && (scope['s3_server'] || '').trim().length == 0 && (scope['s3_server_custom'] || '').trim().length == 0)
			res = EditUriBackendConfig.show_error_dialog('You must select or fill in the server');


		if (res) {

			function checkUsernamePrefix() {
				if (scope.Server.toLowerCase().indexOf(scope.Username.toLowerCase()) != 0) {
					DialogService.dialog('Adjust bucket name?', 'The bucket name should start with your username, prepend automatically?', ['Cancel', 'No', 'Yes'], function(ix) {
						if (ix == 2)
							scope.Server = scope.Username.toLowerCase() + '-' + scope.Server;
						if (ix == 1 || ix == 2)
							continuation();
					});
				} else {
					continuation();
				}
			}

			function checkLowerCase() {
				if (scope.Server.toLowerCase() != scope.Server) {
					DialogService.dialog('Adjust bucket name?', 'The bucket name should be all lower-case, convert automatically?', ['Cancel', 'No', 'Yes'], function(ix) {
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

	EditUriBackendConfig.validaters['b2'] = function(scope, continuation) {
		var res =
			EditUriBackendConfig.require_field(scope, 'Server', 'bucket name') &&
			EditUriBackendConfig.require_field(scope, 'Username', 'B2 Cloud Storage Account ID') &&
			EditUriBackendConfig.require_field(scope, 'Password', 'B2 Cloud Storage Application Key');

		if (res)
			continuation();
	};

	EditUriBackendConfig.validaters['mega'] = function(scope, continuation) {
		scope.Path = scope.Path || '';
		var res =
			EditUriBackendConfig.require_field(scope, 'Username', 'Username') &&
			EditUriBackendConfig.require_field(scope, 'Password', 'Password');

		if (res)
			continuation();
	};


});