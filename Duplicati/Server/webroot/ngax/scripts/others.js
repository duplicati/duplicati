
backupApp.controller('testController', function($scope) {
    $scope.sourcelist = [];
    $scope.excludelist = [];

    $scope.excludearray = '';
    $scope.includearray = '';

    $scope.doUpdate = function() {
        this.sourcelist.length = 0;
        this.sourcelist.push.apply(this.sourcelist, this.includearray.split('\n'));
        this.excludelist.length = 0;
        this.excludelist.push.apply(this.excludelist, this.excludearray.split('\n'));
    };
});
