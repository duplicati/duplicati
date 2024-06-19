backupApp.service('AppService', function ($http, $cookies, $q, $cookies, DialogService, appConfig) {
    this.apiurl = '../api/v1';
    this.proxy_url = null;
    this.access_token = null;
    this.access_token_promise = null;

    var self = this;

    var dummyconfig = function (method, options, data, targeturl) {
    };
    this.proxy_config = dummyconfig;

    var setupConfig = function (method, options, data, targeturl) {
        options = options || {};
        options.method = options.method || method;
        options.responseType = options.responseType || 'json';
        options.headers = options.headers || {};
        if (this.access_token != null) {
            options.headers['Authorization'] = `Bearer ${self.access_token}`;
        }

        if ((method == "POST" || method == "PATCH" || method == "PUT") && options.headers['Content-Type'] == null && data != null && typeof (data) != typeof ('')) {
            options.headers['Content-Type'] = 'application/x-www-form-urlencoded; charset=utf-8';
            options.transformRequest = function (obj) {
                var str = [];
                for (var p in obj) {
                    var arg = obj[p] == null ? '' : encodeURIComponent(obj[p]);
                    str.push(encodeURIComponent(p) + "=" + arg);
                }
                return str.join("&");
            };
        }

        // Disable cache in IE
        if (method == "GET" || method == "HEAD") {
            options.headers['Cache-Control'] = 'no-cache';
            options.headers['Pragma'] = 'no-cache';
        }

        if (($cookies.get('ui-locale') || '').trim().length > 0)
            options.headers['X-UI-Language'] = $cookies.get('ui-locale');

        if (self.proxy_config != null)
            self.proxy_config(method, options, data, targeturl);

        return options;
    };

    var installResponseHook = function (promiseAction) {
        var deferred = $q.defer();
        var self = this;

        function successCallback(response) {
            deferred.resolve(response);
        }

        function errorCallback(response) {
            if (response.status == 401) {
                // If we are actully logged in, we should log out and obtain a new token
                if (self.access_token != null) {
                    self.access_token = null;
                    self.access_token_promise = null;

                    self.injectAccessToken({}, () => { promiseAction().then(successCallback, errorCallback); });
                    return;
                }

                DialogService.dismissAll();
                DialogService.accept('Not logged in', function () {
                    window.location = appConfig.login_url;
                });
                deferred.reject(response);
            } else {
                deferred.reject(response);
            }
        }

        promiseAction().then(successCallback, errorCallback);

        return deferred.promise;
    };

    var chainPromise = function (deferred, callback) {
        callback().then(function (response) {
            deferred.resolve(response);
        }, function (response) { 
            deferred.reject(response); 
        });        
    };

    this.injectAccessToken = function (options, callback) {
        var self = this;
        options.headers = options.headers || {};
        if (this.access_token != null) {
            options.headers['Authorization'] = `Bearer ${self.access_token}`;
            return callback();
        }

        var deferred = $q.defer();
        if (self.access_token_promise == null) {
            self.access_token_promise = deferred.promise;
            var url = self.apiurl + '/auth/refresh';
            $http.post(self.proxy_url == null ? url : self.proxy_url)
                .then(function (response) {
                    self.access_token = response.data.AccessToken;
                    self.access_token_promise = null;
                    options.headers['Authorization'] = `Bearer ${self.access_token}`;
                    chainPromise(deferred, callback);
                }, function (response) {
                    DialogService.dismissAll();
                    DialogService.accept('Not logged in', function () {
                        window.location = appConfig.login_url;
                    });

                    deferred.reject(response);
                });

        } else {            
            self.access_token_promise.then(() => { 
                options.headers['Authorization'] = `Bearer ${self.access_token}`;
                chainPromise(deferred, callback);
            }, (response) => deferred.reject(response));
        }
        return deferred.promise;
    }
    
    this.get = function (url, options) {
        let rurl = url;
        if (!url.startsWith('http')) {
            rurl = this.apiurl + url;
        }

        options = options || {};
        return this.injectAccessToken(options, () => installResponseHook(() => $http.get(this.proxy_url == null ? rurl : this.proxy_url, setupConfig('GET', options, null, rurl))));
    };

    this.patch = function (url, data, options) {
        var rurl = this.apiurl + url;
        options = options || {};
        return this.injectAccessToken(options, () => installResponseHook(() => $http.patch(this.proxy_url == null ? rurl : this.proxy_url, data, setupConfig('PATCH', options, data, rurl))));
    };

    this.post = function (url, data, options) {
        var rurl = this.apiurl + url;
        options = options || {};
        return this.injectAccessToken(options, () => installResponseHook(() => $http.post(this.proxy_url == null ? rurl : this.proxy_url, data, setupConfig('POST', options, data, rurl))));
    };

    this.postJson = function (url, data, options) {     
   
        var rurl = this.apiurl + url;
        options = options || {};
        options.headers = options.headers || {};
        options.headers['Content-Type'] = 'application/json; charset=utf-8';
        return this.injectAccessToken(options, () => installResponseHook(() => $http.post(this.proxy_url == null ? rurl : this.proxy_url, data, setupConfig('POST', options, data, rurl))));
    };

    this.put = function (url, data, options) {
        var rurl = this.apiurl + url;
        options = options || {};
        return this.injectAccessToken(options, () => installResponseHook(() => $http.put(this.proxy_url == null ? rurl : this.proxy_url, data, setupConfig('PUT', options, data, rurl))));
    };

    this.delete = function (url, options) {
        var rurl = this.apiurl + url;
        options = options || {};
        return this.injectAccessToken(options, () => installResponseHook(() => $http.delete(this.proxy_url == null ? rurl : this.proxy_url, setupConfig('DELETE', options, null, rurl))));
    };

    this.get_access_token = function () {
        var deferred = $q.defer();
        const self = this;
        this.injectAccessToken({}, () => {             
            deferred.resolve(self.access_token); 
            return $q.resolve();
        });
        return deferred.promise;
    }

    this.get_export_url = function (backupid, passphrase, exportPasswords) {
        var rurl = this.apiurl + '/backup/' + backupid + '/export';
        rurl += '?export-passwords=' + encodeURIComponent(exportPasswords);
        if ((passphrase || '').trim().length > 0)
            rurl += '&passphrase=' + encodeURIComponent(passphrase);

        if (this.proxy_url != null)
            return this.proxy_url + '?x-proxy-path=' + encodeURIComponent(rurl);
        return rurl;
    };

    this.get_import_url = function (passphrase) {
        var rurl = this.apiurl + '/backups/import';
        if ((passphrase || '').trim().length > 0)
            rurl += '&passphrase=' + encodeURIComponent(passphrase);

        if (this.proxy_url != null)
            return this.proxy_url + '?x-proxy-path=' + encodeURIComponent(rurl);
        return rurl;
    };

    this.get_bugreport_url = function (reportid) {
        var rurl = this.apiurl + '/bugreport/' + reportid;

        if (this.proxy_url != null)
            return this.proxy_url + '?x-proxy-path=' + encodeURIComponent(rurl);

        return rurl;
    };

    this.log_out = function () {
        var rurl = '/logout.cgi';

        return installResponseHook($http.get(this.proxy_url == null ? rurl : this.proxy_url, setupConfig('GET', {}, null, rurl)));
    };

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
