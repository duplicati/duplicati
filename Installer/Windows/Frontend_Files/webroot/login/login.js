$(document).ready(function() {
    var processing = false;

    $('#login-button').click(function() {

        if (processing)
            return;

        processing = true;

        // First we grab the nonce and salt
        $.ajax({
            url: './login.cgi',
            type: 'POST',
            dataType: 'json',
            data: {'get-nonce': 1}
        })
        .done(function(data) {
            var saltedpwd = CryptoJS.SHA256(CryptoJS.enc.Hex.parse(CryptoJS.enc.Utf8.parse($('#login-password').val()) + CryptoJS.enc.Base64.parse(data.Salt)));

            var noncedpwd = CryptoJS.SHA256(CryptoJS.enc.Hex.parse(CryptoJS.enc.Base64.parse(data.Nonce) + saltedpwd)).toString(CryptoJS.enc.Base64);

            $.ajax({
                url: './login.cgi',
                type: 'POST',
                dataType: 'json',
                data: {'password': noncedpwd }
            })
            .done(function(data) {
                window.location = './';
            })
            .fail(function(data) {
                var txt = data;
                if (txt && txt.statusText)
                    txt = txt.statusText;
                alert('Login failed: ' + txt);
                processing = false;
            });
        })
        .fail(function(data) {
            var txt = data;
            if (txt && txt.statusText)
                txt = txt.statusText;

            alert('Failed to get nonce: ' + txt);
            processing = false;
        });




        return false;
    });

});
