backupApp.controller('CaptchaController', function($scope, CaptchaService, DialogService, AppService, AppUtils) {
	var entry = $scope.entry = CaptchaService.active;

	function refreshImage() {
		entry.imageurl = null;

		AppService.post('/captcha', { 'target': entry.target}).then(function(resp) {
    		entry.token = resp.data.token;
			entry.imageurl = AppService.apiurl + '/captcha/' + entry.token;

    	}, function(err) {
			DialogService.dismissCurrent();
			AppUtils.connectionError(err);
		});
	};

	if (entry.token == null)
		refreshImage();

	$scope.reload = refreshImage;
});
