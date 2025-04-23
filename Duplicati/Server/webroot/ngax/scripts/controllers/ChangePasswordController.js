backupApp.controller('ChangePasswordController', function($scope, gettextCatalog) {

    $scope.selection = $scope.$parent.state.CurrentItem;    
    $scope.selection.remotePassword = '';
    $scope.selection.confirmPassword = '';
});
