/*
 * Primary app code
 */

// Global app settings

APP_DATA = null;
APP_EVENTS= {};

$(document).ready(function() {

    // Flag as loaded in case we have plugins
    APP_DATA = {};

    var PRIVATE_DATA = {};

    $('#main-appname').text(APP_CONFIG.branded_name);

    if ((APP_CONFIG.branded_subtitle || '').length > 0) {
        var subdiv = $('<div id="main-appname-subtitle">');
        subdiv.text(APP_CONFIG.branded_subtitle);
        $('#main-appname').append(subdiv);
        $('#main-appname').addClass('has-subtitle');
    }

    $('.dialog').dialog({modal: false});
    $('.modal-dialog').dialog({modal: true});
    $('.tabs').tabs({ active: 2 });
    $('.button').button();

    $('#main-list-container > div.main-backup-entry').remove();
    $('#loading-dialog').show();

    $('#source-folder-browser').jstree({
        'json': {
            'ajax': {
                'url': APP_CONFIG.server_url,
                'data': function(n) {
                    return {
                        'action': 'get-folder-contents',
                        'onlyfolders': true,
                        'path': n === -1 ? "/" : n.data('id')
                    };
                },
                'success': function(data, status, xhr) {
                    for(var i = 0; i < data.length; i++) {
                        var o = data[i];
                        o.title = o.text;
                        o.children = !o.leaf;
                        o.data = { id: o.id };
                        delete o.text;
                        delete o.leaf;
                    }
                    return data;
                }
            },
            'progressive_render' : true,
        },
        'plugins' : [ 'themes', 'json', 'ui' ],
        'core': {
        }
    });

    //$('#edit-dialog').dialog('close');

    $('#backup-name').watermark('Enter a name for your backup');
    $('#backup-uri').watermark('webdavs://example.com/mybackup?');
    $('#encryption-password').watermark('Enter a secure passphrase');
    $('#repeat-password').watermark('Repeat the passphrase');
    $('#server-name').watermark('example.com');
    $('#server-port').watermark('8801');
    $('#server-path').watermark('mybackup');
    $('#server-username').watermark('Username for authentication');
    $('#server-password').watermark('Password for authentication');
    $('#server-options').watermark('Enter connection options here');
    $('#backup-options').watermark('Enter one option pr. line in commandline format, eg. --dblock-size=100MB');

    PRIVATE_DATA.refresh_server_settings = function(callback) {
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
        });
    };

    PRIVATE_DATA.refresh_backup_list = function(callback) {
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
            if ($('#backup-item-template').length > 0 && data.length > 0)
                $.tmpl($('#backup-item-template'), data).prependTo($('#main-list-container'));

            //Fill with jQuery template

            if (callback != null)
                callback(PRIVATE_DATA.backup_list);
        })
        .fail(function(data, status) {
        });
    };

    PRIVATE_DATA.refresh_server_settings();
    PRIVATE_DATA.refresh_backup_list();


    APP_DATA.getServerConfig = function(callback) {
        if (PRIVATE_DATA.server_config == null) {
            PRIVATE_DATA.refresh_server_settings(callback);
        } else {
            callback(PRIVATE_DATA.server_config);
        }
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



});