backupApp.filter('moment', function (AppUtils) {
    // AppUtils is required as it sets up the locale
    return function (input, momentFn /*, ...params */) {
        var args = Array.prototype.slice.call(arguments, 2);
        momentObj = moment(input);
        return momentObj[momentFn].apply(momentObj, args);
    };
});
