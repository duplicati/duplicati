/*
 * Editdialog app code
 */
$(document).ready(function() {
    $('.button').button();

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

    $('.save-button').click(function() {
    });

    $('#connection-uri-dialog').dialog({ 
        modal: true, 
        minWidth: 320, 
        width: $('body').width, 
        autoOpen: false, 
        closeOnEscape: true,
        buttons: [
            {text: 'Cancel', click: function() { $( this ).dialog( "close" ); } },
            {text: 'Create URI', click: function() { $( this ).dialog( "close" ); } }
        ]
     })

    $('#edit-connection-uri-link').click(function() {
        $('#connection-uri-dialog').dialog('open');
    });

});