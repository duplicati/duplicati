backupApp.service('Localization', function($rootScope, $timeout, AppService) {
    this.strings = {};

    var self = this;

    this.preg_quote = function preg_quote (str, delimiter) {
        //  discuss at: http://locutus.io/php/preg_quote/
        // original by: booeyOH
        // improved by: Ates Goral (http://magnetiq.com)
        // improved by: Kevin van Zonneveld (http://kvz.io)
        // improved by: Brett Zamir (http://brett-zamir.me)
        // bugfixed by: Onno Marsman (https://twitter.com/onnomarsman)
        //   example 1: preg_quote("$40")
        //   returns 1: '\\$40'
        //   example 2: preg_quote("*RRRING* Hello?")
        //   returns 2: '\\*RRRING\\* Hello\\?'
        //   example 3: preg_quote("\\.+*?[^]$(){}=!<>|:")
        //   returns 3: '\\\\\\.\\+\\*\\?\\[\\^\\]\\$\\(\\)\\{\\}\\=\\!\\<\\>\\|\\:'

        return (str + '').replace(new RegExp('[.\\\\+*?\\[\\^\\]$(){}=!<>|:\\' + (delimiter || '') + '-]', 'g'), '\\$&')
    };

    this.format = function() {
        if (arguments == null || arguments.length < 1)
            return null;

        if (arguments.length == 1)
            return arguments[0];

        var msg = arguments[0];

        for(var i = 1; i < arguments.length; i++)
            msg = msg.replace(new RegExp( "(" + this.preg_quote('{' + (i-1) + '}') + ")" , 'g' ), arguments[i]);

        return msg;
    };

    this.watch = function(scope, m) {
        scope.$on('localizationchanged', function() {
            if (m) m();

            $timeout(function() {
                scope.$digest();
            });
        });

        if (m) $timeout(m);
    };

    this.setLocale = function(locale) {
        AppService.get('/localizations/' + locale).then(function(data) {
            angular.copy(data.data, self.strings);
            $rootScope.$broadcast('localizationchanged');
        }, function(msg) { 
            if (msg && msg.data && msg.data.Message)
                DialogService.dialog('Error', 'Failed to read localizations: ' + msg.data.Message);
            else
                DialogService.dialog('Error', 'Failed to read localizations: ' + msg.statusText);
        });
    };

    this.setLocale('en-US');
});
