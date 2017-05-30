backupApp.directive('parseSizeNumber', function(AppUtils) {
    return {
        restrict: 'A',
        require: ['ngModel'],
        scope: {
            parseSizeNumber: '@'
          },        
        link: function(scope, element, attr, ctrl) {
            var multiplier = null;

            ctrl[0].$parsers.push(function(txt) {
                return (txt || '0') + (multiplier || '');
            });

            ctrl[0].$formatters.push(function(src) {
                var parts = AppUtils.splitSizeString(src);
                if (parts == null) {
                    multiplier = null;
                    return null;
                }

                if (scope.parseSizeNumber == 'uppercase')
                    multiplier = parts[1].toUpperCase();
                else if (scope.parseSizeNumber == 'lowercase')
                    multiplier = parts[1].toLowerCase();
                else
                    multiplier = parts[1];

                return parts[0];
            });
        }
    };
});

backupApp.directive('parseSizeMultiplier', function(AppUtils) {
    return {
        restrict: 'A',
        scope: {
            parseSizeMultiplier: '@'
          },        
        require: ['ngModel'],
        link: function(scope, element, attr, ctrl) {

            var number = null;

            ctrl[0].$parsers.push(function(txt) {
                return (number || '0') + (txt || '');
            });

            ctrl[0].$formatters.push(function(src) {
                var parts = AppUtils.splitSizeString(src);
                if (parts == null) {
                    number = null;
                    return null;
                }

                number = parts[0];
                if (scope.parseSizeMultiplier == 'uppercase')
                    return parts[1].toUpperCase();
                else if (scope.parseSizeMultiplier == 'lowercase')
                    return parts[1].toLowerCase();
                else
                    return parts[1];
            });
        }
    };
});
