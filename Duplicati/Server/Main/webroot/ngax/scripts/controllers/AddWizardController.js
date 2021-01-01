backupApp.controller('AddWizardController', function($scope, $location, gettextCatalog) {

    $scope.selection = {
        style: 'blank'
    };

    $scope.nextPage = function() {
        if ($scope.selection.style == 'blank')
            $location.path('/add');
        else
            $location.path('/import');
    };
});
