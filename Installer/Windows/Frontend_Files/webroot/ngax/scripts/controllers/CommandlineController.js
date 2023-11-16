backupApp.controller('CommandlineController', function($scope, $routeParams, $location, $timeout, SystemInfo, ServerStatus, AppUtils, AppService, DialogService, gettextCatalog) {
    $scope.SystemInfo = SystemInfo.watch($scope);
    $scope.ServerStatus = ServerStatus;
    $scope.serverstate = ServerStatus.watch($scope);

    $scope.ExtendedOptions = [];
    $scope.Arguments = [];
    $scope.Running = false;
    $scope.Mode = 'submit';
    $scope.ViewLines = [];
    $scope.Started = null;
    $scope.Finished = false;
    $scope.RawFinished = false;

    var viewid = null;
    var view_offset = 0;
    var view_refreshing = false;
    var view_timer = null;
    var scope = $scope;

    function reloadOptionsList()
    {
        var opts = AppUtils.parseOptionStrings($scope.ExtendedOptions || []);

        var encmodule = opts['encryption-module'] || opts['--encryption-module'] || '';
        var compmodule = opts['compression-module'] || opts['--compression-module'] || 'zip';
        var backmodule = $scope.TargetURL || '';
        var ix = backmodule.indexOf(':');
        if (ix > 0)
            backmodule = backmodule.substr(0, ix);

        $scope.ExtendedOptionList = AppUtils.buildOptionList($scope.SystemInfo, encmodule, compmodule, backmodule);
    };

    $scope.$watchCollection("ExtendedOptions", reloadOptionsList);
    $scope.$watch("TargetURL", reloadOptionsList);
    $scope.$on('systeminfochanged', reloadOptionsList);

    reloadOptionsList();

    var scope = $scope;

    $scope.HideEditUri = function() {
        scope.EditUriState = false;
    };

    $scope.run = function() {

        var opts = AppUtils.parseOptionStrings($scope.ExtendedOptions || []);
        var combined = angular.copy($scope.Arguments || []);

        if (($scope.TargetURL || '').trim().length != 0)
            combined.unshift($scope.TargetURL);

        combined.unshift($scope.Command);

        for(n in opts) {
            if (opts[n] == null)
                combined.push(n);
            else
                combined.push(n + '=' + opts[n]);
        }

        options = {
            headers: {'Content-Type': 'application/json; charset=utf-8'},
            responseType: 'text'
        };

        AppService.post('/commandline', combined, options).then(
            function(resp) {
                $location.path('/commandline/view/' + resp.data.ID);
            },
            function(resp) {
                DialogService.dialog(gettextCatalog.getString('Error'), gettextCatalog.getString('Failed to connect: {{message}}', { message: message }));
            }
        );
    };

    $scope.Abort = function()
    {
        AppService.post('/commandline/' + viewid + '/abort' ).then(function(resp) {

        }, function() {
            AppUtils.connectionError.apply(AppUtils, arguments);
        });

    };

    function FetchOutputLines()
    {
        if (view_refreshing)
            return;

        view_refreshing = true;
        if (view_timer != null) {
            $timeout.cancel(view_timer);
            view_timer = null;
        }

        AppService.get('/commandline/' + viewid + '?pagesize=100&offset=' + view_offset).then(function(resp) {
            $scope.ViewLines.push.apply($scope.ViewLines, resp.data.Items);
            view_offset = view_offset + resp.data.Items.length;

            while ($scope.ViewLines.length > 2000)
                $scope.ViewLines.shift();

            $scope.Started = resp.data.Started;
            $scope.Finished = resp.data.Finished && view_offset == resp.data.Count;
            $scope.RawFinished = resp.data.Finished;

            view_refreshing = false;
            var wait_time = 2000;

            // Fetch more as we are not empty
            if (resp.data.Items.length != 0)
                wait_time = 100;
            // All done, slowly keep the data alive
            else if (resp.data.Finished)
                wait_time = 10000;

            view_timer = $timeout(FetchOutputLines, wait_time);

        }, function(resp) {
            $scope.Started = true;
            if (resp.status == 404) {
                $scope.ViewLines.push('Connection lost, data has expired ...');
                $scope.Finished = true;
            } else {
                $scope.ViewLines.push('Connection error, retry in 2 sec ...');
                view_refreshing = false;
                view_timer = $timeout(FetchOutputLines, 2000);
            }
        });
    };

    AppService.get('/commandline').then(function(resp) {
        var cmds = [];
        for (var i = resp.data.length - 1; i >= 0; i--)
            cmds.push(resp.data[i]);

        $scope.SupportedCommands = cmds.sort();
        $scope.Command = 'help';

    }, function() {
        AppUtils.connectionError.apply(AppUtils, arguments);
        $location.path('/');
    });

    if ($routeParams.viewid != null) {
        $scope.Mode = 'view';
        viewid = $routeParams.viewid;
        view_offset = 0;

        FetchOutputLines();
    }

    if ($routeParams.backupid != null) {
        AppService.get('/backup/' + $routeParams.backupid + '/export?argsonly=true&export-passwords=true').then(
            function(resp) {
                $scope.TargetURL = resp.data.Backend;
                $scope.Arguments = resp.data.Arguments;
                $scope.ExtendedOptions = resp.data.Options;
                $scope.Command = 'backup';
            },
            function(resp) {
                var message = resp.statusText;
                if (resp.data != null && resp.data.Message != null)
                    message = resp.data.Message;

                DialogService.dialog(gettextCatalog.getString('Error'), gettextCatalog.getString('Failed to connect: {{message}}', { message: message }));
            }
        );
    }
});
