/*
 * Primary app code
 */

// Global app settings

APP_DATA = null;
APP_EVENTS= {};
APP_UTIL = {
    parseBoolOption: function(val, def) {
        if (val == null) {
            if (def === undefined)
                return true;
            else
                return def == true;
        }

        var str = (val + '').toLowerCase();
        return str == '' || str == 'true' || str == '1' || str == 'yes' || str == 'on';
    },

    isValidBoolOption: function(val) {
        if (val == null)
            return true;

        var t = (val + '').toLowerCase();
        return val == '' || t == '1' || t == 'true' || t == 'on' || t == 'yes' || t == '0' || t == 'false' || t == 'off' || t == 'no';
    },

    parseOptionStrings: function(val, dict, validateCallback) {
        dict = dict || {};
        var lines = val.replace('\r', '\n').split('\n');
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
    },

    fill_form: function(form, data, map, extra) {
        map = map || {};
        data = data || {};

        for(var k in data) {
            var key = k;
            var m = map[key];
            var v = data[k];

            if (m !== false) {
                if (m && typeof(m) == typeof(''))
                    key = m;

                if (m && typeof(m) == typeof(function() {})) {
                    m(data, key, v, extra);
                } else {                    
                    var n = form.find('#' + key);
                    if (n.attr('type') == 'checkbox') {
                        n.attr('checked', APP_UTIL.parseBoolOption(v));
                    } else {
                        n.val(v);
                    }

                    n.change();
                }
            }
        }
    },

    read_form: function(form, map, values, extra) {
        values = values || {};
        map = map || {};

        form.find('select').each(function(i, e) {
            var key = e.id;
            var m = map[e.id];

            if (m !== false) {
                if (m && typeof(m) == typeof(function() {})) {
                    m(values, key, e, extra);
                } else {
                    if (m && typeof(m) == typeof(''))
                        key = m;

                    values[key] = $(e).val();
                }
            }
        });

        form.find('input').each(function(i, e) { 
            var key = e.id;
            var m = map[e.id];

            if (m !== false) {
                if (m && typeof(m) == typeof(function() {})) {
                    m(values, key, e, extra);
                } else {
                    if (m && typeof(m) == typeof(''))
                        key = m;

                if (e.type == 'checkbox')
                    values[key] = $(e).is(':checked');
                else
                    values[key] = $(e).val();
                }
            }
        });

        return values;
    }
};

