backupApp.directive('advancedOptionsEditor', function() {
    return {
        restrict: 'E',
        scope: {
            ngModel: '=',
            ngOptionList: '='
        },
        templateUrl: 'templates/advancedoptionseditor.html',
        controller: function($scope, $timeout) {
            $scope.NewItem = null;

            var optionmap = null;

            function rebuildOptionMap() {
                optionmap = {};

                for(var n in $scope.ngOptionList)
                    optionmap[$scope.ngOptionList[n].Name.toLowerCase()] = $scope.ngOptionList[n];
            }

            function coreName(name) {
                if (name == null)
                    return '';
                if (typeof(name) != typeof(''))
                    name = name.Name;

                name = name || '';
                if (name.indexOf('--') == 0)
                    name = name.substr(2);
                if (name.indexOf('=') >= 0)
                    name = name.substr(0, name.indexOf('='));
                return name;
            }

            $scope.getEntry = function(key) {
                if (optionmap == null)
                    return null;

                return optionmap[coreName(key)];
            };

            $scope.getDisplayName = function(name) {
                var item = $scope.getEntry(name);
                if (item == null)
                    return coreName(name);

                return item.Name + ': ' + item.ShortDescription;
            };

            $scope.getInputType = function(item) {
                var item = $scope.getEntry(item);
                if (item == null)
                    return 'text';

                if (item.Type == 'Enumeration')
                    return 'enum';
                if (item.Type == 'Flags')
                    return 'flags';
                else if (item.Type == 'Boolean')
                    return 'bool';
                else if (item.Type == 'Password')
                    return 'password';
                else if (item.Type == 'Integer')
                    return 'int';
                else if (item.Type == 'Size')
                    return 'size';
                else if (item.Type == 'Timespan')
                    return 'timespan';
                else
                    return 'text';
            };

            $scope.getShortName = function(name) {
                var item = $scope.getEntry(name);
                if (item == null)
                    return coreName(name);

                return item.Name;
            };

            $scope.getShortDescription = function(item) {
                var item = $scope.getEntry(item);
                if (item == null)
                    return null;

                return item.ShortDescription;
            };

            $scope.getLongDescription = function(item) {
                var item = $scope.getEntry(item);
                if (item == null)
                    return null;

                return item.LongDescription;
            };

            $scope.getEnumerations = function(item) {
                var item = $scope.getEntry(item);
                if (item == null)
                    return null;

                return item.ValidValues;
            };

            $scope.deleteItem = function(item) {
                for (var i = $scope.ngModel.length - 1; i >= 0; i--) {
                    if ($scope.ngModel[i] == item) {
                        $scope.ngModel.splice(i, 1);
                        return;
                    }
                };
            };

            $scope.$watch('ngOptionList', rebuildOptionMap);
            $scope.$watch('NewItem', function() {
                if ($scope.NewItem != null) {
                    var opt = '--' + $scope.NewItem.Name;
                    var itm = $scope.getEntry(opt);
                    if (itm != null && itm.DefaultValue != null)
                        opt += '=' + itm.DefaultValue;

                    $scope.ngModel.push(opt);
                    $scope.NewItem = null;
                }
            });

        }
    };
});