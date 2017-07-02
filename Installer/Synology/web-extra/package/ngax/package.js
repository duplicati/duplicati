backupApp.service('ProxyService', function(AppService, $http) {
    var origconfig = AppService.proxy_config;
    var synotoken = null;
    var is_grabbing = false;

    try
    {
        if (synotoken == null)
            synotoken = SYNO.SDS.Session.SynoToken;
    }
    catch (e) {}

    try
    {
        if (synotoken == null)
            synotoken = window.parent.SYNO.SDS.Session.SynoToken;
    }
    catch (e) {}

    function grab_syno_token() {
        if (synotoken != null || is_grabbing)
            return;

        is_grabbing = true;
        $http.get('/webman/login.cgi?_dc=' + Math.random()).then(
            function(data) {
                is_grabbing = false;
                synotoken = data.data.SynoToken;
            },

            function() {
                is_grabbing = false;
            }
        );
    };

    AppService.proxy_config = function(method, options, data, targeturl) {
        grab_syno_token();

        if (synotoken != null)
            options.headers['X-Syno-Token'] = synotoken;

        if (origconfig != null)
            origconfig(method, options, data, targeturl);
    };
    
    grab_syno_token();

});