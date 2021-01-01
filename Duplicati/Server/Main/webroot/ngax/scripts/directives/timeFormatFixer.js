// From: https://mark.zealey.org/2015/01/08/formatting-time-inputs-nicely-with-angularjs
backupApp.directive('ngModel', function( $filter ) {
    return {
        require: '?ngModel',
        link: function(scope, elem, attr, ngModel) {
            if( !ngModel )
                return;
            if( attr.type !== 'time' )
                return;
                    
            ngModel.$formatters.unshift(function(value) {
                return value.replace(/:00\.000$/, '')
            });
        }
    }   
});         