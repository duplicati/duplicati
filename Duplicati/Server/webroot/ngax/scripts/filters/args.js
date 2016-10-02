backupApp.filter('args', function(Localization) {
    return function() {
        return Localization.format.apply(Localization, arguments);
    }
});
