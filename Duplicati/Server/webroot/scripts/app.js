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

    $('#main-list-container').empty();
    $('#main-list-loader').show();

    PRIVATE_DATA.refresh_server_settings = function(callback) {
        $.ajax({
            url: APP_CONFIG.server_url,
            data: { action: 'system-info' }
        })
        .done(function(data) {
            PRIVATE_DATA.server_config = data;
            $('#main-list-loader').hide();
            if (callback != null)
                callback(PRIVATE_DATA.server_config);
        })
        .fail(function(data, status) {
        });
    };

    PRIVATE_DATA.refresh_server_settings();

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