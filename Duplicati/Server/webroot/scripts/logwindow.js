$(document).ready(function() {
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

        var datacalback = function(data) {
            $.tmpl($('#log-data-template'), data).appendTo($('#log-dialog'));
            $('#log-dialog').find('.log-exception').each(function(i, e) {
                $(e).html(nl2br($(e).html()));
            });

            $('#log-dialog').find('.log-exception').click(function(e) {
                e.bubbles = false;
                return false;
            });
            $('#log-dialog').find('.log-timestamp').each(function(i, e) {
                $(e).text((new Date(parseInt($(e).attr('title')))).toLocaleString());
            });
            $('#log-dialog .log-entry').click(function(e) {
                $(e.target).closest('.log-entry').toggleClass('expanded');
            });
        };

        if (data.first == null) {
            $('#log-dialog').empty();
            APP_DATA.callServer({'action': 'read-log', 'pagesize': 100}, datacalback);
        };

    };

    $.showBackupLog = function(id) {

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
                $('#log-dialog').dialog('close');
            }}
        ]        
    });
});