$(document).ready(function() {
    var processing = false;
    
    function getQueryParam(name) {
        var regex = new RegExp("[\\?&]" + name + "=([^&#]*)"), results = regex.exec(location.search);
        return results === null ? "" : decodeURIComponent(results[1].replace(/\+/g, " "));
    }

    // Function that logs in using a stored refresh token
    function tryLogin(followUpCallback) {
        if (processing)
            return;

        processing = true;
        const storedNonce = localStorage.getItem('v1:persist:duplicati:refreshNonce');
        const body = storedNonce ? { Nonce: storedNonce } : undefined;

        $.ajax({
            url: './api/v1/auth/refresh',
            type: 'POST',
            contentType: 'application/json',
            data: JSON.stringify(body)
        })
        .done(function(data) {
            if (data.RefreshNonce)
                localStorage.setItem('v1:persist:duplicati:refreshNonce', data.RefreshNonce);
            window.location = './';
        })
        .fail(function(data) {
            processing = false;
            if (followUpCallback)
            {
                followUpCallback();
            }
            else
            {
                var txt = data;
                if (txt && txt.responseJSON && txt.responseJSON.Error)
                    txt = txt.responseJSON.Error;
                else if (txt && txt.statusText)
                    txt = txt.statusText;
                alert('Signin failed: ' + txt);
            }
        });

        return false;            
    }

    // Function that logs in using a sign-in token
    function submitToken() {
        if (processing)
            return;

        processing = true;

        $.ajax({
            url: './api/v1/auth/signin',
            type: 'POST',
            contentType: 'application/json',            
            data: JSON.stringify({'SigninToken': $('#signin-token').val(), 'RememberMe': true })
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
            alert('Signin failed: ' + txt);
            processing = false;
        });

        return false;        
    }

    $('#signin-form').on('submit', submitToken);

    var token = getQueryParam('token');
    if (token) {
        $('#signin-token').val(token);
        tryLogin(() => submitToken());
    } else {
        tryLogin(null);
    }


});
