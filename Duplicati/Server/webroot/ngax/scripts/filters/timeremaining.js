backupApp.filter('timeremaining', function() {
  return function(value) {
      if (value == null)
          return null;

      if (typeof(value) == typeof(new Date()))
          value = Math.max(0, value - new Date());
      else
          value = parseInt(value);

      value = Math.floor(value / 1000);

      if (value < 0)
          return "now";

    var r = [];

    var s = value % 60;
    value = Math.floor((value - s) / 60);
    r.push(s);

    if (value > 0) {

        var m = value % (60);
        value = Math.floor((value - m) / 60);
        r.push(m);
    }

    if (value > 0) {

        var h = value % (24);
        value = Math.floor((value - h) / 24);
        r.push(h);
    }

    if (value > 0) {
        var d = value;
        r.push(d);
    }

    r = r.reverse();

    for (var i = 0; i < r.length; i++) {
        var v = r[i] + '';
        if ((i != 0 || r.length < 2) && v.length < 2)
            v = '0' + v;
        
        r[i] = v;
    };

    if (r.length == 1)
        r.unshift('0');

    return r.join(':');
  }
});
