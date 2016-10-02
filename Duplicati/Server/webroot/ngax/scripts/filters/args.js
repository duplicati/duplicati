backupApp.filter('args', function() {
    return function() {
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

            return (str + '')
                .replace(new RegExp('[.\\\\+*?\\[\\^\\]$(){}=!<>|:\\' + (delimiter || '') + '-]', 'g'), '\\$&')
        }
        
        if (arguments == null || arguments.length < 1)
            return null;

        if (arguments.length == 1)
            return arguments[0];

        var msg = arguments[0];

        for (var i = 1; i < arguments.length; i++)
            msg = msg.replace(new RegExp( "(" + this.preg_quote('{' + (i-1) + '}') + ")" , 'g' ), arguments[i]);

        return msg;
    }
});
