backupApp.controller('ImportController', function($scope, $routeParams, $location, AppService) {
    $scope.Connecting = false;
    $scope.Completed = false;
    $scope.ImportURL = AppService.get_import_url();

    // Ugly, but we need to communicate across the iframe load
    $scope.CallbackMethod = 'callback-' + Math.random();
    window[$scope.CallbackMethod] = function(message) {
    	$scope.Connecting = false;
        $scope.Completed = true;
        if (message == 'OK')
            $location.path('/');
        else
            alert(message);
    };

    $scope.doSubmit = function() {
    	// TODO: Ugly non-angular way
    	document.getElementById('import-form').submit();
    };

});