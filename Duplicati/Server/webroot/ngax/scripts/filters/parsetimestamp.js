backupApp.filter('parsetimestamp', function(AppUtils) {
  return function(value) {
      if (value == null)
          return null;

      if (typeof(value) != typeof(new Date()))
    {
          if (typeof(value) == typeof(''))
            value =  AppUtils.parseDate(value);
          else
              value = new Date(parseInt(value) * 1000);
    }

      return AppUtils.toDisplayDateAndTime(value);
  }
});
