backupApp.controller('AppController', function($scope, $cookies, $location, AppService, BrandingService, ServerStatus, SystemInfo, AppUtils, DialogService, gettextCatalog) {
    $scope.brandingService = BrandingService.watch($scope);
    $scope.state = ServerStatus.watch($scope);
    $scope.systemInfo = SystemInfo.watch($scope);

    $scope.localized = {};
    $scope.location = $location;

    $scope.doReconnect = function() {
        ServerStatus.reconnect();
    };

    $scope.resume = function() {
        ServerStatus.resume().then(function() {}, AppUtils.connectionError);
    };

    $scope.pause = function(duration) {
        ServerStatus.pause(duration).then(function() {}, AppUtils.connectionError);
    };

    $scope.isLoggedIn = $cookies.get('session-auth') != null && $cookies.get('session-auth') != '';

    $scope.log_out = function() {
        AppService.log_out().then(function() {
            $cookies.remove('session-auth', { path: '/' });
            location.reload(true);            
        }, AppUtils.connectionError);
    };

    $scope.pauseOptions = function() {
        if ($scope.state.programState != 'Running') {
            $scope.resume();
        } else {
            DialogService.htmlDialog(
                gettextCatalog.getString('Pause options'), 
                'templates/pause.html', 
                [gettextCatalog.getString('OK'), gettextCatalog.getString('Cancel')], 
                function(index, text, cur) {
                    if (index == 0 && cur != null && cur.time != null) {
                        var time = cur.time;
                        $scope.pause(time == 'infinite' ? '' : time);
                    }
                }
            );
        }
    };

    $scope.throttleOptions = function() {
        alert('Throttle options are not implemented yet');
    };

    function updateCurrentPage() {
        if ($location.$$path == '/' || $location.$$path == '')
            $scope.current_page = 'home';
        else if ($location.$$path == '/addstart' || $location.$$path == '/add' || $location.$$path == '/import')
            $scope.current_page = 'add';
        else if ($location.$$path == '/restorestart' || $location.$$path == '/restore' || $location.$$path == '/restoredirect' || $location.$$path.indexOf('/restore/') == 0)
            $scope.current_page = 'restore';
        else if ($location.$$path == '/settings')
            $scope.current_page = 'settings';
        else if ($location.$$path == '/log')
            $scope.current_page = 'log';
        else if ($location.$$path == '/about')
            $scope.current_page = 'about';
        else
            $scope.current_page = '';
    };

    $scope.$on('serverstatechanged', function() {
        // Unwanted jQuery interference, but the menu is built with this
        if (ServerStatus.state.programState == 'Paused') {
            $('#contextmenu_pause').removeClass('open');
            $('#contextmenulink_pause').removeClass('open');            
        }
    });

    //$scope.$on('$routeUpdate', updateCurrentPage);
    $scope.$watch('location.$$path', updateCurrentPage);
    updateCurrentPage();

});
