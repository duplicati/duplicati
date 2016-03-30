backupApp.service('AppService', function($http, $cookies, $q, DialogService, appConfig) {
    this.apiurl = '/api/v1';

    var setupConfig = function (method, options, data) {
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
                for(var p in obj)
                str.push(encodeURIComponent(p) + "=" + encodeURIComponent(obj[p]));
                return str.join("&");
            };
        }

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
        
        return installResponseHook($http.get(rurl, setupConfig('GET', options)));
    };
    
    this.getx = function(url, options) {
        var rurl = this.apiurl + url;
        return $http.get(rurl, setupConfig('GET', options));
    };

    this.patch = function(url, data, options) {
        var rurl = this.apiurl + url;
        return installResponseHook($http.patch(rurl, data, setupConfig('PATCH', options, data)));
    };

    this.post = function(url, data, options) {
        var rurl = this.apiurl + url;
        return installResponseHook($http.post(rurl, data, setupConfig('POST', options, data)));
    };

    this.put = function(url, data, options) {
        var rurl = this.apiurl + url;
        return installResponseHook($http.put(rurl, data, setupConfig('PUT', options, data)));
    };

    this.delete = function(url, options) {
        var rurl = this.apiurl + url;
        return installResponseHook($http.delete(rurl, setupConfig('DELETE', options)));
    };


    this.get_export_url = function(backupid, passphrase) {
        var rurl = this.apiurl + '/backup/' + backupid + '/export?x-xsrf-token=' + encodeURIComponent($cookies.get('xsrf-token'));
        if ((passphrase || '').trim().length > 0)
            rurl += '&passphrase=' + encodeURIComponent(passphrase);

        return rurl;
    };

    this.get_import_url = function(passphrase) {
        var rurl = this.apiurl + '/backups/import?x-xsrf-token=' + encodeURIComponent($cookies.get('xsrf-token'));
        if ((passphrase || '').trim().length > 0)
            rurl += '&passphrase=' + encodeURIComponent(passphrase);

        return rurl;
    };

    this.get_bugreport_url = function(reportid) {
        return this.apiurl + '/bugreport/' + reportid + '?x-xsrf-token=' + encodeURIComponent($cookies.get('xsrf-token'));
    };
});