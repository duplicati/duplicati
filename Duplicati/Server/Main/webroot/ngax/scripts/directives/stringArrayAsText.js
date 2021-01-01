backupApp.directive('stringArrayAsText', function(AppUtils) {
    return {
        restrict: 'A',
        require: ['ngModel'],
        link: function(scope, element, attr, ctrl) {

            function renderItem(src) {
                return (src || []).join('\n');
            }

            scope.$watch(attr['ngModel'], function(newval, oldval, scope) { 
                ctrl[0].$viewValue = renderItem(newval);
                ctrl[0].$render(); 
            }, true);

            ctrl[0].$parsers.push(function(txt) {
                return AppUtils.removeEmptyEntries(AppUtils.replace_all(txt, '\r', '\n').split('\n'));
            });

            ctrl[0].$formatters.push(function(src) {
                return renderItem(src);
            });
        }
    };
});
