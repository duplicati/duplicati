backupApp.controller('ThrottleController', function($scope, AppService, ServerStatus, SystemInfo, AppUtils) {
    $scope.speedMultipliers = AppUtils.speedMultipliers;

    $scope.selection = $scope.$parent.state.CurrentItem;

    AppService.get('/serversettings').then(function(data) {

		$scope.selection.uploadspeed = data.data['max-upload-speed'];
		$scope.selection.downloadspeed = data.data['max-download-speed'];

		$scope.selection.uploadthrottleenabled = ($scope.selection.uploadspeed != '');
		$scope.selection.downloadthrottleenabled = ($scope.selection.downloadspeed != '');

		// Nicer looking UI
		if (!$scope.selection.uploadthrottleenabled)
			$scope.selection.uploadspeed = "10MB";
		if (!$scope.selection.downloadthrottleenabled)
			$scope.selection.downloadspeed = "10MB";

    }, AppUtils.connectionError);

});
