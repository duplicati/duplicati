backupApp.controller('CommandlineController', function($scope, $routeParams, SystemInfo, AppUtils, AppService, DialogService, gettextCatalog) {
    $scope.SystemInfo = SystemInfo.watch($scope);

    $scope.ExtendedOptions = [];
    $scope.Arguments = [];
    $scope.Running = false;

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
                $scope.outputlines = [resp.data];
            },
            function(resp) {
                $scope.outputlines = [resp.data];
            }
        );
    };

    AppService.get('/commandline').then(function(resp) {
        var cmds = [];
        var helps = {};
        for (var i = resp.data.length - 1; i >= 0; i--) {
            cmds.push(resp.data[i].Key);
            helps[resp.data[i].Key] = resp.data[i].Description;
        }

        $scope.SupportedCommands = cmds;
        $scope.CommandHelp = helps;
        $scope.Command = 'Find';

    }, function() {
        AppUtils.connectionError.apply(AppUtils, arguments);
        $location.path('/');
    });

    if ($routeParams.backupid != null) {
        AppService.get('/backup/' + $routeParams.backupid + '/export?argsonly=true').then(
            function(resp) {
                $scope.TargetURL = resp.data.Backend;
                $scope.Arguments = resp.data.Arguments;
                $scope.ExtendedOptions = resp.data.Options;
            }, 
            function(resp) {
                $scope.Connecting = false;
                var message = resp.statusText;
                if (resp.data != null && resp.data.Message != null)
                    message = resp.data.Message;

                DialogService.dialog(gettextCatalog.getString('Error'), gettextCatalog.getString('Failed to connect: {{message}}', { message: message }));
            }
        );
    }
});