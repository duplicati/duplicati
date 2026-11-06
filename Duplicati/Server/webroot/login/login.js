document.addEventListener('DOMContentLoaded', function() {
    var processing = false;
    
    document.getElementById('no-js-warning').style.display = 'none';

    // Posts the body as JSON and resolves with the parsed reply, or rejects with the error text
    function postJson(url, body) {
        return fetch(url, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: body === undefined ? undefined : JSON.stringify(body)
        })
        .catch(function() {
            throw 'error';
        })
        .then(function(response) {
            return response.json()
                .catch(function() { return null; })
                .then(function(data) {
                    if (response.ok)
                        return data || {};
                    throw (data && data.Error) || response.statusText;
                });
        });
    }

    document.getElementById('login-form').addEventListener('submit', function(e) {
        e.preventDefault();

        if (processing)
            return;

        processing = true;

        postJson('./api/v1/auth/login', {'Password': document.getElementById('login-password').value, 'RememberMe': true })
        .then(function(data) {
            if (data.RefreshNonce)
                localStorage.setItem('v1:persist:duplicati:refreshNonce', data.RefreshNonce);
            window.location = './';
        }, function(txt) {
            alert('Login failed: ' + txt);
            processing = false;
        });
    });
});