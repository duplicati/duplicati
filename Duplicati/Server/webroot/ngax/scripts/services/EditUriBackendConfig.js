backupApp.service('EditUriBackendConfig', function(AppService, AppUtils, SystemInfo, DialogService, gettextCatalog) {

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

    // Testers perform additional checks when pressing the Test button
    this.testers = { };

    this.defaultbackend = 'file';
    this.defaulttemplate = 'templates/backends/generic.html';
    this.defaultbuilder = function(scope) {
        var opts = {};
        self.merge_in_advanced_options(scope, opts, true);

        var url = AppUtils.format('{0}{1}://{2}{3}/{4}{5}',
            scope.Backend.Key,
            (scope.SupportsSSL && scope.UseSSL) ? 's' : '',
            scope.Server || '',
            (scope.Port || '') == '' ? '' : ':' + scope.Port,
            scope.Path || '',
            AppUtils.encodeDictAsUrl(opts)
        );

        return url;
    };

    this.merge_in_advanced_options = function (scope, dict, includeUserPassword) {
        if (includeUserPassword == null) {
            includeUserPassword = true;
        }
        // Some backends do not have input fields for Username and Password
        // When changing backends, these variables are not cleared
        // Only include them if the backend supports it and shows input fields for them
        // Other options appear in the AdvancedOptions list and can be removed manually if not supported
        if (includeUserPassword && scope.Username != null && scope.Username != '')
            dict['auth-username'] = scope.Username;
        if (includeUserPassword && scope.Password != null && scope.Password != '')
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

    this.show_error_dialog = function(msg) {
        DialogService.dialog('Error', msg);
        return false;
    };

    this.show_warning_dialog = function(msg, continuation) {
        DialogService.dialog(gettextCatalog.getString('Confirmation required'), msg, [gettextCatalog.getString('No'), gettextCatalog.getString('Yes')], function(ix) {
            if (ix == 1)
                continuation();
        });
    };

    this.defaultvalidater = function(scope, continuation) {
        continuation();
    };

    this.require_field = function(scope, field, label) {
        if ((scope[field] || '').trim().length == 0)
            return self.show_error_dialog(gettextCatalog.getString('You must fill in {{field}}', { field: label || field }));

        return true;
    };

    this.recommend_field = function (scope, field, label, reason, continuation) {
        if ((scope[field] || '').trim().length == 0)
            return self.show_warning_dialog(gettextCatalog.getString('You should fill in {{field}}{{reason}}', { field: label || field, reason: reason }), continuation);
        else
            continuation();
    };

    this.require_server = function(scope) {
        if ((scope.Server || '').trim().length == 0)
            return self.show_error_dialog(gettextCatalog.getString('You must fill in the server name or address'));

        return true;
    };

    this.require_path = function(scope) {
        if ((scope.Path || '').trim().length == 0)
            return self.show_error_dialog(gettextCatalog.getString('You must specify a path'));

        return true;
    };

    this.recommend_path = function(scope, continuation) {
        if ((scope.Path || '').trim().length == 0)
            return self.show_warning_dialog(gettextCatalog.getString('If you do not enter a path, all files will be stored in the login folder.\nAre you sure this is what you want?'), continuation);
        else
            continuation();
    };

    this.require_username_and_password = function(scope) {
        if ((scope.Username || '').trim().length == 0)
            return self.show_error_dialog(gettextCatalog.getString('You must fill in the username'));
        if ((scope.Password || '').trim().length == 0)
            return self.show_error_dialog(gettextCatalog.getString('You must fill in the password'));

        return true;
    };

    this.require_username = function(scope) {
        if ((scope.Username || '').trim().length == 0)
            return self.show_error_dialog(gettextCatalog.getString('You must fill in the username'));

        return true;
    };

});
