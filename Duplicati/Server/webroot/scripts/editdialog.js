/*
 * Editdialog app code
 */
$(document).ready(function() {
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

    var updatePasswordIndicator = function() {
        $.passwordStrength($('#encryption-password')[0].value, function(r) {
            var f = $('#backup-password-strength');
            if (r == null) {
                f.text('Strength: Unknown');
                r = {score: -1}
            } else {
                f.text(r.crack_time_display);
            }

            f.removeClass('password-strength-0');
            f.removeClass('password-strength-1');
            f.removeClass('password-strength-2');
            f.removeClass('password-strength-3');
            f.removeClass('password-strength-4');
            f.removeClass('password-strength-unknown');

            if (r.score == 0)
                f.addClass('password-strength-0');
            else if (r.score == 1)
                f.addClass('password-strength-1');
            else if (r.score == 2)
                f.addClass('password-strength-2');
            else if (r.score == 3)
                f.addClass('password-strength-3');
            else if (r.score == 4)
                f.addClass('password-strength-4');
            else
                f.addClass('password-strength-unknown');

        });

        if ($('#encryption-password')[0].value != $('#repeat-password')[0].value) {
            $('#repeat-password').addClass('password-mismatch');
            //$('#encryption-password').addClass('password-mismatch');
        } else {
            $('#repeat-password').removeClass('password-mismatch');
            //$('#encryption-password').removeClass('password-mismatch');
        }
    }

    $('#encryption-password').change(updatePasswordIndicator);
    $('#repeat-password').change(updatePasswordIndicator);
    $('#encryption-password').keyup(updatePasswordIndicator);
    $('#repeat-password').keyup(updatePasswordIndicator);

    $('#toggle-show-password').click(function() {
        $('#encryption-password').togglePassword();    
    });

    $('#encryption-password').on('passwordShown', function () {
        $('#toggle-show-password').text('Hide passwords')
        $('#repeat-password').showPassword();
        //$('#repeat-password').hide();
        //$('#repeat-password-label').hide();
    }).on('passwordHidden', function () {
        $('#toggle-show-password').text('Show passwords')        
        $('#repeat-password').hidePassword();
        //$('#repeat-password').show();
        //$('#repeat-password-label').show();
    });    

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