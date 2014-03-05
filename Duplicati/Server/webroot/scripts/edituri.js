/*
 * Edituri app code
 */

BACKEND_STATE = null;
EDIT_URI = null;


$(document).ready(function() {

    EDIT_URI = {
        URL_REGEXP_FIELDS: ['source_uri', 'backend-type', '--auth-username', '--auth-password', 'server-name', 'server-port', 'server-path', 'querystring'],
        URL_REGEXP: /([^:]+)\:\/\/(?:(?:([^\:]+)(?:\:?:([^@]*))?\@))?(?:([^\/\?\:]+)(?:\:(\d+))?)(?:\/([^\?]*))?(?:\?(.+))?/,
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
            }

            if (!BACKEND_STATE.current_state.optionalauth) {
                if (values['--auth-username'] == '')
                    return EDIT_URI.validation_error($('server-username'), 'You must fill in a username');
                if (!BACKEND_STATE.current_state.optionalpassword)
                    if (values['--auth-password'] == '')
                        return EDIT_URI.validation_error($('server-password'), 'You must fill in a password');
            }

            if (validateOptions) {
                var validOptions = BACKEND_STATE.module_lookup[values['backend-type']].Options;
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
                    if (k.substr(0, 2) == '--') {
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

            url += '/' + cfg['server-path'];

            var opts = [];

            if (cfg['--auth-username'] && cfg['--auth-username'] != '')
                opts.push('auth-username=' + encodeURIComponent(cfg['--auth-username']));

            if (cfg['--auth-password'] && cfg['--auth-password'] != '')
                opts.push('auth-password=**********');

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
                    res['--' + key] = val;
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
            'backend-type': function() {}
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
                    opttext += k.substr(2) + '=' + decodeURIComponent(values[k] || '') + '\n';
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

    $('#connection-uri-dialog').on( "dialogclose", function( event, ui ) {
        resetform();
    });

    $('#connection-uri-dialog').on( "dialogopen", function( event, ui ) {
        BACKEND_STATE = {};

        APP_DATA.getServerConfig(function(serverdata) {
            var drop = $('#backend-type');
            drop.empty();

            BACKEND_STATE.modules = serverdata['BackendModules'];
            BACKEND_STATE.module_lookup = {};

            for (var i = 0; i < BACKEND_STATE.modules.length; i++) {
              drop.append($("<option></option>").attr("value", BACKEND_STATE.modules[i].Key).text(BACKEND_STATE.modules[i].DisplayName));
              BACKEND_STATE.module_lookup[BACKEND_STATE.modules[i].Key] = BACKEND_STATE.modules[i];
            }

            BACKEND_STATE.orig_uri = $('#backup-uri').val();
            BACKEND_STATE.orig_cfg = EDIT_URI.decode_uri(BACKEND_STATE.orig_uri);
            var scheme = BACKEND_STATE.orig_cfg['backend-type'];

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
            BACKEND_STATE.first_setup = false;
            if (cfg.fill_form)
                cfg.fill_form($('#edit-uri-form'), BACKEND_STATE.orig_cfg);
            else
                EDIT_URI.fill_form($('#edit-uri-form'), BACKEND_STATE.orig_cfg);
        }
    });

    $('#connection-uri-dialog').dialog({ 
        modal: true, 
        minWidth: 320, 
        width: $('body').width, 
        autoOpen: false, 
        closeOnEscape: true,
        buttons: [
            {text: 'Cancel', click: function() { $( this ).dialog( "close" ); } },
            {text: 'Create URI', click: function() { 

                var values = EDIT_URI.read_form($('#edit-uri-form'));

                if (!EDIT_URI.parse_extra_options($('#server-options'), values))
                    return;

                if (!BACKEND_STATE.current_state.hasssl && values['--use-ssl'] !== undefined)
                    delete values['--use-ssl'];


                if (BACKEND_STATE.current_state.validate)
                {
                    if (!BACKEND_STATE.current_state.validate($('#connection-uri-dialog'), values))
                        return;
                } else {
                    if (!EDIT_URI.validate_input(values, true))
                        return;
                }

                var uri = null;
                if (BACKEND_STATE.current_state.build_uri) {
                    uri = BACKEND_STATE.current_state.build_uri($('#connection-uri-dialog'), values);
                } else {
                    uri = EDIT_URI.build_uri(values);
                }

                $( this ).dialog( "close" );
                $('#backup-uri').val(uri);
            } }
        ]
     });
});