backupApp.service('AppService', function ($http, $cookies, $q, $cookies, DialogService, appConfig) {
    this.apiurl = '../api/v1';
    this.access_token = null;
    this.access_token_promise = null;

    const self = this;

    function loginRequired() {
        DialogService.dismissAll();
        DialogService.accept('Not logged in', function () {
            window.location = appConfig.login_url;
        });
    }


    var setupConfig = function (method, options, data, targeturl) {
        options = options || {};
        options.method = options.method || method;
        options.responseType = options.responseType || 'json';
        options.headers = options.headers || {};
        if (options.headers['Content-Type'] == null) 
            options.headers['Content-Type'] = 'application/json; charset=utf-8';        
        if (self.access_token != null) 
            options.headers['Authorization'] = `Bearer ${self.access_token}`;
        
        // Disable cache in IE
        if (method == "GET" || method == "HEAD") {
            options.headers['Cache-Control'] = 'no-cache';
            options.headers['Pragma'] = 'no-cache';
        }

        if (($cookies.get('ui-locale') || '').trim().length > 0)
            options.headers['X-UI-Language'] = $cookies.get('ui-locale');

        return options;
    };

    // Input is a function that returns a promise (e.g. $http.get)
    // The function is called and the promise is returned, but if the response is 401, 
    // the current token is removed and a new is obtained
    // If the new token is obtained, the input function is called again, retrying the operation
    // If the new token is not obtained, the user is redirected to the login page
    var installResponseHook = function (promiseAction) {
        var deferred = $q.defer();

        promiseAction().then(
            response => { deferred.resolve(response) },

            response => {
                if (response.status == 401) {
                        
                    // If we have a token, we should remove it and obtain a new one
                    if (self.access_token != null) {
                        self.access_token = null;
                        self.access_token_promise = null;
                    }

                    // If we are already getting a token, this call will return a shared promise
                    self.getAccessToken().then(
                        () => { 
                            // Got a new token, retry the operation
                            promiseAction().then(
                                response2 => deferred.resolve(response2),
                                response2 => { 
                                    deferred.reject(response2);
                                }
                            ); 
                        }, 
                        () => { 
                            // Fail, but report the original failed response, not the refresh response
                            deferred.reject(response);
                        }
                    );

                } else {
                    // Non-authentication error
                    deferred.reject(response);
                }
            }
        );

        return deferred.promise;
    };

    this.clearAccessToken = function () {
        self.access_token = null;
        self.access_token_promise = null;
    }

    // Returns a promise that resolves to the access token
    this.getAccessToken = function () {
        if (self.access_token != null) {
            var deferred = $q.defer();
            deferred.resolve(this.access_token);    
            return deferred.promise;

        } else if (self.access_token_promise != null) {
            return self.access_token_promise;
        } else {
            var deferred = $q.defer();
            self.access_token_promise = deferred.promise;
            $http.post(self.apiurl + '/auth/refresh')
                .then(function (response) {
                    self.access_token = response.data.AccessToken;
                    self.access_token_promise = null;
                    deferred.resolve(self.access_token);
                }, function (response) {
                    // Failed to get a new token, clear the old one
                    self.access_token = null;
                    self.access_token_promise = null;
                    
                    // Auth error, refresh token invalid
                    if (response.status == 401)
                        loginRequired();

                    deferred.reject(response);
                });

            return deferred.promise;
        }
    }
    
    this.get = function (url, options) {
        let rurl = url;
        if (!url.startsWith('http')) {
            rurl = this.apiurl + url;
        }

        return this.getAccessToken().then(() => installResponseHook(() => $http.get(rurl, setupConfig('GET', options, null, rurl))));
    };

    this.patch = function (url, data, options) {
        var rurl = this.apiurl + url;
        options = options || {};
        return this.getAccessToken().then(() => installResponseHook(() => $http.patch(rurl, data, setupConfig('PATCH', options, data, rurl))));
    };

    this.post = function (url, data, options) {
        var rurl = this.apiurl + url;
        options = options || {};
        return this.getAccessToken().then(() => installResponseHook(() => $http.post(rurl, data, setupConfig('POST', options, data, rurl))));
    };

    this.postJson = function (url, data, options) {        
        var rurl = this.apiurl + url;
        options = options || {};
        options.headers = options.headers || {};
        options.headers['Content-Type'] = 'application/json; charset=utf-8';
        return this.getAccessToken().then(() => installResponseHook(() => $http.post(rurl, data, setupConfig('POST', options, data, rurl))));
    };

    this.put = function (url, data, options) {
        var rurl = this.apiurl + url;
        options = options || {};
        return this.getAccessToken().then(() => installResponseHook(() => $http.put(rurl, data, setupConfig('PUT', options, data, rurl))));
    };

    this.delete = function (url, options) {
        var rurl = this.apiurl + url;
        options = options || {};
        return this.getAccessToken().then(() => installResponseHook(() => $http.delete(rurl, setupConfig('DELETE', options, null, rurl))));
    };

    this.get_export_url = function (backupid, passphrase, exportPasswords) {
        var deferred = $q.defer();
        this.post("/auth/issuetoken/export").then(
            resp => {
                var rurl = this.apiurl + '/backup/' + backupid + '/export';
                rurl += '?export-passwords=' + encodeURIComponent(exportPasswords);
                rurl += '&token=' + encodeURIComponent(resp.data.Token);
                if ((passphrase || '').trim().length > 0)
                    rurl += '&passphrase=' + encodeURIComponent(passphrase);        

                deferred.resolve(rurl);
            },
            resp => deferred.reject(resp)
        );

        return deferred.promise;
    }

    
    this.get_bugreport_url = function (reportid) {
        var deferred = $q.defer();
        this.post("/auth/issuetoken/bugreport").then(
            resp => {
                var rurl = this.apiurl + '/bugreport/' + reportid;
                rurl += '?token=' + encodeURIComponent(resp.data.Token);
                deferred.resolve(rurl);
            },
            resp => deferred.reject(resp)
        );

        return deferred.promise;
    }

    this.responseErrorMessage = function (resp) {
        if (resp == null) {
            return '';
        }
        if (typeof resp === 'string' || resp instanceof String) {
            return resp;
        }
        var message = resp.statusText;
        if (resp.data != null) {
            // Different ways to communicate error message (this should be refactored in the server at some point)
            if (resp.data.Message != null) {
                message = resp.data.Message;
            } else if (resp.data.Error != null) {
                message = resp.data.Error;
            } else if (resp.data.message != null) {
                message = resp.data.message;
            } else if (resp.data.reason != null) {
                message = resp.data.reason;
            }
        }
        return message;
    };
});
