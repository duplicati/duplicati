/*
 * Edituri app code
 */

BACKEND_STATE = null;

$(document).ready(function() {

    var resetform = function() {
        $('#server-name').watermark('example.com');
        $('#server-port').watermark('8801');
        $('#server-path').watermark('mybackup');
        $('#server-username').watermark('Username for authentication');
        $('#server-password').watermark('Password for authentication');
        $('#server-options').watermark('Enter connection options here');
        $('#edit-dialog-extensions').empty();

        if (BACKEND_STATE.custom_cleanup != null) {
            BACKEND_STATE.custom_cleanup($('#connection-uri-dialog'), $('#edit-dialog-extensions'));
            BACKEND_STATE.custom_cleanup = null;
        }
    };

    $('#connection-uri-dialog').on( "dialogopen", function( event, ui ) {
        BACKEND_STATE = {};

        APP_DATA.getServerConfig(function(serverdata) {
            var drop = $('#backend-type');
            drop.empty();

            BACKEND_STATE.modules = serverdata['BackendModules'];

            for (var i = 0; i < BACKEND_STATE.modules.length; i++)
              drop.append($("<option></option>").attr("value", BACKEND_STATE.modules[i].Key).text(BACKEND_STATE.modules[i].DisplayName));

            drop.change();
        },
        function() {
            alert('Failed to get server setup...')
        });
    });

    $('#backend-type').change(function() {
        var k = $('#backend-type').val();
        var m = null;
        var hasssl = false;

        var cfg = APP_DATA.plugins.backend[k] || {};

        // Auto-detect for SSL
        if (cfg.hasssl == null) {
            for (var i in BACKEND_STATE.modules)
                if (BACKEND_STATE.modules[i].Key == k)
                    m = BACKEND_STATE.modules[i];
            
            if (m && m.Options)
                for(var i in m.Options)
                    if (m.Options[i].Name == 'use-ssl')
                        cfg.hasssl = true;
        }

        resetform();

        // Simple config changes
        $('#server-use-ssl').toggle(cfg.hasssl == true);
        $('#server-use-ssl-label').toggle(cfg.hasssl == true);

        $('#server-name-and-port-label').toggle(cfg.hideserverandport != true);
        $('#server-name-and-port').toggle(cfg.hideserverandport != true);

        $('#server-port').toggle(cfg.hideport != true);
        $('#server-username-and-password').toggle(cfg.hideusernameandpassword != true);

        BACKEND_STATE.custom_cleanup = cfg.custom_cleanup;
        
        if (cfg.custom_callback != null) {
            cfg.custom_callback($('#connection-uri-dialog'), $('#edit-dialog-extensions'));
        }


    });
});