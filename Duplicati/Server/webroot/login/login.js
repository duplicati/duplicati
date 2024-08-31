$(document).ready(function() {
    var processing = false;

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