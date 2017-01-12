angular.module('backupApp').run(function($rootScope){
    $rootScope.parseInt = function(str) {
        return parseInt(str);
    };  
});
