backupApp.service('Localization', function($rootScope, $timeout, AppService) {
    this.strings = {};


    this.preg_quote = function( str ) {
        // http://kevin.vanzonneveld.net
        // +   original by: booeyOH
        // +   improved by: Ates Goral (http://magnetiq.com)
        // +   improved by: Kevin van Zonneveld (http://kevin.vanzonneveld.net)
        // +   bugfixed by: Onno Marsman
        // *     example 1: preg_quote("$40");
        // *     returns 1: '\$40'
        // *     example 2: preg_quote("*RRRING* Hello?");
        // *     returns 2: '\*RRRING\* Hello\?'
        // *     example 3: preg_quote("\\.+*?[^]$(){}=!<>|:");
        // *     returns 3: '\\\.\+\*\?\[\^\]\$\(\)\{\}\=\!\<\>\|\:'

        return (str+'').replace(/([\\\.\+\*\?\[\^\]\$\(\)\{\}\=\!\<\>\|\:])/g, "\\$1");
    }

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

    this.localize = function() {
        if (arguments == null || arguments.length < 1)
            return '';

        if (this.strings[arguments[0]] != null)
            arguments[0] = this.strings[msg];

        return this.format.apply(this, arguments);
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
            angular.copy(this.strings, data.data);
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
