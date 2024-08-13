backupApp.controller('CaptchaController', function($scope, CaptchaService, DialogService, AppService, AppUtils) {
	var entry = $scope.entry = CaptchaService.active;

	function refreshChallenge() {
		entry.imageurl = null;

		AppService.postJson('/captcha', { 'target': entry.target}).then(function(resp) {
    		entry.token = resp.data.Token;
			entry.expectedAnswer = resp.data.Answer;
			entry.noVisualChallenge = resp.data.NoVisualChallenge;
			entry.imageurl = AppService.apiurl + '/captcha/' + entry.token;

    	}, function(err) {
			DialogService.dismissCurrent();
			AppUtils.connectionError(err);
		});
	};

	if (entry.token == null)
		refreshChallenge();

	$scope.reload = refreshChallenge;
});
