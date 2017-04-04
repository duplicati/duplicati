var backupApp = angular.module(
    'backupApp', 
    [
        'ngRoute', 
        'dotjem.angular.tree',
        'ngCookies',
        'ngSanitize',
        'gettext',
        'ngclipboard'
    ]
);

backupApp.constant('appConfig', {
    login_url: '/login.html'
});

backupApp.config(['$routeProvider',
    function($routeProvider) {
        $routeProvider.
            when('/home', {
                templateUrl: 'templates/home.html'
            }).
            when('/add', {
                templateUrl: 'templates/addoredit.html'
            }).
            when('/add-import', {
                templateUrl: 'templates/addoredit.html'
            }).
            when('/restorestart', {
                templateUrl: 'templates/restorewizard.html'
            }).
            when('/addstart', {
                templateUrl: 'templates/addwizard.html'
            }).
            when('/edit/:backupid', {
                templateUrl: 'templates/addoredit.html'
            }).
            when('/restoredirect', {
                templateUrl: 'templates/restoredirect.html'
            }).
            when('/restoredirect-import', {
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
            when('/delete/:backupid', {
                templateUrl: 'templates/delete.html'
            }).
            when('/log/:backupid', {
                templateUrl: 'templates/log.html'
            }).
            when('/log', {
                templateUrl: 'templates/log.html'
            }).
            when('/updatechangelog', {
                templateUrl: 'templates/updatechangelog.html'
            }).
            when('/export/:backupid', {
                templateUrl: 'templates/export.html'
            }).
            when('/import', {
                templateUrl: 'templates/import.html'
            }).
            when('/restore-import', {
                templateUrl: 'templates/import.html'
            }).
            when('/localdb/:backupid', {
                templateUrl: 'templates/localdatabase.html'
            }).
            when('/commandline', {
                templateUrl: 'templates/commandline.html'
            }).
            when('/commandline/:backupid', {
                templateUrl: 'templates/commandline.html'
            }).
            when('/commandline/view/:viewid', {
                templateUrl: 'templates/commandline.html'
            }).
            otherwise({
                templateUrl: 'templates/home.html'
                //redirectTo: '/home'
        });
}]);

backupApp.run(function($injector) {
    try {
        $injector.get('OEMService');
    } catch(e) {}
    try {
        $injector.get('CustomService');
    } catch(e) {}
    try {
        $injector.get('ProxyService');
    } catch(e) {}
});

// Registers a global parseInt function
angular.module('backupApp').run(function($rootScope){
    $rootScope.parseInt = function(str) {
        return parseInt(str);
    };  
});

// Register a global back function
/*backupApp.run(function ($rootScope, $location) {

    var history = [];
    $rootScope.$on('$routeChangeSuccess', function() {
        history.push($location.$$path);
    });

    $rootScope.back = function () {
        var prevUrl = history.length > 1 ? history.splice(-2)[0] : "/home";
        $location.path(prevUrl);
    };

});*/
