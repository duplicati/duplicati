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

    $('.button').button();

    $('#main-list-container > div.main-backup-entry').remove();
    $('#loading-dialog').dialog({modal: true}).show();

    //$('#edit-dialog').dialog('close');

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
            })
        }
    };


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

    $('#main-newbackup').click(function() {
        $("#edit-dialog").dialog('open');
    });

    $('#edit-dialog').tabs({ active: 0, activate: function(event, ui) {
        var buttons = $(this).parent().find('.ui-dialog-buttonpane').find('.ui-button');

        if (ui.newPanel[0].id == 'edit-tab-general')
            $(buttons[0]).button('option', 'disabled', true);
        else if (ui.oldPanel[0].id == 'edit-tab-general')
            $(buttons[0]).button('option', 'disabled', false);

        if (ui.newPanel[0].id == 'edit-tab-options')
            $(buttons[1]).find('span').each(function(ix, el) {el.innerText = 'Save'});
        else if (ui.oldPanel[0].id == 'edit-tab-options')
            $(buttons[1]).find('span').each(function(ix, el) {el.innerText = 'Next'});
    }});

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
                cur = Math.min(cur+1, 4);
                $('#edit-dialog').tabs( "option", "active", cur);
            }}
        ]
    });    

    // Hack the tabs into the dialog title
    $('#edit-dialog').parent().find('.ui-dialog-titlebar').after($('#edit-dialog').find('.ui-tabs-nav'));
    $('#edit-dialog').parent().addClass('ui-tabs');

});