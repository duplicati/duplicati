$(document).ready(function() {
    var appdatacallback = function(data, tag) {
        $.tmpl($('#log-data-template'), data).appendTo(tag);
        tag.find('.log-exception').each(function(i, e) {
            $(e).html(nl2br($(e).html()));
        });

        tag.find('.log-exception').click(function(e) {
            e.bubbles = false;
            return false;
        });
        tag.find('.log-timestamp').each(function(i, e) {
            $(e).text((new Date(parseInt($(e).attr('title')))).toLocaleString());
        });
        tag.find('.log-entry').click(function(e) {
            $(e.target).closest('.log-entry').toggleClass('expanded');
        });
    };

    var generaldatacallback = function(data, tag) {
        $.tmpl($('#log-backup-general-template'), data).appendTo(tag);
        tag.find('.log-exception').each(function(i, e) {
            $(e).html(nl2br($(e).html()));
        });
        tag.find('.log-message').each(function(i, e) {
            $(e).html(nl2br($(e).html()));
        });

        tag.find('.log-exception').click(function(e) {
            return e.bubbles = false;
        });
        tag.find('.log-message').click(function(e) {
            return e.bubbles = false;
        });

        tag.find('.log-timestamp').each(function(i, e) {
            $(e).text((new Date(parseInt($(e).attr('title')))).toLocaleString());
        });

        tag.find('.log-entry').click(function(e) {
            $(e.target).closest('.log-entry').toggleClass('expanded');
        });
    };

    var remotedatacallback = function(data, tag) {
        $.tmpl($('#log-backup-remote-template'), data).appendTo(tag);
        tag.find('.log-data').each(function(i, e) {
            $(e).html(nl2br($(e).html()));
        });
        tag.find('.log-data').click(function(e) {
            return e.bubbles = false;
        });

        tag.find('.log-timestamp').each(function(i, e) {
            $(e).text((new Date(parseInt($(e).attr('title')))).toLocaleString());
        });
        
        tag.find('.log-entry').click(function(e) {
            $(e.target).closest('.log-entry').toggleClass('expanded');
        });
    };

    $.showAppLog = function() {
        var dlg_buttons = $('#log-dialog').parent().find('.ui-dialog-buttonpane').find('.ui-button');

        $('#log-dialog').dialog('open');

        var data = $('#log-dialog').data('log');
        if (!data) {
            data = {
                first: null,
                last: null
            };
            $('#log-dialog').data('log', data);
        }

        if (data.first == null) {
            $('#log-dialog').empty();
            APP_DATA.callServer({'action': 'read-log', 'pagesize': 100}, function(data) {
                appdatacallback(data, $('#log-dialog'));
            });
        };

    };

    $.showBackupLog = function(id) {

        $('#backup-log-tab-general').empty();
        $('#backup-log-tab-remote').empty();

        $('<div id="restore-search-loader" class="small-loader-icon">').appendTo($('#backup-log-tab-general'));
        $('<div id="restore-search-loader" class="small-loader-icon">').appendTo($('#backup-log-tab-remote'));

        $('#backup-log-dialog').dialog('open');

        APP_DATA.callServer({'action': 'read-log', 'pagesize': 100, 'id': id, 'remotelog': false}, function(generaldata) {
            APP_DATA.callServer({'action': 'read-log', 'pagesize': 100, 'id': id, 'remotelog': true}, function(remotedata) {
                $('#backup-log-tab-general').empty();
                $('#backup-log-tab-remote').empty();

                generaldatacallback(generaldata, $('#backup-log-tab-general'));
                remotedatacallback(remotedata, $('#backup-log-tab-remote'));
            }, function() { alert('Failed to read log data'); });
        }, function() { alert('Failed to read log data'); });
    };


    $('#log-dialog').dialog({
        minWidth: 320, 
        width: $('body').width > 600 ? 320 : 600, 
        minHeight: 480, 
        height: 500, 
        modal: true,
        autoOpen: false,
        closeOnEscape: true,
        buttons: [
            { text: 'Close', disabled: false, click: function(event, ui) {
                $('#log-dialog').dialog('close');
            }}
        ]        
    }); 
    
    $('#backup-log-dialog').tabs({ active: 0 });
    $('#backup-log-dialog').dialog({
        minWidth: 320, 
        width: $('body').width > 600 ? 320 : 600, 
        minHeight: 480, 
        height: 500, 
        modal: true,
        autoOpen: false,
        closeOnEscape: true,
        buttons: [
            { text: 'Close', disabled: false, click: function(event, ui) {
                $('#backup-log-dialog').dialog('close');
            }}
        ]        
    });
});