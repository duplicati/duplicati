/*
 * Edituri app code
 */

BACKEND_STATE = null;
EDIT_URI = null;

$(document).ready(function() {

    EDIT_URI = {
        createFieldset: function(config) {
            var outer = $('<div></div>');
            var label = config.label ? $('<div></div>').addClass('edit-dialog-label ' + (config.labelclass || '')).html(config.label) : null;
            var field;
            if (config.type == 'link') {
                field = config.field === false ? null : 
                    $('<a />').text(config.title)
                    .addClass('action-link ' + (config.fieldclass || ''))
                    .attr('href', config.href || '#')
                    .attr('target', config.target || '_blank');

            } else {
                field = config.field === false ? null : 
                    $('<input type="' + (config.type || 'text') + '" name="' + (config.name || '') + '" />')
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
                if (values['server-username'] == '')
                    return EDIT_URI.validation_error($('server-username'), 'You must fill in a username');
                if (!BACKEND_STATE.current_state.optionalpassword)
                    if (values['server-password'] == '')
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
                            var t = val.toUpperCase();
                            if (val != '' && t != '1' && t != 'true' && t != 'on' && t != 'yes' && t != '0' && t != 'false' && t != 'off' && t != 'no') {
                                return EDIT_URI.validation_error($('#server-options'), 'Invalid value for ' + k.substr(2) + ': ' + val + ' is not a valid boolean value');
                            }

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
            var str = el.val();
            var lines = el.val().replace('\r', '\n').split('\n');
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

                    if (dict['--' + key] !== undefined)
                        return EDIT_URI.validation_error(el, 'Duplicate option ' + key);

                    dict['--' + key] = value;
                }
            }

            return true;
        },
        build_uri: function(cfg) {
            var url = cfg['backend-type'];
            if (cfg['server-use-ssl'] || cfg['use-ssl'] || cfg['--use-ssl'])
                url += 's';

            url += '://';
            url += cfg['server-name'];
            if (cfg['server-port'])
                url += ':' + cfg['server-port'];

            url += '/' + cfg['server-path'];

            var opts = [];

            if (cfg['server-username'])
                opts.push('auth-username=' + encodeURIComponent(cfg['server-username']));
            else if (cfg['--auth-username'])
                opts.push('auth-username=' + encodeURIComponent(cfg['server-username']));

            if (cfg['server-password'])
                opts.push('auth-password=**********');
            else if (cfg['--auth-password'])
                opts.push('auth-password=**********');

            for(var k in cfg)
                if (k.substr(0, 2) == '--' && k != '--auth-password' && k != '--auth-username')
                    opts.push(encodeURIComponent(k.substr(2)) + '=' + encodeURIComponent(cfg[k]));

            if (opts.length > 0) {
                url += '?';
                url += opts.join('&');
            }

            return url;

        },

        decode_uri: function(uri) {
            return {};
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

        if (BACKEND_STATE.fieldset_cleanup != null)
            for(var i in BACKEND_STATE.fieldset_cleanup)
                BACKEND_STATE.fieldset_cleanup[i].remove();

        if (BACKEND_STATE && BACKEND_STATE.current_state && BACKEND_STATE.current_state.cleanup )
            BACKEND_STATE.current_state.cleanup($('#connection-uri-dialog'), $('#edit-dialog-extensions'));        
        BACKEND_STATE.current_state = null;
        BACKEND_STATE.fieldset_cleanup = [];

        $('#edit-dialog-extensions').empty();        
    };

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
        if (cfg.hasssl == null) {
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

                var values = {};

                $('#edit-dialog-form').find('select').each(function(i, e) { values[e.name] = $(e).val() });
                $('#edit-dialog-form').find('input').each(function(i, e) { 
                    if (e.type == 'checkbox')
                        values[e.name] = $(e).is(':checked');
                    else
                        values[e.name] = $(e).val();
                });

                if (!EDIT_URI.parse_extra_options($('#server-options'), values))
                    return;

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