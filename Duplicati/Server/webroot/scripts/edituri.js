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
                field = config.field === false ? null : $('<a />').text(config.title).addClass('action-link ' + (config.fieldclass || '')).each(function(i, e) {
                    if (config.href)
                        e.href = config.href;
                    e.target = config.target || '_blank';
                });
            } else {
                field = config.field === false ? null : $('<input type="' + (config.type || 'text') + '" />').addClass('text ui-widget-content ui-corner-all ' + (config.fieldclass || ''));
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
                        r[k].each(function(i,e) { e.title = config.title});

            if (config.watermark && field != null)
                field.watermark(config.watermark);


            if (config.after)
                outer.insertAfter(config.after);
            else if (config.before)
                outer.insertBefore(config.before);
            else if (config.append)
                config.append(outer);
            else
                $('#edit-dialog-extensions').append(outer);

            return r;
        }
    };

    var resetform = function() {
        $('#server-name').watermark('example.com');
        $('#server-port').watermark('8801');
        $('#server-path').watermark('mybackup');
        $('#server-username').watermark('Username for authentication');
        $('#server-password').watermark('Password for authentication');
        $('#server-options').watermark('Enter connection options here');
        $('#edit-dialog-extensions').empty();
        $('#server-username-label').text('Username');
        $('#server-password-label').text('Password');

        if (BACKEND_STATE && BACKEND_STATE.current_state && BACKEND_STATE.current_state.custom_cleanup )
            BACKEND_STATE.current_state.custom_cleanup($('#connection-uri-dialog'), $('#edit-dialog-extensions'));        
        BACKEND_STATE.current_state = null;
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

        if (cfg.usernamelabel)
            $('#server-username-label').text(cfg.usernamelabel);
        if (cfg.passwordlabel)
            $('#server-password-label').text(cfg.passwordlabel);
        if (cfg.usernamewatermark)
            $('#server-username-label').watermark(cfg.usernamewatermark);
        if (cfg.passwordwatermark)
            $('#server-password-label').watermark(cfg.passwordwatermark);

        BACKEND_STATE.current_state = cfg;

        if (cfg.custom_callback != null) {
            cfg.custom_callback($('#connection-uri-dialog'), $('#edit-dialog-extensions'));
        }


    });
});