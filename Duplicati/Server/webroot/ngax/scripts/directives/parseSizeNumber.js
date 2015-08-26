backupApp.directive('parseSizeNumber', function(AppUtils) {
	return {
		restrict: 'A',
		require: ['ngModel'],
		link: function(scope, element, attr, ctrl) {
			var multiplier = null;

			ctrl[0].$parsers.push(function(txt) {
				txt = txt || '0';
				return txt + (multiplier || '');
			});

			ctrl[0].$formatters.push(function(src) {
				var parts = AppUtils.splitSizeString(src);
				if (parts == null) {
					multiplier = null;
					return null;
				}

				multiplier = parts[1];

				return parts[0];
			});
		}
	};
});

backupApp.directive('parseSizeMultiplier', function(AppUtils) {
	return {
		restrict: 'A',
		require: ['ngModel'],
		link: function(scope, element, attr, ctrl) {

			var number = null;

			ctrl[0].$parsers.push(function(txt) {
				return (number || '0') + (txt || '');
			});

			ctrl[0].$formatters.push(function(src) {
				var parts = AppUtils.splitSizeString(src);
				if (parts == null) {
					number = null;
					return null;
				}

				number = parts[0];
				return parts[1];
			});
		}
	};
});