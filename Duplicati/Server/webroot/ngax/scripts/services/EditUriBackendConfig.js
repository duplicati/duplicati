backupApp.service('EditUriBackendConfig', function(AppService, AppUtils, SystemInfo) {

	var self = this;

	// All backends with a custom UI must register here
	this.templates = {
		'file': 'templates/backends/file.html',
		's3': 'templates/backends/s3.html'
	};

	// Loaders are a way for backends to request extra data from the server
	this.loaders = {
		's3': function(scope) {
			AppService.post('/webmodule/s3-getconfig', {'s3-config': 'Providers'}).then(function(data) {
				scope.s3_providers = data.data.Result;
			}, AppUtils.connectionError);

			AppService.post('/webmodule/s3-getconfig', {'s3-config': 'Regions'}).then(function(data) {
				scope.s3_regions = data.data.Result;
			}, AppUtils.connectionError);
		}
	};

	// Parsers take a decomposed uri input and sets up the scope variables
	this.parsers = {
		'file': function(scope, module, server, port, path, options) {
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
		},
		's3': function(scope, module, server, port, path, options) {
			if (options['--aws_access_key_id'])
				scope.Username = options['--aws_access_key_id'];
			if (options['--aws_secret_access_key'])
				scope.Password = options['--aws_secret_access_key'];

			scope.s3_rrs = options['--s3-use-rrs'];
			scope.s3_server = scope.s3_server_custom = options['--s3-server-name'];
			scope.s3_region = scope.s3_region_custom = options['--s3-location-constraint'];

			var nukeopts = ['--aws_access_key_id', '--aws_secret_access_key', '--s3-use-rrs', '--s3-server-name', '--s3-location-constraint'];
			for(var x in nukeopts)
				delete options[nukeopts[x]];
		}
	};

	// Builders take the scope and produce the uri output
	this.builders = {
		's3': function(scope) {
			var opts = {
				's3-server-name': AppUtils.contains_value(scope.s3_providers, scope.s3_server) ? scope.s3_server : scope.s3_server_custom
			};

			if (scope.s3_region != null)
				opts['s3-location-constraint'] = AppUtils.contains_value(scope.s3_regions, scope.s3_region) ? scope.s3_region : scope.s3_region_custom;
			if (scope.s3_rrs)
				opts['s3-use-rrs'] = true;

			self.merge_in_advanced_options(scope, opts);

			var url = AppUtils.format('{0}{1}://{2}/{3}{4}',
				's3',
				scope.UseSSL ? 's' : '',
				scope.Server,
				scope.Path,
				AppUtils.encodeDictAsUrl(opts)
			);

			return url;
		},
		'file': function(scope) {
			var opts = {}
			self.merge_in_advanced_options(scope, opts);
			var url = AppUtils.format('file://{0}{1}',
				scope.Path,
				AppUtils.encodeDictAsUrl(opts)
			);

			return url;
		}
	};

	this.validaters = {

	};

	this.defaultbackend = 'file';
	this.defaulttemplate = 'templates/backends/generic.html';
	this.defaultbuilder = function(scope) {
		var opts = {};
		self.merge_in_advanced_options(scope, opts);

		var url = AppUtils.format('{0}{1}://{2}{3}{4}{5}',
			scope.Backend.Key,
			(scope.SupportsSSL && scope.UseSSL) ? 's' : '',
			scope.Server || '',
			(scope.Port || '') == '' ? '' : ':' + scope.Port,
			scope.Path || '',
			AppUtils.encodeDictAsUrl(opts)
		);

		return url;
	};

	this.merge_in_advanced_options = function(scope, dict) {
		if (scope.Username != null && scope.Username != '')
			dict['auth-username'] = scope.Username;
		if (scope.Password != null && scope.Password != '')
			dict['auth-password'] = scope.Password;

		if (!AppUtils.parse_extra_options(scope.AdvancedOptions, dict))
			return false;

		for(var k in dict)
			if (k.indexOf('--') == 0) {
				dict[k.substr(2)] = dict[k];
				delete dict[k];
			}

		return true;

	};

	this.defaultvalidater = function(scope) {
		return true;
	};

});