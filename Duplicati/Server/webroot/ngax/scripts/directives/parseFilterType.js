backupApp.directive('parseFilterType', function(AppUtils, SystemInfo) {
    return {
        restrict: 'A',
        require: ['ngModel'],
        link: function(scope, element, attr, ctrl) {
            ctrl[0].$parsers.push(function(txt) {
                var dirsep = SystemInfo.state.DirectorySeparator || '/';
                // Store type in scope for parseFilterContent directive
                // This is necessary when changing directly the content (for example
                // adding a '/' at the body end and then changing the filter type)
                // This works because ng-repeat creates a child scope for each iteration
                // It is still glitchy in some edge cases, but should be more consistent
                scope.filterType = txt;
                return AppUtils.buildFilter(txt, scope.filterBody, dirsep);
            });

            ctrl[0].$formatters.push(function(src) {
                var dirsep = SystemInfo.state.DirectorySeparator || '/';
                var parts = AppUtils.splitFilterIntoTypeAndBody(src, dirsep);
                if (parts == null) {
                    body = null;
                    return null;
                }

                scope.filterType = parts[0];
                return parts[0];
            });
        }
    };
});

backupApp.directive('parseFilterContent', function(AppUtils, SystemInfo) {
    return {
        restrict: 'A',
        require: ['ngModel'],
        link: function(scope, element, attr, ctrl) {
            ctrl[0].$parsers.push(function(txt) {
                var dirsep = SystemInfo.state.DirectorySeparator || '/';
                // Store content in scope for parseFilterType directive
                scope.filterBody = txt;
                return AppUtils.buildFilter(scope.filterType, txt, dirsep);
            });

            ctrl[0].$formatters.push(function(src) {
                var dirsep = SystemInfo.state.DirectorySeparator || '/';
                // Remove double trailing slashes
                var parts = AppUtils.splitFilterIntoTypeAndBody(src, dirsep);
                if (parts == null) {
                    scope.filterType = null;
                    return null;
                }

                scope.filterBody = parts[1];
                return parts[1];
            });
        }
    };
});
