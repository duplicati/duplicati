backupApp.service('EditUriBackendConfig', function(AppService, AppUtils, SystemInfo) {

	var self = this;

	// All backends with a custom UI must register here
	this.templates = { };

	// Loaders are a way for backends to request extra data from the server
	this.loaders = { };

	// Parsers take a decomposed uri input and sets up the scope variables
	this.parsers = { };

	// Builders take the scope and produce the uri output
	this.builders = { };

	// Validaters check the input and show the user an error or warning
	this.validaters = { };

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