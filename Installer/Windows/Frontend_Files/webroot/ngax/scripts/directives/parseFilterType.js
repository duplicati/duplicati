backupApp.directive('parseFilterType', function(AppUtils, SystemInfo) {
    return {
        restrict: 'A',
        require: ['ngModel'],
        link: function(scope, element, attr, ctrl) {
            var body = null;

            ctrl[0].$parsers.push(function(txt) {
                var dirsep = SystemInfo.state.DirectorySeparator || '/';
                return AppUtils.buildFilter(txt, body, dirsep);
            });

            ctrl[0].$formatters.push(function(src) {
                var dirsep = SystemInfo.state.DirectorySeparator || '/';
                var parts = AppUtils.splitFilterIntoTypeAndBody(src, dirsep);
                if (parts == null) {
                    body = null;
                    return null;
                }

                body = parts[1];

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

            var type = null;

            ctrl[0].$parsers.push(function(txt) {
                var dirsep = SystemInfo.state.DirectorySeparator || '/';
                return AppUtils.buildFilter(type, txt, dirsep);
            });

            ctrl[0].$formatters.push(function(src) {
                var dirsep = SystemInfo.state.DirectorySeparator || '/';
                var parts = AppUtils.splitFilterIntoTypeAndBody(src, dirsep);
                if (parts == null) {
                    type = null;
                    return null;
                }

                type = parts[0];
                return parts[1];
            });
        }
    };
});
