backupApp.controller('RestoreController', function ($scope, $routeParams, $location, AppService, AppUtils, SystemInfo) {

	$scope.SystemInfo = SystemInfo.watch($scope);
    $scope.AppUtils = AppUtils;

	$scope.HideEditUri = function() {
		$scope.EditUriState = false;
	};

});