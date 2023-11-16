backupApp.controller('ImportController', function($rootScope, $scope, $timeout, $location, AppService, DialogService) {
    $scope.Connecting = false;
    $scope.Completed = false;
    $scope.ImportURL = AppService.get_import_url();

    $scope.restoremode = $location.$$path.indexOf('/restore-import') == 0;

    // Ugly, but we need to communicate across the iframe load
    $scope.CallbackMethod = 'callback-' + Math.random();
    window[$scope.CallbackMethod] = function(message, jobdefinition) {
        // The delay fixes an issue with Ghostery
        // failing somewhere
        $timeout(function() { 

            $scope.Connecting = false;
            $scope.Completed = true;
            
            if (message == 'OK')
                $location.path('/');
            else if (jobdefinition != null && typeof(jobdefinition) != typeof(''))
            {
                // Use the root scope to pass the imported job
                $rootScope.importConfig = jobdefinition;

                if ($scope.restoremode)
                    $location.path('/restoredirect-import'); 
                else
                    $location.path('/add-import'); 
            }
            else
                DialogService.dialog('Error', message);

        }, 100);        
    };

    $scope.doSubmit = function() {
        // TODO: Ugly non-angular way
        $scope.Connecting = true;
        document.getElementById('import-form').submit();
    };

});
