$(document).ready(function() {
    var processing = false;
    
    document.getElementById('no-js-warning').style.display = 'none';

    // Check SSO configuration from the server
    $.ajax({
        url: './api/v1/auth/entra/config',
        type: 'GET',
        accepts: 'application/json'
    })
    .done(function(data) {
        if (data && data.Enabled) {
            if (data.AutoRedirect) {
                // SSO-only mode: redirect straight to Azure AD
                window.location = './api/v1/auth/entra/authorize';
            } else {
                // SSO available: show the button alongside the password form
                document.getElementById('sso-section').style.display = 'block';
            }
        }
    })
    .fail(function() {
        // SSO not available or endpoint not reachable – proceed with password form only
    });

    $('#login-form').on('submit', function() {

        if (processing)
            return;

        processing = true;

        $.ajax({
            url: './api/v1/auth/login',
            type: 'POST',
            contentType: 'application/json',
            data: JSON.stringify({'Password': $('#login-password').val(), 'RememberMe': true })
        })
        .done(function(data) {
            if (data.RefreshNonce)
                localStorage.setItem('v1:persist:duplicati:refreshNonce', data.RefreshNonce);
            window.location = './';
        })
        .fail(function(data) {
            var txt = data;
            if (txt && txt.responseJSON && txt.responseJSON.Error)
                txt = txt.responseJSON.Error;
            else if (txt && txt.statusText)
                txt = txt.statusText;
            alert('Login failed: ' + txt);
            processing = false;
        });

        return false;
    });
});