backupApp.controller('AboutController', function($scope, $location, BrandingService, ServerStatus, AppService, SystemInfo, AppUtils) {
    $scope.brandingService = BrandingService.watch($scope);
    $scope.Page = 'general';
    $scope.sysinfo = SystemInfo.watch($scope);
    $scope.state = ServerStatus.watch($scope);

    // Common licenses
    var licenses = {
        'MIT': 'http://www.linfo.org/mitlicense.html',
        'Apache': 'https://www.apache.org/licenses/LICENSE-2.0.html',
        'Apache 2': 'https://www.apache.org/licenses/LICENSE-2.0.html',
        'Apache 2.0': 'https://www.apache.org/licenses/LICENSE-2.0.html',
        'Public Domain': 'https://creativecommons.org/licenses/publicdomain/',
        'GPL': 'https://www.gnu.org/copyleft/gpl.html',
        'LGPL': 'https://www.gnu.org/copyleft/lgpl.html',
        'MS-PL': 'http://opensource.org/licenses/MS-PL',
        'Microsoft Public': 'http://opensource.org/licenses/MS-PL',
        'New BSD': 'http://opensource.org/licenses/BSD-3-Clause'
    };

    AppService.get('/acknowledgements').then(function(resp) {
		$scope.Acknowledgements = resp.data.Acknowledgements;
    });

    $scope.$watch('Page', function() {
    	if ($scope.Page == 'changelog' && $scope.ChangeLog == null) {
    		AppService.get('/changelog?from-update=false').then(function(resp) {
    			$scope.ChangeLog = 	resp.data.Changelog;
    		});
    	} else if ($scope.Page == 'licenses' && $scope.Licenses == null) {
    		AppService.get('/licenses').then(function(resp) {
    			var res = [];
    			for(var n in resp.data) {
    				var r = JSON.parse(resp.data[n].Jsondata);
                    if (r != null) {
                        r.licenselink = r.licenselink || licenses[r.license] || '#';
                        res.push(r);
                    }    				
    			}
				$scope.Licenses = res;
    		});
    	}
    });

    $scope.doShowUpdateChangelog = function() {
        $location.path('/updatechangelog');
    };

    $scope.doStartUpdateDownload = function() {
        AppService.post('/updates/install');
    };

    $scope.doStartUpdateActivate = function() {
        AppService.post('/updates/activate').then(function() {}, AppUtils.connectionError('Activate failed: '));
    };

    $scope.doCheckForUpdates = function() {
        AppService.post('/updates/check');

    };

});