/*
 * Edituri app code
 */

BACKEND_STATE = null;
EDIT_URI = null;


$(document).ready(function() {

    EDIT_URI = {
        URL_REGEXP_FIELDS: ['source_uri', 'backend-type', '--auth-username', '--auth-password', 'server-name', 'server-port', 'server-path', 'querystring'],
        URL_REGEXP: /([^:]+)\:\/\/(?:(?:([^\:]+)(?:\:?:([^@]*))?\@))?(?:([^\/\?\:]*)(?:\:(\d+))?)(?:\/([^\?]*))?(?:\?(.+))?/,
        QUERY_REGEXP: /(?:^|&)([^&=]*)=?([^&]*)/g,

        createFieldset: function(config) {
            var outer = $('<div></div>');
            var label = config.label ? $('<div></div>').addClass('edit-dialog-label ' + (config.labelclass || '')).html(config.label) : null;
            var field;
            if (config.type == 'link') {
                field = config.field === false ? null :
                    $('<a />').text(config.title)
                    .attr('id', (config.name || ''))
                    .addClass('action-link ' + (config.fieldclass || ''))
                    .attr('href', config.href || '#')
                    .attr('target', config.target || '_blank');

            } else {
                field = config.field === false ? null :
                    $('<input type="' + (config.type || 'text') + '" name="' + (config.name || '') + '" />')
                    .attr('id', (config.name || ''))
                    .addClass('text ui-widget-content ui-corner-all ' + (config.fieldclass || ''));

                if (field && config.value !== undefined)
                    config.attr('value', config.value);
            }

            var checklabel = config.checklabel ? $('<div></div>').addClass('checkbox-label ' + (config.labelclass || '')).html(config.checklabel) : null;

            outer.append(label, field, checklabel);

            var r = {
                outer: outer,
                label: label,
                field: field,
                checklabel: checklabel
            };

            if (config.title)
                for(var k in r)
                    if (r[k])
                        r[k].attr('title', config.title);

            if (config.watermark && field != null)
                field.watermark(config.watermark);

            BACKEND_STATE.fieldset_cleanup.push(outer);

            if (config.after)
                outer.insertAfter(config.after);
            else if (config.before)
                outer.insertBefore(config.before);
            else if (config.append)
                config.append(outer);
            else
                $('#edit-dialog-extensions').append(outer);

            return r;
        },

        validate_input: function(values, validateOptions) {
            if (!BACKEND_STATE.current_state.hideserverandport) {
                if (values['server-name'] == '')
                    return EDIT_URI.validation_error($('server-name'), 'You must fill in the server name');
            } else {
                values['server-name'] = '';
            }

            if (!BACKEND_STATE.current_state.optionalauth) {
                if (values['--auth-username'] == '')
                    return EDIT_URI.validation_error($('server-username'), 'You must fill in a username');
                if (!BACKEND_STATE.current_state.optionalpassword)
                    if (values['--auth-password'] == '')
                        return EDIT_URI.validation_error($('server-password'), 'You must fill in a password');
            }

            if (validateOptions) {
                var validOptions = BACKEND_STATE.module_lookup[values['backend-type']].Options.concat(BACKEND_STATE.extra_options);
                var validOptionsDict = {};
                var names = [];
                for (var n in validOptions) {
                    validOptionsDict[validOptions[n].Name] = validOptions[n];
                    names.push(n.Name);
                    if (n.Aliases != null)
                        for (var i in n.Aliases) {
                            validOptionsDict[i] = n;
                            names.push(i);
                        }
                }

                for(var k in values) {
                    if (k.length > 2 && k.substr(0, 2) == '--') {
                        var opt = validOptionsDict[k.substr(2)];

                        if (!opt) {
                            return EDIT_URI.validation_error($('#server-options'), 'Invalid option: ' + k.substr(2));
                        }

                        if (opt.Deprecated) {
                            alert('Warning', 'The option ' + opt.Name + ' is deprecated :' + opt.DeprecationMessage);
                        }

                        var val = values[k];

                        if (opt.Typename == 'Boolean') {
                            if (!APP_UTIL.isValidBoolOption(val))
                                return EDIT_URI.validation_error($('#server-options'), 'Invalid value for ' + k.substr(2) + ': ' + val + ' is not a valid boolean value');

                        } else if (opt.Typename == 'Password') {

                        } else if (opt.Typename == 'Integer') {
                            if ((parseInt(val) + '') != val) {
                                return EDIT_URI.validation_error($('#server-options'), 'Invalid value for ' + k.substr(2) + ': ' + val + ' is not an integer');
                            }
                        } else if (opt.Typename == 'Size') {
                            //TODO
                        } else if (opt.Typename == 'Timespan') {
                            //TODO
                        }

                    }
                }
            }

            return true;
        },

        validation_error: function(el, message) {
            alert(message);
            window.setTimeout( function() { el.focus(); }, 500);
            return false;
        },

        parse_extra_options: function(el, dict) {
            return APP_UTIL.parseOptionStrings(el.val(), dict, function(d, k, v) {
                if (d['--' + k] !== undefined)
                    return EDIT_URI.validation_error(el, 'Duplicate option ' + k);

                return true;
            }) != null;
        },
        build_uri: function(cfg) {
            var url = cfg['backend-type'];
            if (APP_UTIL.parseBoolOption(cfg['--use-ssl'], false))
                url += 's';

            url += '://';
            url += cfg['server-name'];
            if (cfg['server-port'])
                url += ':' + cfg['server-port'];

            if (cfg['server-path'] != '')
            {
                if(url[url.length - 1] != '/')
                    url += '/';

                url += cfg['server-path'];
            }

            var opts = [];

            if (cfg['--auth-username'] && cfg['--auth-username'] != '')
                opts.push('auth-username=' + encodeURIComponent(cfg['--auth-username']));

            if (cfg['--auth-password'] && cfg['--auth-password'] != '')
                opts.push('auth-password=' + encodeURIComponent(cfg['--auth-password']));

            for(var k in cfg)
                if (k.substr(0, 2) == '--' && k != '--auth-password' && k != '--auth-username' && k != '--use-ssl' && cfg[k] !== false && cfg[k] != '')
                    opts.push(encodeURIComponent(k.substr(2)) + '=' + encodeURIComponent(cfg[k]));

            if (opts.length > 0) {
                url += '?';
                url += opts.join('&');
            }

            return url;
        },

        decode_uri: function(uri) {

            var i = EDIT_URI.URL_REGEXP_FIELDS.length + 1;
            var res = {};

            var m = EDIT_URI.URL_REGEXP.exec(uri);

            // Invalid URI
            if (!m)
                return res;

            while (i--) {
                res[EDIT_URI.URL_REGEXP_FIELDS[i]] = m[i] || "";
            }

            res.querystring.replace(EDIT_URI.QUERY_REGEXP, function(str, key, val) {
                if (key)
                    res['--' + key] = decodeURIComponent(val);
            });

            var scheme = res['backend-type'];

            if (scheme && scheme[scheme.length - 1] == 's' && !APP_DATA.plugins.backend[scheme] && APP_DATA.plugins.backend[scheme.substr(0, scheme.length-1)]) {
                res['backend-type'] = scheme.substr(0, scheme.length-1);
                res['--use-ssl'] = true;
            }

            return res;
        },

        find_scheme: function(uri) {
            if (!uri || uri.length == 0)
                return null;

            uri = uri.trim().toLowerCase();
            var ix = uri.indexOf('://');
            if (ix <= 0) {
                if (BACKEND_STATE.module_lookup['file'])
                    return 'file';
                return BACKEND_STATE.modules[0];
            }

            return (EDIT_URI.decode_uri(uri)['backend-type'] || '').toLowerCase()
        },

        read_form: function(form) {
            var map = EDIT_URI.fill_dict_map;

            var values = APP_UTIL.read_form(form, map);
            if (BACKEND_STATE.current_state.fill_dict_map)
                values = APP_UTIL.read_form(form, BACKEND_STATE.current_state.fill_dict_map, values);

            return values;
        },

        fill_form_map: {
            '--auth-username': 'server-username',
            '--auth-password': 'server-password',
            '--use-ssl': 'server-use-ssl',
            'backend-type': function() {},
        },

        fill_dict_map: {
            'server-username': '--auth-username',
            'server-password': '--auth-password',
            'server-use-ssl': '--use-ssl'
        },

        fill_form: function(form, values) {
            var found = {};

            var map = EDIT_URI.fill_form_map;
            APP_UTIL.fill_form($('#edit-uri-form'), values, map);
            if (BACKEND_STATE.current_state.fill_form_map) {
                map = $.extend(true, {}, map);
                for(var k in map)
                    map[k] = false;

                $.extend(true, map, BACKEND_STATE.current_state.fill_form_map);

                APP_UTIL.fill_form($('#edit-uri-form'), values, map);
            }

            var opttext = '';
            for(var k in values) {
                if (map[k] === undefined && k.indexOf('--') == 0) {
                    opttext += k + '=' + decodeURIComponent(values[k] || '') + '\n';
                }
            }

            form.find('#server-options').val(opttext);
        }
    };

    var resetform = function() {
        $('#server-name').watermark('example.com');
        $('#server-port').watermark('8801');
        $('#server-path').watermark('mybackup');
        $('#server-username').watermark('Username for authentication');
        $('#server-password').watermark('Password for authentication');
        $('#server-options').watermark('Enter connection options here');
        $('#server-username-label').text('Username');
        $('#server-password-label').text('Password');

        if (BACKEND_STATE != null) {
            if (BACKEND_STATE.fieldset_cleanup != null)
                for(var i in BACKEND_STATE.fieldset_cleanup)
                    BACKEND_STATE.fieldset_cleanup[i].remove();

            if (BACKEND_STATE && BACKEND_STATE.current_state && BACKEND_STATE.current_state.cleanup )
                BACKEND_STATE.current_state.cleanup($('#connection-uri-dialog'), $('#edit-dialog-extensions'));
            BACKEND_STATE.current_state = null;
            BACKEND_STATE.fieldset_cleanup = [];
        }
        $('#edit-dialog-extensions').empty();
    };

    var uriel = null;

    $('#connection-uri-dialog').on( "dialogclose", function( event, ui ) {
        uriel = null;
        resetform();
    });



    $('#connection-uri-dialog').on( "setup-dialog", function( event, el ) {
        uriel = $(el);

        BACKEND_STATE = {};
        $('#edit-uri-form').each(function(i, e) { e.reset(); });

        APP_DATA.getServerConfig(function(serverdata) {
            var drop = $('#backend-type');
            drop.empty();

            BACKEND_STATE.modules = serverdata['BackendModules'];
            BACKEND_STATE.module_lookup = {};
            BACKEND_STATE.extra_options = [];
            for(var i in serverdata['ConnectionModules'])
                BACKEND_STATE.extra_options = BACKEND_STATE.extra_options.concat(serverdata['ConnectionModules'][i].Options);

            var group_basic = $('<optgroup label="Standard protocols"></optgroup>');
            var group_local = $('<optgroup label="Local storage"></optgroup>');
            var group_prop = $('<optgroup label="Proprietary"></optgroup>');
            var group_others = $('<optgroup label="Others"></optgroup>');

            for (var i = 0; i < BACKEND_STATE.modules.length; i++) {
              BACKEND_STATE.module_lookup[BACKEND_STATE.modules[i].Key] = BACKEND_STATE.modules[i];
            }

            var used = {};

            for(var i in {'ftp':0, 'ssh':0, 'webdav':0, 'aftp':0}) {
                if (BACKEND_STATE.module_lookup[i]) {
                    used[i] = true;
                    group_basic.append($("<option></option>").attr("value", i).text(BACKEND_STATE.module_lookup[i].DisplayName));
                }
            }

            if (BACKEND_STATE.module_lookup['s3']) {
                used['s3'] = true;
                group_basic.append($("<option></option>").attr("value", 's3').text('S3 compatible'));
            }

            if (BACKEND_STATE.module_lookup['openstack']) {
                used['openstack'] = true;
                group_basic.append($("<option></option>").attr("value", 'openstack').text('OpenStack Object Storage/ Swift'));
            }

            for(var i in {'file':0}) {
                if (BACKEND_STATE.module_lookup[i]) {
                    used[i] = true;
                    group_local.append($("<option></option>").attr("value", i).text(BACKEND_STATE.module_lookup[i].DisplayName));
                }
            }

            for (var i in { 's3': 0, 'azure': 0, 'googledrive': 0, 'onedrive': 0, 'cloudfiles': 0, 'gcs': 0, 'openstack': 0, 'hubic': 0, 'amzcd': 0, 'b2': 0, 'mega': 0, 'od4b': 0, 'mssp': 0 }) {
                if (BACKEND_STATE.module_lookup[i]) {
                    used[i] = true;
                    group_prop.append($("<option></option>").attr("value", i).text(BACKEND_STATE.module_lookup[i].DisplayName));
                }
            }

            for (var i = 0; i < BACKEND_STATE.modules.length; i++) {
                var k = BACKEND_STATE.modules[i].Key;
                if (!used[k]) {
                    group_others.append($("<option></option>").attr("value", k).text(BACKEND_STATE.module_lookup[k].DisplayName));
                }
            }

            if (group_basic.children().length > 0)
                drop.append(group_basic);
            if (group_local.children().length > 0)
                drop.append(group_local);
            if (group_prop.children().length > 0)
                drop.append(group_prop);
            if (group_others.children().length > 0)
                drop.append(group_others);


            BACKEND_STATE.orig_uri = uriel.val();
            BACKEND_STATE.orig_cfg = EDIT_URI.decode_uri(BACKEND_STATE.orig_uri);
            var scheme = BACKEND_STATE.orig_cfg['backend-type'];

            if (scheme == null && (BACKEND_STATE.orig_uri.indexOf('://') < 0 || BACKEND_STATE.orig_uri.indexOf('file://') == 0))
                scheme = 'file';

            if (scheme && APP_DATA.plugins.backend[scheme] && APP_DATA.plugins.backend[scheme].decode_uri) {
                BACKEND_STATE.orig_cfg = APP_DATA.plugins.backend[scheme].decode_uri(BACKEND_STATE.orig_uri);
            }

            BACKEND_STATE.first_setup = true;
            if (scheme && BACKEND_STATE.module_lookup[scheme])
                drop.val(scheme);

            drop.change();
        },
        function() {
            alert('Failed to get server setup...')
        });
    });

    $('#server-use-ssl').change(function() {
        if (BACKEND_STATE.current_state.defaultportssl) {
            if ($('#server-use-ssl').is(':checked')) {
                $('#server-port').watermark(BACKEND_STATE.current_state.defaultportssl + '');
            } else {
                $('#server-port').watermark(BACKEND_STATE.current_state.defaultport + '');
            }
        }
    });

    $('#backend-type').change(function() {
        var k = $('#backend-type').val();
        var m = null;
        var hasssl = false;

        var cfg = APP_DATA.plugins.backend[k] || {};

        // Auto-detect for SSL
        if (cfg.hasssl === undefined) {
            var m = BACKEND_STATE.module_lookup[k];

            if (m && m.Options)
                for(var i in m.Options)
                    if (m.Options[i].Name == 'use-ssl')
                        cfg.hasssl = true;
        }

        resetform();

        // Simple config changes through properties
        $('#server-use-ssl').toggle(cfg.hasssl == true);
        $('#server-use-ssl-label').toggle(cfg.hasssl == true);

        $('#server-name-and-port-label').toggle(cfg.hideserverandport != true);
        $('#server-name-and-port').toggle(cfg.hideserverandport != true);

        $('#server-port').toggle(cfg.hideport != true);
        $('#server-username-and-password').toggle(cfg.hideusernameandpassword != true);

        if (cfg.usernamelabel)
            $('#server-username-label').text(cfg.usernamelabel + '');
        if (cfg.passwordlabel)
            $('#server-password-label').text(cfg.passwordlabel + '');
        if (cfg.serverandportlabel)
            $('#server-name-and-port-label').text(cfg.serverandportlabel + '');
        if (cfg.serverpathlabel)
            $('#server-path').text(cfg.serverpathlabel + '');
        if (cfg.usernamewatermark)
            $('#server-username-label').watermark(cfg.usernamewatermark + '');
        if (cfg.passwordwatermark)
            $('#server-password-label').watermark(cfg.passwordwatermark + '');
        if (cfg.defaultport)
            $('#server-port').watermark(cfg.defaultport + '');


        BACKEND_STATE.current_state = cfg;

        // Specialized setup
        if (cfg.setup != null)
            cfg.setup($('#connection-uri-dialog'), $('#edit-dialog-extensions'));

        if (!cfg.hasssl)
            $('#server-use-ssl').attr('checked', false);
        $('#server-use-ssl').change();

        if (BACKEND_STATE.first_setup) {

            if (cfg.hideserverandport) {
                var hostname = BACKEND_STATE.orig_cfg['server-name'];
                if (hostname != '' && hostname != null) {
                    if (hostname[hostname.length - 1] != '/' && BACKEND_STATE.orig_cfg['server-path'] != '')
                        hostname += '/';
                    BACKEND_STATE.orig_cfg['server-name'] = '';
                    BACKEND_STATE.orig_cfg['server-path'] = hostname + BACKEND_STATE.orig_cfg['server-path'];
                }
            }

            BACKEND_STATE.first_setup = false;
            if (cfg.fill_form)
                cfg.fill_form($('#edit-uri-form'), BACKEND_STATE.orig_cfg);
            else
                EDIT_URI.fill_form($('#edit-uri-form'), BACKEND_STATE.orig_cfg);
        }
    });

    var validate_and_return_uri = function() {
        var values = EDIT_URI.read_form($('#edit-uri-form'));

        if (!EDIT_URI.parse_extra_options($('#server-options'), values))
            return null;

        if (!BACKEND_STATE.current_state.hasssl && values['--use-ssl'] !== undefined)
            delete values['--use-ssl'];


        if (BACKEND_STATE.current_state.validate)
        {
            if (!BACKEND_STATE.current_state.validate($('#connection-uri-dialog'), values))
                return null;
        } else {
            if (!EDIT_URI.validate_input(values, true))
                return null;
        }

        var uri = null;
        if (BACKEND_STATE.current_state.build_uri) {
            uri = BACKEND_STATE.current_state.build_uri($('#connection-uri-dialog'), values);
        } else {
            uri = EDIT_URI.build_uri(values);
        }

        return uri;
   };

    $('#connection-uri-dialog').dialog({
        modal: true,
        minWidth: 320,
        width: $('body').width > 600 ? 320 : 600,
        autoOpen: false,
        closeOnEscape: true,
        buttons: [
            {text: 'Cancel', click: function() { $( this ).dialog( "close" ); } },
            {text: 'Test connection', click: function() {
                var selfbtn = $(this).parent().find('.ui-dialog-buttonpane').find('.ui-button').first().next();

                var hasTriedCreate = false;
                var hasTriedCert = false;

                var testConnection = null;

                var handleError = function(data, success, message) {
                    selfbtn.button('option', 'disabled', false);
                    selfbtn.button('option', 'label', 'Test connection');

                    if (!hasTriedCreate && message == 'missing-folder')
                    {
                        if (confirm('The folder ' + $('#server-path').val() + ' does not exist\nCreate it now?')) {
                            createFolder();
                        }
                    }
                    else if (!hasTriedCert && message.indexOf('incorrect-cert:') == 0)
                    {
                        var hash = message.substr('incorrect-cert:'.length);
                        if (confirm('The server certificate could not be validated.\nDo you want to approve the SSL certificate with the hash: ' + hash + '?' )) {

                            hasTriedCert = true;
                            $('#server-options').val($('#server-options').val() + '--accept-specified-ssl-hash=' + hash);

                            testConnection();
                        }
                    }
                    else
                        alert('Failed to connect: ' + message);
                }

                testConnection = function() {
                    selfbtn.button('option', 'disabled', true);
                    selfbtn.button('option', 'label', 'Testing ...');

                    APP_DATA.callServer({action: 'test-backend', url: uri}, function(data) {
                        selfbtn.button('option', 'disabled', false);
                        selfbtn.button('option', 'label', 'Test connection');
                        alert('Connection worked!');
                    }, handleError);
                };

                var createFolder = function() {
                    hasTriedCreate = true;
                    selfbtn.button('option', 'disabled', true);
                    selfbtn.button('option', 'label', 'Creating folder ...');

                    APP_DATA.callServer(
                        {action: 'create-remote-folder', url: uri},
                        testConnection,
                        handleError
                    );
                };

                var uri = validate_and_return_uri();
                if (uri != null) {
                    testConnection();
                }
            } },
            {text: 'Create URI', click: function() {

                var uri = validate_and_return_uri();
                if (uri != null) {
                    uriel.val(uri);
                    $( this ).dialog( "close" );
                }
            } }
        ]
     });

    $('#server-options-label').click(function() {
        $('#backup-options-dialog').dialog('open');

            var k = $('#backend-type').val();
            var m = BACKEND_STATE.module_lookup[k];

            if (m && m.Options) {
            $('#backup-options-dialog').trigger('configure', { Options: m.Options.concat(BACKEND_STATE.extra_options), callback: function(id) {
                $('#backup-options-dialog').dialog('close');

                var txt = $('#server-options').val().trim();
                if (txt.length > 0)
                    txt += '\n';

                var defaultvalue = '';
                for(var o in m.Options)
                    if (m.Options[o].Name == id) {
                        defaultvalue = m.Options[o].DefaultValue;
                        break;
                    }


                txt += '--' + id + '=' + defaultvalue;
                $('#server-options').val('').val(txt);
                $('#server-options').focus();

            }});
        }
    });
});