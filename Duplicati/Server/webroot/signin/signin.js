document.addEventListener('DOMContentLoaded', function() {
    var processing = false;

    document.getElementById('no-js-warning').style.display = 'none';
    
    function getQueryParam(name) {
        var regex = new RegExp("[\?&]" + name + "=([^&#]*)"), results = regex.exec(location.search);
        return results === null ? "" : decodeURIComponent(results[1].replace(/\+/g, " "));
    }

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

    // Function that logs in using a stored refresh token
    function tryLogin(followUpCallback) {
        if (processing)
            return;

        processing = true;
        const storedNonce = localStorage.getItem('v1:persist:duplicati:refreshNonce');
        const body = storedNonce ? { Nonce: storedNonce } : undefined;

        postJson('./api/v1/auth/refresh', body)
        .then(function(data) {
            if (data.RefreshNonce)
                localStorage.setItem('v1:persist:duplicati:refreshNonce', data.RefreshNonce);
            window.location = './';
        }, function(txt) {
            processing = false;
            if (followUpCallback)
            {
                followUpCallback();
            }
            else
            {
                alert('Signin failed: ' + txt);
            }
        });
    }

    // Function that logs in using a sign-in token
    function submitToken() {
        if (processing)
            return;

        processing = true;

        postJson('./api/v1/auth/signin', {'SigninToken': document.getElementById('signin-token').value, 'RememberMe': true })
        .then(function(data) {
            if (data.RefreshNonce)
                localStorage.setItem('v1:persist:duplicati:refreshNonce', data.RefreshNonce);
            window.location = './';
        }, function(txt) {
            alert('Signin failed: ' + txt);
            processing = false;
        });
    }

    document.getElementById('signin-form').addEventListener('submit', function(e) {
        e.preventDefault();
        submitToken();
    });

    var token = getQueryParam('token');
    if (token) {
        document.getElementById('signin-token').value = token;
        tryLogin(() => submitToken());
    } else {
        tryLogin(null);
    }


});
