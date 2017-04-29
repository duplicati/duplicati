backupApp.controller('PauseController', function($scope, $location, gettextCatalog) {
     $scope.selection = $scope.$parent.state.CurrentItem;

     $scope.selection.time = 'infinite';
});
