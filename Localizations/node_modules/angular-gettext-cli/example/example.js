angular.module("myApp").controller("helloController", function (i18n) {
    var myString = i18n("Hello");
    var myOtherString = __("hi");
});
