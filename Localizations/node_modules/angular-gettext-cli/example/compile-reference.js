angular.module('gettext-test').run(['gettextCatalog', function (gettextCatalog) {
/* jshint -W100 */
    gettextCatalog.setStrings('de', {"Hello":"Hallo","Hello {{name}}":"Hallo {{name}}"});
/* jshint +W100 */
}]);