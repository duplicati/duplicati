backupApp.controller('ConfirmDeleteController', function($scope, $location, gettextCatalog) {
    $scope.selection = $scope.$parent.state.CurrentItem;
    
    if ($scope.selection.requiredword == '')
        $scope.selection.requiredword = 'delete all files';
    $scope.selection.typedword = '';
});
