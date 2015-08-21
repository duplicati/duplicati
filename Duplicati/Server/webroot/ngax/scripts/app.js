var backupApp = angular.module(
    'backupApp', 
    [
        'ngRoute', 
        'btford.modal', 
        'dotjem.angular.tree'
    ]
);

backupApp.config(['$routeProvider',
	function($routeProvider) {
		$routeProvider.
			when('/home', {
				templateUrl: 'templates/home.html'
			}).
			when('/add', {
				templateUrl: 'templates/addoredit.html'
			}).
			when('/edit/:backupid', {
				templateUrl: 'templates/addoredit.html'
			}).
			when('/restore', {
				templateUrl: 'templates/restore.html'
			}).
			when('/settings', {
				templateUrl: 'templates/settings.html'
			}).
            when('/test', {
                templateUrl: 'templates/test.html',
                controller: 'testController'
            }).
            when('/api', {
                templateUrl: 'templates/api.html'
            }).
			otherwise({
				templateUrl: 'templates/home.html'
				//redirectTo: '/home'
		});
}]);
