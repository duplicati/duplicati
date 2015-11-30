backupApp.service('Localization', function($rootScope, $timeout, AppService, AppUtils) {
    this.strings = {};

    this.localize = function() {
        if (arguments == null || arguments.length < 1)
            return '';

        if (this.strings[arguments[0]] != null)
            arguments[0] = this.strings[msg];

        return AppUtils.format.apply(AppUtils, arguments);
    };

    this.watch = function(scope, m) {
        scope.$on('localizationchanged', function() {
            if (m) m();

            $timeout(function() {
                scope.$digest();
            });
        });

        if (m) $timeout(m);
    }

    this.setLocale = function(locale) {
        AppService.get('/localizations/' + locale).then(function(data) {
            angular.copy(this.strings, data.data);
            $rootScope.$broadcast('localizationchanged');
        }, AppUtils.connectionError(this.localize('Failed to read localizations')));
    };

    this.setLocale('en-US');
});