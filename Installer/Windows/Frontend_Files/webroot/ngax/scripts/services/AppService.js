backupApp.service('AppService', function($http, $cookies, $q, $cookies, DialogService, appConfig) {
    this.apiurl = '../api/v1';
    this.proxy_url = null;

    var self = this;

    var dummyconfig = function(method, options, data, targeturl) { };
    this.proxy_config = dummyconfig;

    var setupConfig = function (method, options, data, targeturl) {
        options = options || {};
        options.method = options.method || method;
        options.responseType = options.responseType || 'json';
        options.xsrfHeaderName ='X-XSRF-Token';
        options.xsrfCookieName = 'xsrf-token';
        options.headers = options.headers || {};

        if ((method == "POST" || method == "PATCH" || method == "PUT") && options.headers['Content-Type'] == null && data != null && typeof(data) != typeof('')) {
            options.headers['Content-Type'] = 'application/x-www-form-urlencoded; charset=utf-8';
            options.transformRequest = function(obj) {
                var str = [];
                for(var p in obj) {
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

    var installResponseHook = function(promise) {
        var deferred = $q.defer();

        promise.then(function successCallback(response) {
            deferred.resolve(response);
        }, function errorCallback(response) {
            if (response.status == 401){
                DialogService.dismissAll();
                DialogService.accept('Not logged in', function () {
                    window.location = appConfig.login_url;
                });
                return;
            }
            deferred.reject(response);
        });

        return deferred.promise;
    };

    this.get = function(url, options) {
        var rurl = this.apiurl + url;

        return installResponseHook($http.get(this.proxy_url == null ? rurl : this.proxy_url, setupConfig('GET', options, null, rurl)));
    };

    this.patch = function(url, data, options) {
        var rurl = this.apiurl + url;
        return installResponseHook($http.patch(this.proxy_url == null ? rurl : this.proxy_url, data, setupConfig('PATCH', options, data, rurl)));
    };

    this.post = function(url, data, options) {
        var rurl = this.apiurl + url;
        return installResponseHook($http.post(this.proxy_url == null ? rurl : this.proxy_url, data, setupConfig('POST', options, data, rurl)));
    };

    this.put = function(url, data, options) {
        var rurl = this.apiurl + url;
        return installResponseHook($http.put(this.proxy_url == null ? rurl : this.proxy_url, data, setupConfig('PUT', options, data, rurl)));
    };

    this.delete = function(url, options) {
        var rurl = this.apiurl + url;
        return installResponseHook($http.delete(this.proxy_url == null ? rurl : this.proxy_url, setupConfig('DELETE', options, null, rurl)));
    };


    this.get_export_url = function(backupid, passphrase, exportPasswords) {
        var rurl = this.apiurl + '/backup/' + backupid + '/export?x-xsrf-token=' + encodeURIComponent($cookies.get('xsrf-token'));
        rurl += '&export-passwords=' + encodeURIComponent(exportPasswords);
        if ((passphrase || '').trim().length > 0)
            rurl += '&passphrase=' + encodeURIComponent(passphrase);

        if (this.proxy_url != null)
            return this.proxy_url + '?x-proxy-path=' + encodeURIComponent(rurl);
        return rurl;
    };

    this.get_import_url = function(passphrase) {
        var rurl = this.apiurl + '/backups/import?x-xsrf-token=' + encodeURIComponent($cookies.get('xsrf-token'));
        if ((passphrase || '').trim().length > 0)
            rurl += '&passphrase=' + encodeURIComponent(passphrase);

        if (this.proxy_url != null)
            return this.proxy_url + '?x-proxy-path=' + encodeURIComponent(rurl);
        return rurl;
    };

    this.get_bugreport_url = function(reportid) {
        var rurl = this.apiurl + '/bugreport/' + reportid + '?x-xsrf-token=' + encodeURIComponent($cookies.get('xsrf-token'));

        if (this.proxy_url != null)
            return this.proxy_url + '?x-proxy-path=' + encodeURIComponent(rurl);

        return rurl;
    };

    this.log_out = function() {
        var rurl = '/logout.cgi';

        return installResponseHook($http.get(this.proxy_url == null ? rurl : this.proxy_url, setupConfig('GET', {}, null, rurl)));
    };
});
