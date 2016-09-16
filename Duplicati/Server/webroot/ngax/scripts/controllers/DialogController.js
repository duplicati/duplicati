backupApp.controller('DialogController', function($scope, DialogService) {
    $scope.state = DialogService.watch($scope);

    $scope.onButtonClick = function(index) {
        var cur = $scope.state.CurrentItem;        
        DialogService.dismissCurrent();

        if (cur.callback)
            cur.callback(index);
    };
    
});