$(document).ready(function() {

    // Flag as loaded in case we have plugins
    APP_DATA = {
        plugins: {
            backend: {},
            compression: {},
            encryption: {},
            primary: {}

        }
    };

    var PRIVATE_DATA = {};

    $('#main-appname').text(APP_CONFIG.branded_name);

    if ((APP_CONFIG.branded_subtitle || '').length > 0) {
        var subdiv = $('<div id="main-appname-subtitle">');
        subdiv.text(APP_CONFIG.branded_subtitle);
        $('#main-appname').append(subdiv);
        $('#main-appname').addClass('has-subtitle');
    }

    $('.button').button();

    $('#main-list-container > div.main-backup-entry').remove();
    $('#loading-dialog').dialog({modal: true}).show();

    // Register a global function for password strength
    $.passwordStrength = function(password, callback) {
        if (callback == null)
            return;

        var onUpdate = function(res) {
            try { callback(res); }
            catch (e) { }
        };

        try { onUpdate(zxcvbn(password)); }
        catch (e) { 
            // Not loaded, try this:
            $.getScript('/scripts/zxcvbn.js', function() {
                try {
                    onUpdate(zxcvbn(password));
                }
                catch (e) {
                    onUpdate(null);
                }
            });
        }
    };


    PRIVATE_DATA.refresh_server_settings = function(callback, errorhandler) {
        $.ajax({
            url: APP_CONFIG.server_url,
            dataType: 'json',
            data: { action: 'system-info' }
        })
        .done(function(data) {
            PRIVATE_DATA.server_config = data;
            $('#loading-dialog').dialog("close");
            if (callback != null)
                callback(PRIVATE_DATA.server_config);
        })
        .fail(function(data, status) {
            if (errorhandler)
                errorhandler(data, status);
        });
    };

    PRIVATE_DATA.refresh_backup_list = function(callback, errorhandler) {
        $.ajax({
            url: APP_CONFIG.server_url,
            dataType: 'json',
            data: { action: 'list-backups' }
        })
        .done(function(data) {
            PRIVATE_DATA.backup_list = data;
            $('#loading-dialog').dialog("close");

            // Clear existing stuff
            $('#main-list-container > div.main-backup-entry').remove();

            if ($('#backup-item-template').length > 0 && data.length > 0) {

                // Pre-processing of data
                for(var n in data) {
                    var b = data[n];
                    b.Metadata = b.Metadata || {};

                    if (!b.Metadata['TargetSizeString'])
                        b.Metadata['TargetSizeString'] = '< unknown >'
                    if (!b.Metadata['SourceSizeString'])
                        b.Metadata['SourceSizeString'] = '< unknown >'
                }

                if (APP_DATA.plugins.primary['backup-list-preprocess'])
                    APP_DATA.plugins.primary['backup-list-preprocess'](data);

                //Fill with jQuery template
                $.tmpl($('#backup-item-template'), data).prependTo($('#main-list-container'));


                // Post processing of data
                for(var n in data) {
                    var id = data[n].ID;

                    $('#backup-details-run-' + id).click(function() {
                        APP_DATA.runBackup(id);
                    });

                    $('#backup-details-restore-' + id).click(function() { 
                        APP_DATA.restoreBackup(id);
                    });

                    $('#backup-details-edit-' + id).click(function() {
                        APP_DATA.editBackup(id);
                    });

                    $('#backup-details-delete-' + id).click(function() {
                        APP_DATA.deleteBackup(id);
                    });

                }
                
                if (APP_DATA.plugins.primary['backup-list-postrocess'])
                    APP_DATA.plugins.primary['backup-list-postrocess']($('#main-list-container'), $('#main-list-container > div.main-backup-entry'), data);

            }

            if (callback)
                callback(PRIVATE_DATA.backup_list);
        })
        .fail(function(data, status) {
            if (errorhandler)
                errorhandler(data, status);
        });
    };

    PRIVATE_DATA.refresh_server_settings();
    PRIVATE_DATA.refresh_backup_list();
    var serverWithCallback = function(data, callback, errorhandler, refreshMethod) {
        if (typeof(data) == typeof(''))
            data = { action: data };

        var method = 'GET';
        if (data.HTTP_METHOD) {
            method = data.HTTP_METHOD;
            delete data.HTTP_METHOD;
        }

        $.ajax({
            url: APP_CONFIG.server_url,
            type: method,
            dataType: 'json',
            data: data
        })
        .done(function(data) {
            if (refreshMethod)
                refreshMethod(data, true, null);

            if (callback != null)
                callback(data, true, null);
        })
        .fail(function(data, status) {
            if (refreshMethod)
                refreshMethod(data, false, data.statusText);

            if (errorhandler)
                errorhandler(data, false, data.statusText);
        });
    };


    APP_DATA.getServerConfig = function(callback, errorhandler) {
        if (PRIVATE_DATA.server_config == null) {
            PRIVATE_DATA.refresh_server_settings(callback, errorhandler);
        } else {
            callback(PRIVATE_DATA.server_config);
        }
    };

    APP_DATA.validatePath = function(path, callback) { 
        serverWithCallback({ action: 'validate-path', path: path }, callback, callback); 
    };
    APP_DATA.getLabels = function(callback) { 
        serverWithCallback('list-tags', callback, callback); 
    };

    APP_DATA.getBackupDefaults = function(callback, errorhandler) { 
        serverWithCallback('get-backup-defaults', callback, errorhandler); 
    };
    APP_DATA.getBackupData = function(id, callback, errorhandler) { 
        serverWithCallback({ action: 'get-backup', id: id}, callback, errorhandler); 
    };

    APP_DATA.addBackup = function(cfg, callback, errorhandler) {
        serverWithCallback(
            { action: 'add-backup', HTTP_METHOD: 'POST', data: JSON.stringify(cfg)}, 
            callback, 
            errorhandler, 
            function() { PRIVATE_DATA.refresh_backup_list(); }
        );
    };

    APP_DATA.updateBackup = function(cfg, callback, errorhandler) {
        serverWithCallback(
            { action: 'update-backup', HTTP_METHOD: 'POST', data: JSON.stringify(cfg)}, 
            callback, 
            errorhandler, 
            function() { PRIVATE_DATA.refresh_backup_list(); }
        );
    };


    APP_DATA.editNewBackup = function() {
        APP_DATA.editBackup();
    };

    APP_DATA.deleteBackup = function(id, callback, errorhandler) {
        serverWithCallback(
            { action: 'delete-backup', id: id }, 
            callback, 
            errorhandler, 
            function() { PRIVATE_DATA.refresh_backup_list(); }
        );
    };

    APP_DATA.editBackup = function(id) {
        APP_DATA.getServerConfig(function(data) {

            var callback = function(data) {
                $("#edit-dialog").dialog('open');

                // Bug-fix, this will remove style="width: auto", which breaks Chrome a bit
                $("#edit-dialog").css('width', '');

                // Send the defaults to the dialog
                $("#edit-dialog").trigger('setup-dialog', data.data);  
            };

            var errorhandler = function() {
                alert('Failed to get server setup...')
            };

            if (id == undefined || parseInt(id) < 0)
                APP_DATA.getBackupDefaults(callback, errorhandler);
            else
                APP_DATA.getBackupData(id, callback, errorhandler);
        },
        function() {
            alert('Failed to get server setup...')
        });
        
    };

    APP_DATA.runBackup = function(id) {
        
    };

    APP_DATA.restoreBackup = function(id) {
        
    };

    $('#main-settings').click(function() {
        var pos = $('#main-settings').position();
        var barheight = $('#main-topbar').outerHeight();
        var menuwidth = $('#main-control-menu').outerWidth();
        var buttonwidth = $('#main-settings').outerWidth();

        $('#main-control-menu').css({
            position: 'absolute',
            top: barheight + 'px',
            right: ($(document).outerWidth() - (pos.left + buttonwidth)) + 'px'
        });
        $('#main-control-menu').toggle();
    });

    $('#main-donate').click(function() {

    });

    $('#main-control').click(function() {
    });

    $('#main-newbackup').click(function() {
        APP_DATA.editNewBackup();

    });

    $('#edit-dialog').tabs({ active: 0 });
    $("#edit-dialog").dialog({ 
        minWidth: 320, 
        width: $('body').width > 600 ? 320 : 600, 
        minHeight: 480, 
        height: 500, 
        modal: true,
        autoOpen: false,
        closeOnEscape: true,
        buttons: [
            { text: 'Previous', disabled: true, click: function(event, ui) {
                var cur = parseInt($('#edit-dialog').tabs( "option", "active"));
                cur = Math.max(cur-1, 0);
                $('#edit-dialog').tabs( "option", "active", cur);
            }},
            { text: 'Next', click: function(event, ui) {
                var cur = parseInt($('#edit-dialog').tabs( "option", "active"));
                var max = $('#edit-dialog').parent().find('[role=tablist] > li').size() - 1;

                if (!EDIT_BACKUP.validate_tab(cur))
                    return;

                event.curPage = cur;
                event.currentTarget.curPage = cur;
                cur = Math.min(cur+1, max);
                $('#edit-dialog').tabs( "option", "active", cur);
            }}
        ]
    });    

    // Hack the tabs into the dialog title
    $('#edit-dialog').parent().find('.ui-dialog-titlebar').after($('#edit-dialog').find('.ui-tabs-nav'));
    $('#edit-dialog').parent().addClass('ui-tabs');

});