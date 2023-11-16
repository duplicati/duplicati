backupApp.service('AppUtils', function($rootScope, $timeout, $cookies, DialogService, gettextCatalog) {

    var apputils = this;

    this.exampleOptionString = '--dblock-size=100MB';

    function setMomentLocale() {
        try {
            var browser_lang = navigator.languages ?
                navigator.languages[0] :
                (navigator.language || navigator.userLanguage);
            moment.locale($cookies.get('ui-locale') ? $cookies.get('ui-locale') : browser_lang);
        } catch (e) {
        }
    }
    setMomentLocale();
    $rootScope.$on('ui_language_changed', setMomentLocale);

    this.formatSizeString = function(val) {
        var formatSizes = ['TB', 'GB', 'MB', 'KB'];
        val = parseInt(val || 0);
        var max = formatSizes.length;
        for(var i = 0; i < formatSizes.length; i++) {
            var m = Math.pow(1024, max - i);
            if (val > m) {
                return (val / m).toFixed(2) + ' ' + formatSizes[i];
            }
        }

        return val + ' bytes';
    };

    this.watch = function(scope, m) {
        scope.$on('apputillookupschanged', function() {
            if (m) m();

            $timeout(function() {
                scope.$digest();
            });
        });

        if (m) $timeout(m);
    };

    this.getEntryTypeFromIconCls = function(cls)
    {
        // Entry type is used in as the ALT entry,
        // to guide screen reading software for visually
        // impaired users

        var res = gettextCatalog.getString('Folder');

        if (cls == 'x-tree-icon-mydocuments')
            res = gettextCatalog.getString('My Documents');
        else if (cls == 'x-tree-icon-mymusic')
            res = gettextCatalog.getString('My Music');
        else if (cls == 'x-tree-icon-mypictures')
            res = gettextCatalog.getString('My Pictures');
        else if (cls == 'x-tree-icon-desktop')
            res = gettextCatalog.getString('Desktop');
        else if (cls == 'x-tree-icon-home')
            res = gettextCatalog.getString('Home');
        else if (cls == 'x-tree-icon-hypervmachine')
            res = gettextCatalog.getString('Hyper-V Machine');
        else if (cls == 'x-tree-icon-hyperv')
            res = gettextCatalog.getString('Hyper-V Machines');
        else if (cls == 'x-tree-icon-broken')
            res = gettextCatalog.getString('Broken access');
        else if (cls == 'x-tree-icon-locked')
            res = gettextCatalog.getString('Access denied');
        else if (cls == 'x-tree-icon-symlink')
            res = gettextCatalog.getString('Symbolic link');
        else if (cls == 'x-tree-icon-leaf')
            res = gettextCatalog.getString('File');

        return res;
    };

    function reloadTexts() {
        apputils.fileSizeMultipliers = [
            {name: gettextCatalog.getString('byte'), value: 'b'},
            {name: gettextCatalog.getString('KByte'), value: 'KB'},
            {name: gettextCatalog.getString('MByte'), value: 'MB'},
            {name: gettextCatalog.getString('GByte'), value: 'GB'},
            {name: gettextCatalog.getString('TByte'), value: 'TB'}
        ];

        apputils.timerangeMultipliers = [
            {name: gettextCatalog.getString('Minutes'), value: 'm'},
            {name: gettextCatalog.getString('Hours'), value: 'h'},
            {name: gettextCatalog.getString('Days'), value: 'D'},
            {name: gettextCatalog.getString('Weeks'), value: 'W'},
            {name: gettextCatalog.getString('Months'), value: 'M'},
            {name: gettextCatalog.getString('Years'), value: 'Y'}
        ];

        apputils.shorttimerangeMultipliers = [
            {name: gettextCatalog.getString('Seconds'), value: 's'},
            {name: gettextCatalog.getString('Minutes'), value: 'm'},
            {name: gettextCatalog.getString('Hours'), value: 'h'}
        ];

        apputils.daysOfWeek = [
            {name: gettextCatalog.getString('Mon'), value: 'mon'},
            {name: gettextCatalog.getString('Tue'), value: 'tue'},
            {name: gettextCatalog.getString('Wed'), value: 'wed'},
            {name: gettextCatalog.getString('Thu'), value: 'thu'},
            {name: gettextCatalog.getString('Fri'), value: 'fri'},
            {name: gettextCatalog.getString('Sat'), value: 'sat'},
            {name: gettextCatalog.getString('Sun'), value: 'sun'}
        ];

        apputils.speedMultipliers = [
            {name: gettextCatalog.getString('byte/s'), value: 'b'},
            {name: gettextCatalog.getString('KByte/s'), value: 'KB'},
            {name: gettextCatalog.getString('MByte/s'), value: 'MB'},
            {name: gettextCatalog.getString('GByte/s'), value: 'GB'},
            {name: gettextCatalog.getString('TByte/s'), value: 'TB'}
        ];


        apputils.exampleOptionString = gettextCatalog.getString('Enter one option per line in command-line format, eg. {0}');

        apputils.filterClasses = [{
            name: gettextCatalog.getString('Exclude directories whose names contain'),
            key: '-dir*',
            prefix: '-*',
            suffix: '*!',
            rx: '\\-\\*([^\\!]+)\\*\\!'
        }, {
            name: gettextCatalog.getString('Exclude files whose names contain'),
            key: '-file*',
            prefix: '-[.*',
            suffix: '[^\\!]*]',    // Escape dirsep inside regexp. Required on Windows, no effect on other platforms.
            rx: '\\-\\[\\.\\*[\\^\\!\\]\\*\\]'
        }, {
            name: gettextCatalog.getString('Exclude folder'),
            key: '-folder',
            prefix: '-',
            suffix: '!',
            rx: '\\-(.*)\\!'
        }, {
            name: gettextCatalog.getString('Exclude file'),
            key: '-path',
            prefix: '-',
            exclude: ['*', '?', '{'],
            rx: '\\-([^\\[\\{\\*\\?]+)'
        }, {
            name: gettextCatalog.getString('Exclude file extension'),
            key: '-ext',
            rx: '\\-\\*\.(.*)',
            prefix: '-*.'
        }, {
            name: gettextCatalog.getString('Exclude regular expression'),
            key: '-[]',
            prefix: '-[',
            suffix: ']'
        }, {
            name: gettextCatalog.getString('Include regular expression'),
            key: '+[]',
            prefix: '+[',
            suffix: ']'
        }, {
            name: gettextCatalog.getString('Exclude filter group'),
            key: '-{}',
            prefix: '-{',
            suffix: '}'
        }, {
        //// Since all the current filter groups are intended for exclusion, there isn't a reason to show the 'Include group' section in the UI yet.
        //    name: gettextCatalog.getString('Include filter group'),
        //    key: '+{}',
        //    prefix: '+{',
        //    suffix: '}'
        //}, {
            name: gettextCatalog.getString('Include expression'),
            key: '+',
            prefix: '+'
        }, {
            name: gettextCatalog.getString('Exclude expression'),
            key: '-',
            prefix: '-'
        }];

        apputils.filterGroups = [{
            name: gettextCatalog.getString('Default excludes'),
            value: 'DefaultExcludes'
        }, {
        //// As the DefaultIncludes are currently empty, we don't need to include them in the UI yet.
        //    name: gettextCatalog.getString('Default includes'),
        //    value: 'DefaultIncludes'
        //}, {
            name: gettextCatalog.getString('System Files'),
            value: 'SystemFiles'
        }, {
            name: gettextCatalog.getString('Operating System'),
            value: 'OperatingSystem'
        }, {
            name: gettextCatalog.getString('Cache Files'),
            value: 'CacheFiles'
        }, {
            name: gettextCatalog.getString('Temporary Files'),
            value: 'TemporaryFiles'
        }, {
            name: gettextCatalog.getString('Applications'),
            value: 'Applications'
        }];

        apputils.filterTypeMap = {};
        for (var i = apputils.filterClasses.length - 1; i >= 0; i--)
            apputils.filterTypeMap[apputils.filterClasses[i].key] = apputils.filterClasses[i];

        $rootScope.$broadcast('apputillookupschanged');
    };

    reloadTexts();
    $rootScope.$on('gettextLanguageChanged', reloadTexts);

    this.parseBoolString = function(txt, def) {
        txt = (txt || '').toLowerCase();
        if (txt == '0' || txt == 'false' || txt == 'off' || txt == 'no' || txt == 'f')
            return false;
        else if (txt == '1' || txt == 'true' || txt == 'on' || txt == 'yes' || txt == 't')
            return true;
        else
            return def === undefined ? false : def;
    };


    this.splitSizeString = function(val) {
        var m = (/(\d*)(\w*)/mg).exec(val);
        var mul = null;
        if (!m)
            return [parseInt(val), null];
        else
            return [parseInt(m[1]), m[2]];
    }


    this.toDisplayDateAndTime = function(dt) {
        return moment(dt).format('lll');
    };

    this.parseDate = function(dt) {
        if (typeof(dt) == typeof('')) {
            var msec = Date.parse(dt);
            if (isNaN(msec)) {
                if (dt.length == 16 && dt[8] == 'T' && dt[15] == 'Z') {
                    dt = dt.substr(0, 4) + '-' + dt.substr(4, 2) + '-' + dt.substr(6, 2) + 'T' +
                              dt.substr(9, 2) + ':' + dt.substr(11, 2) + ':' + dt.substr(13, 2) + 'Z';
                }
                return new Date(dt);
            } else {
                return new Date(msec);
            }
        }
        else
            return new Date(dt);
    };

    this.parseOptionStrings = function(val, dict, validateCallback) {
        dict = dict || {};

        var lines = null;

        if (val != null && typeof(val) == typeof([]))
            lines = val;
        else
            lines = this.replace_all(val || '', '\r', '\n').split('\n');

        for(var i in lines) {
            var line = lines[i].trim();
            if (line != '' && line[0] != '#') {
                if (line.indexOf('--') == 0) {
                    line = line.substr(2);
                }

                var eqpos = line.indexOf('=');
                var key = line;
                var value = true;
                if (eqpos > 0) {
                    key = line.substr(0, eqpos).trim();
                    value = line.substr(eqpos + 1).trim();
                    if (value == '')
                        value = true;
                }

                if (validateCallback)
                    if (!validateCallback(dict, key, value))
                        return null;

                dict['--' + key] = value;
            }
        }

        return dict;
    };

    this.parse_extra_options = function(str, dict) {
        return this.parseOptionStrings(str, dict, function(d, k, v) {
            if (d['--' + k] !== undefined) {
                DialogService.dialog(gettextCatalog.getString('Error'), gettextCatalog.getString('Duplicate option {{opt}}', { opt: k }));
                return false;
            }

            return true;
        }) != null;
    };

    this.serializeAdvancedOptions = function(opts) {
        return this.serializeAdvancedOptionsToArray(opts).join('\n');
    };

    this.serializeAdvancedOptionsToArray = function(opts) {
        var res = [];
        for(var n in opts)
            if (n.indexOf('--') == 0)
                res.push(n + '=' + opts[n]);

        return res;
    };

    this.mergeAdvancedOptions = function(advStr, target, source) {
        var adv = {}
        if (!this.parse_extra_options(advStr, adv))
            return false;

        angular.extend(target, adv);

        // Remove advanced options, no longer in the list
        for(var n in source)
            if (n.indexOf('--') == 0)
                if (target[n] === undefined)
                    target[n] = null;

    };

    this.notifyInputError = function(msg) {
        DialogService.dialog('Error', msg);
        return false;
    };

    this.connectionError = function(txt, msg) {
        if (typeof(txt) == typeof('')) {
            if (msg == null)
                return function(msg) {
                    if (msg && msg.data && msg.data.Message)
                        DialogService.dialog(gettextCatalog.getString('Error'), txt + msg.data.Message);
                    else
                        DialogService.dialog(gettextCatalog.getString('Error'), txt + msg.statusText);
                };
        } else {
            msg = txt;
            txt = '';
        }

        if (msg && msg.data && msg.data.Message)
            DialogService.dialog(gettextCatalog.getString('Error'), txt + msg.data.Message);
        else
            DialogService.dialog(gettextCatalog.getString('Error'), txt + msg.statusText);
    };

    this.generatePassphrase = function() {
        var specials = '!@#$%^&*()_+{}:"<>?[];\',./';
        var lowercase = 'abcdefghijklmnopqrstuvwxyz';
        var uppercase = lowercase.toUpperCase();
        var numbers = '0123456789';
        var all = specials + lowercase + uppercase + numbers;

        function choose(str, n) {
            var res = '';
            for (var i = 0; i < n; i++) {
                res += str.charAt(Math.floor(Math.random() * str.length));
            }

            return res;
        };

        var pwd = (
            choose(specials, 2) +
            choose(lowercase, 2) +
            choose(uppercase, 2) +
            choose(numbers, 2) +
            choose(all, (Math.random()*5) + 5)
        ).split('');

        for(var i = 0; i < pwd.length; i++) {
            var pos = parseInt(Math.random() * pwd.length);
            var t = pwd[i]
            pwd[i] = pwd[pos];
            pwd[pos] = t;
        }

        return pwd.join('');
    }

    this.nl2br = function(str, is_xhtml) {
        var breakTag = (is_xhtml || typeof is_xhtml === 'undefined') ? '<br />' : '<br>';
        return (str + '').replace(/([^>\r\n]?)(\r\n|\n\r|\r|\n)/g, '$1'+ breakTag +'$2');
    };

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

    this.replace_all_insensitive = function(str, pattern, replacement) {
      return str.replace( new RegExp( "(" + this.preg_quote(pattern) + ")" , 'gi' ), replacement );
    };

    this.replace_all = function(str, pattern, replacement) {
      return str.replace( new RegExp( "(" + this.preg_quote(pattern) + ")" , 'g' ), replacement );
    };

    this.format = function() {
        if (arguments == null || arguments.length < 1)
            return null;

        if (arguments.length == 1)
            return arguments[0];

        var msg = arguments[0];

        for(var i = 1; i < arguments.length; i++)
            msg = this.replace_all(msg, '{' + (i-1) + '}', arguments[i]);

        return msg;
    };

    this.encodeDictAsUrl = function(obj) {
            if (obj == null)
                return '';

            var str = [];
            for(var p in obj) {
                var x = encodeURIComponent(p);
                if (obj[p] != null)
                    x += "=" + encodeURIComponent(obj[p]);
                str.push(x);
            }

            if (str.length == 0)
                return '';

            return '?' + str.join("&");
    };

    var URL_REGEXP_FIELDS = ['source_uri', 'backend-type', '--auth-username', '--auth-password', 'server-name', 'server-port', 'server-path', 'querystring'];
    var URL_REGEXP = /([^:]+)\:\/\/(?:(?:([^\:]+)(?:\:?:([^@]*))?\@))?(?:([^\/\?\:]*)(?:\:(\d+))?)(?:\/([^\?]*))?(?:\?(.+))?/;
    var QUERY_REGEXP = /(?:^|&)([^&=]*)=?([^&]*)/g;

    this.decode_uri = function(uri, backendlist) {

        var i = URL_REGEXP_FIELDS.length + 1;
        var res = {};

        var m = URL_REGEXP.exec(uri);

        // Invalid URI
        if (!m)
            return res;

        while (i--) {
            res[URL_REGEXP_FIELDS[i]] = m[i] || "";
        }

        res.querystring.replace(QUERY_REGEXP, function(str, key, val) {
            if (key)
                res['--' + key] = decodeURIComponent((val || '').replace(/\+/g, '%20'));
        });

        var backends = {};
        for(var i in backendlist)
            backends[backendlist[i].Key] = true;

        var scheme = res['backend-type'];

        if (scheme && scheme[scheme.length - 1] == 's' && !backends[scheme] && backends[scheme.substr(0, scheme.length-1)]) {
            res['backend-type'] = scheme.substr(0, scheme.length-1);
            res['--use-ssl'] = true;
        }

        return res;
    };

    this.find_scheme = function(uri, backendlist) {
        if (!uri || uri.length == 0)
            return null;

        uri = uri.trim().toLowerCase();
        var ix = uri.indexOf('://');
        if (ix <= 0) {
            for(var i in backendlist)
                if (backendlist[i].Key == 'file')
                    return 'file';

            return backendlist[0];
        }

        return (EDIT_URI.decode_uri(uri)['backend-type'] || '').toLowerCase()
    };

    this.contains_value = function(dict, value) {
        for(var k in dict)
            if (dict[k] == value)
                return true;

        return false;
    };

    this.contains_key = function(dict, key, keyname) {
        for(var i in dict)
            if (keyname == null ? i == key : dict[i][keyname] == key)
                return true;

        return false;
    };


    this.removeEmptyEntries = function(x) {
        for (var i = x.length - 1; i >= 0; i--) {
            if (x[i] == null || x[i] == '')
                x.splice(i, 1);
        };

        return x;
    };

    this.splitFilterIntoTypeAndBody = function(src, dirsep) {
        if (src == null)
            return null;

        if (dirsep == null) {
            throw new Error('No dirsep provided!');
        }

        function matches(txt, n) {
            var pre = apputils.replace_all(n.prefix || '', '!', dirsep);
            var suf = apputils.replace_all(n.suffix || '', '!', dirsep);

            if (txt.indexOf(pre) != 0 || txt.lastIndexOf(suf) != txt.length - suf.length)
                return null;

            var type = n.key;
            var body = txt.substr(pre.length);
            body = body.substr(0, body.length - suf.length);

            if (body.length >= 2 && body[1] == '[') {
                body = body.substr(1, body.length - 2);
            }

            if (n.exclude != null) {
                for (var i = n.exclude.length - 1; i >= 0; i--)
                    if (body.indexOf(apputils.replace_all(n.exclude[i] || '', '!', dirsep)) >= 0)
                        return null;
            }

            return [type, body];
        }

        for(var i in this.filterClasses) {
            var n = matches(src, this.filterClasses[i]);
            if (n != null)
                return n;
        }

        return null;
    };

    this.buildFilter = function(type, body, dirsep) {
        if (type == null || this.filterTypeMap[type] == null)
            return body;

        body = body || '';

        var f = this.filterTypeMap[type];
        var pre = this.replace_all(f.prefix || '', '!', dirsep);
        var suf = this.replace_all(f.suffix || '', '!', dirsep);

        if (pre.length >= 2 && pre[1] == '[') {
            //Regexp encode body ....
        }

        return pre + body + suf;
    };

    this.filterListToRegexps = function(filters, caseSensitive) {
        var res = [];

        for(var i = 0; i < filters.length; i++) {
            var f = filters[i];

            if (f == null || f.length == 0)
                continue;

            var flag = f.substr(0, 1);
            var filter = f.substr(1);
            var rx = filter.substr(0, 1) == '[' && filter.substr(filter.length - 1, 1) == ']';
            if (rx)
                filter = filter.substr(1, filter.length - 2);
            else
                filter = this.replace_all(this.replace_all(this.preg_quote(filter), '*', '.*'), '?', '.');

            try {
                res.push([flag == '+', new RegExp(filter, caseSensitive ? 'g' : 'gi')]);
            } catch (e) {
            }
        }

        return res;
    };

    this.evalFilter = function(path, filters, include) {
        for(var i = 0; i < filters.length; i++) {
            var m = path.match(filters[i][1]);
            if (m && m.length == 1 && m[0].length == path.length)
                return filters[i][0];
        }

        return include === undefined ? true : include;
    };

    this.buildOptionList = function(sysinfo, encmodule, compmodule, backmodule) {
        if (sysinfo == null || sysinfo.Options == null)
            return null;

        var items = angular.copy(sysinfo.Options);
        for(var n in items)
            items[n].Category = gettextCatalog.getString('Core options');

        function copyToList(lst, key) {
            if (key != null && typeof(key) != typeof(''))
                key = null;

            for(var n in lst)
            {
                if (key == null || key.toLowerCase() == lst[n].Key.toLowerCase())
                {
                    var m = angular.copy(lst[n].Options);
                    for(var i in m)
                        m[i].Category = lst[n].DisplayName;
                    items.push.apply(items, m);
                }
            }
        }

        copyToList(sysinfo.GenericModules);

        if (encmodule !== false)
            copyToList(sysinfo.EncryptionModules, encmodule);
        if (compmodule !== false)
            copyToList(sysinfo.CompressionModules, compmodule);
        if (backmodule !== false)
            copyToList(sysinfo.BackendModules, backmodule);

        return items;
    };

    this.extractServerModuleOptions = function(advOptionsList, servermodulelist, servermodulesettings, optionlistname) {
        if (optionlistname == null)
            optionlistname = 'SupportedCommands';

        for(var mix in servermodulelist) {
            var mod = servermodulelist[mix];
            for(var oix in mod[optionlistname]) {
                var opt = mod[optionlistname][oix];
                var prefixstr = '--' + opt.Name + '=';
                for (var i = advOptionsList.length - 1; i >= 0; i--) {
                    if (advOptionsList[i].indexOf(prefixstr) == 0) {
                        servermodulesettings[opt.Name] = advOptionsList[i].substr(prefixstr.length);
                        advOptionsList.splice(i, 1);
                    }
                }
            }
        }
    };

    this.formatDuration = function(duration) {
        // duration is a timespan string in format (dd.)hh:mm:ss.sss
        // the part (dd.) is as indicated optional

        if (duration == null) {
            return;
        }

        var timespanArray = duration.split(':');

        if (timespanArray.length < 3) {
            return;
        }

        if (timespanArray[0].indexOf('.') > 0) {
            timespanArray[0] = timespanArray[0].replace('.', " day(s) and ");
        }

        // round second according to ms
        timespanArray[2] = Math.round(parseFloat(timespanArray[2])).toString();
        // zero-padding
        if (timespanArray[2].length == 1) timespanArray[2] = '0' + timespanArray[2];

        return timespanArray.join(':');
    };

});
