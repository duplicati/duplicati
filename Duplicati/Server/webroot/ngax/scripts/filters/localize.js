backupApp.filter('localize', function(Localization) {
  return function() {
  	return Localization.localize.apply(Localization, arguments);
  }
});