var backupApp = angular.module(
    'backupApp', 
    [
        'ngRoute', 
        'dotjem.angular.tree',
        'ngSanitize'
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
			when('/restoredirect', {
				templateUrl: 'templates/restoredirect.html'
			}).
			when('/restore/:backupid', {
				templateUrl: 'templates/restore.html'
			}).
			when('/settings', {
				templateUrl: 'templates/settings.html'
			}).
			when('/about', {
				templateUrl: 'templates/about.html'
			}).
			when('/log/:backupid', {
				templateUrl: 'templates/log.html'
			}).
			when('/log', {
				templateUrl: 'templates/log.html'
			}).
            when('/test', {
                templateUrl: 'templates/test.html',
                controller: 'testController'
            }).
            when('/api', {
                templateUrl: 'templates/api.html'
            }).
			when('/pause', {
				templateUrl: 'templates/pause.html'
			}).
			otherwise({
				templateUrl: 'templates/home.html'
				//redirectTo: '/home'
		});
}]);
