$(document).ready(function() {
    var appdatacallback = function(data, tag) {
        var td = $.tmpl($('#log-data-template'), data);
        td.appendTo(tag);

        td.find('.log-exception').each(function(i, e) {
            $(e).html(nl2br($(e).html()));
        });

        td.find('.log-exception').click(function(e) {
            e.bubbles = false;
            return false;
        });
        td.find('.log-timestamp').each(function(i, e) {
            $(e).text((new Date(parseInt($(e).attr('title')))).toLocaleString());
        });
        td.click(function(e) {
            $(this).toggleClass('expanded');
        });
    };

    var livedatacallback = function(data, tag) {
        var scrolledToEnd = tag.parent().scrollTop() > tag.height() - (tag.parent().height()*2);

        var td = $.tmpl($('#live-log-data-template'), data)
        td.appendTo(tag);

        td.find('.log-exception').each(function(i, e) {
            $(e).html(nl2br($(e).html()));
        });

        td.find('.log-exception').click(function(e) {
            e.bubbles = false;
            return false;
        });
        td.find('.log-timestamp').each(function(i, e) {
            $(e).text(new Date($(e).attr('title')).toLocaleString());
        });
        td.click(function(e) {
            $(this).toggleClass('expanded');
        });

        if (data != null && data.length > 0 && scrolledToEnd)
            tag.parent().scrollTop(tag.height());
    };

    var generaldatacallback = function(data, tag) {
        var td = $.tmpl($('#log-backup-general-template'), data)
        td.appendTo(tag);

        td.find('.log-exception').each(function(i, e) {
            $(e).html(nl2br($(e).html()));
        });
        td.find('.log-message').each(function(i, e) {
            $(e).html(nl2br($(e).html()));
        });

        td.find('.log-exception').click(function(e) {
            return e.bubbles = false;
        });
        td.find('.log-message').click(function(e) {
            return e.bubbles = false;
        });

        td.find('.log-timestamp').each(function(i, e) {
            $(e).text((new Date(parseInt($(e).attr('title')))).toLocaleString());
        });

        td.click(function(e) {
            $(this).toggleClass('expanded');
        });
    };

    var remotedatacallback = function(data, tag) {
        var td = $.tmpl($('#log-backup-remote-template'), data);
        td.appendTo(tag);

        td.find('.log-data').each(function(i, e) {
            $(e).html(nl2br($(e).html()));
        });
        td.find('.log-data').click(function(e) {
            return e.bubbles = false;
        });

        td.find('.log-timestamp').each(function(i, e) {
            $(e).text((new Date(parseInt($(e).attr('title')))).toLocaleString());
        });
        
        td.click(function(e) {
            $(e.target).closest('.log-entry').toggleClass('expanded');
        });
    };

    var refreshRunning = false;
    var refreshId = 0;

    var refreshLiveData = function() {
        if (refreshRunning)
            return;

        var v = $('#log-tab-live-level').val();
        if (v == '' || v == 'disabled')
            return;

        if (!$('#log-dialog').dialog('isOpen'))
            return;

        refreshRunning = true;

        APP_DATA.callServer({'action': 'poll-log-messages', 'level': v, 'id': refreshId},
            function(data) {
                for(var n in data)
                    refreshId = Math.max(refreshId, data[n].ID);

                refreshRunning = false; 
                livedatacallback(data, $('#log-tab-live'));
                setTimeout(refreshLiveData, 3000);
            },
            function() {
                refreshRunning = false; 
                setTimeout(refreshLiveData, 1000);
            });
    };

    $.showAppLog = function() {
        var dlg_buttons = $('#log-dialog').parent().find('.ui-dialog-buttonpane').find('.ui-button');

        $('#log-dialog').dialog('open');

        var data = $('#log-tab-stored').data('log');
        if (!data) {
            data = {
                first: null,
                last: null
            };
            $('#log-tab-stored').data('log', data);
        }

        if (data.first == null) {
            $('#log-tab-stored').empty();
            APP_DATA.callServer({'action': 'read-log', 'pagesize': 100}, function(data) {
                appdatacallback(data, $('#log-tab-stored'));
            });
        };

        var loglevel = $('#log-tab-live-level');
        loglevel.empty();

        $('#log-tab-live').find('.log-entry').remove();

        loglevel.change(function() { 
            refreshId = 0;
            
            var v = $('#log-tab-live-level').val();
            if (v == '' || v == 'disabled')
                return;

            $('#log-tab-live').find('.log-entry').remove();
            refreshLiveData();
        });

        APP_DATA.getServerConfig(function(server_config) {

            loglevel.append($('<option></option>')
                 .attr('value', 'disabled')
                 .text('Disabled')); 

            for(var n in server_config.LogLevels)
                loglevel.append($('<option></option>')
                         .attr('value',server_config.LogLevels[n])
                         .text(server_config.LogLevels[n]));    

        }, function() { alert('Failed to read server data'); });

    };

    $.showBackupLog = function(id) {

        $('#backup-log-tab-general').empty();
        $('#backup-log-tab-remote').empty();

        $('<div id="restore-search-loader" class="small-loader-icon">').appendTo($('#backup-log-tab-general'));
        $('<div id="restore-search-loader" class="small-loader-icon">').appendTo($('#backup-log-tab-remote'));

        var loglevel = $('#backup-log-live-level');
        loglevel.empty();

        loglevel.change(refreshLiveData);

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


    $('#log-dialog').tabs({ active: 0 });
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
                $(this).dialog('close');
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
                $(this).dialog('close');
            }}
        ]        
    });
});