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

backupApp.filter('parseDate', function(parsetimestampFilter, momentFilter) {
  return function(value, force) {
    if (force) {
      return parsetimestampFilter(value);    
    }
    
    return momentFilter(value, 'calendar');
  }
});
