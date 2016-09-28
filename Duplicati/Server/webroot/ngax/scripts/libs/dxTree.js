(function () {
    var comp = angular.module('dotjem.angular.tree', []),
        SW_REGEX = /^(\S+)(\s+as\s+(\w+))?$/;

    comp.controller("dxStartWithCtrl", [function () {}]);

    function $RootNodeDirective() {
        return {
            restrict: 'AEC',
            require: 'dxStartWith',
            controller: 'dxStartWithCtrl',

            scope: true,
            terminal: true,
            transclude: true,
            multiElement: true,

            $$tlb: true,

            compile: function (elm, attr) {
                var exp = attr['dxStartWith'] || attr.root,
                    match = exp.match(SW_REGEX),
                    watch = match[1],
                    priorAlias = match[3] || '';

                return function $RootNodeDirectiveLink(scope, elm, attr, ctrl, $transclude) {
                    ctrl.transclude = $transclude;
                    ctrl.transclude(scope, function (clone, scope) {
                        elm.append(clone);
                        scope.$dxLevel = 0;
                        scope.$dxIsRoot = true;
                        function updatePrior(value) {
                            scope.$dxPrior = value;
                            if (priorAlias !== '') {
                                scope[priorAlias] = value;
                            }
                        }
                        scope.$watch(watch, updatePrior);
                    });
                };
            }
        };
    }

    function $NodeDirective() {
        return {
            restrict: 'AEC',
            require: '^dxStartWith',

            scope: true,
            terminal: true,
            multiElement: true,

            compile: function (elm, attr) {
                var exp = attr['dxConnect'] || attr.connect,
                    match = exp.match(SW_REGEX),
                    watch = match[1],
                    priorAlias = match[3] || '';

                return function $NodeDirectiveLink(scope, elm, attr, ctrl) {
                    ctrl.transclude(scope, function (clone, scope) {
                        elm.append(clone);

                        scope.$dxLevel = scope.$dxLevel + 1;
                        scope.$dxIsRoot = false;
                        function updatePrior(value) {
                            scope.$dxPrior = value;
                            if (priorAlias !== '') {
                                scope[priorAlias] = value;
                            }
                        }

                        scope.$watch(watch, updatePrior);
                    });
                };
            }
        };
    }

    comp.directive('dxStartWith', $RootNodeDirective);
    comp.directive('dxConnect', $NodeDirective);
}());
