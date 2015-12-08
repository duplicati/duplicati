backupApp.controller('EditBackupController', function ($scope, $routeParams, $location, $timeout, AppService, AppUtils, SystemInfo, DialogService) {

	$scope.SystemInfo = SystemInfo.watch($scope);
    $scope.AppUtils = AppUtils;

	$scope.RepeatPasshrase = null;
	$scope.PasswordStrength = 'unknown';
	$scope.CurrentStep = 0;
	$scope.EditUriState = false;
    $scope.showhiddenfolders = false;
    $scope.EditSourceAdvanced = false;
    $scope.EditFilterAdvanced = false;

    $scope.ExcludeAttributes = [];
    $scope.ExcludeLargeFiles = false;

    $scope.fileAttributes = [
    	{name: 'Hidden files', value: 'hidden'}, 
    	{name: 'System files', value: 'system'}, 
    	{name: 'Temporary files', value: 'temporary'}
	];

	var scope = $scope;

	function computePassPhraseStrength() {

        var strengthMap = {
        	'': "Empty",
        	'x': "Passwords do not match",
            0: "Useless",
            1: "Very weak",
            2: "Weak",
            3: "Strong",
            4: "Very strong"
        };

        var passphrase = scope.Options == null ? '' : scope.Options['passphrase'];

		if (scope.RepeatPasshrase != passphrase) 
			scope.PassphraseScore = 'x';
		else if ((passphrase || '') == '')
			scope.PassphraseScore = '';
		else
			scope.PassphraseScore = (zxcvbn(passphrase) || {score: -1}).score;

		scope.PassphraseScoreString = strengthMap[scope.PassphraseScore] || 'Unknown';	
	}

	$scope.$watch('Options["passphrase"]', computePassPhraseStrength);
	$scope.$watch('RepeatPasshrase', computePassPhraseStrength);

	$scope.generatePassphrase = function() {
		this.Options["passphrase"] = this.RepeatPasshrase = AppUtils.generatePassphrase();
		this.ShowPassphrase = true;
	};

	$scope.togglePassphraseVisibility = function() {
		this.ShowPassphrase = !this.ShowPassphrase;;
	};

	$scope.nextPage = function() {
		$scope.CurrentStep = Math.min(3, $scope.CurrentStep + 1);
	};

	$scope.prevPage = function() {
		$scope.CurrentStep = Math.max(0, $scope.CurrentStep - 1);

	};

	$scope.HideEditUri = function() {
		scope.EditUriState = false;
	};

	var oldSchedule = null;

	$scope.toggleSchedule = function() {
		if (scope.Schedule == null) {
			if (oldSchedule == null) {
				oldSchedule = {
					Tags: [],
					Repeat: '1D',
					AllowedDays: []
				};
			}

			scope.Schedule = oldSchedule;
			oldSchedule = null;
		} else {
			oldSchedule = scope.Schedule;
			scope.Schedule = null;
		}
	};

    $scope.addManualSourcePath = function() {
        if (scope.validatingSourcePath)
            return;

        if (scope.manualSourcePath == null || scope.manualSourcePath == '')
        	return;

        var dirsep = scope.SystemInfo.DirectorySeparator || '/';

        if (dirsep == '/') {
        	if (scope.manualSourcePath.substr(0, 1) != '/' && scope.manualSourcePath.substr(0, 1) != '%') {
        		DialogService.dialog('Relative paths not allowed', "The path must be an absolute path, i.e. it must start with a forward slash '/' ");
        		return;
        	}
        }

        function continuation() {
	        scope.validatingSourcePath = true;

	        AppService.post('/filesystem/validate', {path: scope.manualSourcePath}).then(function() {
	            scope.validatingSourcePath = false;
	            scope.Backup.Sources.push(scope.manualSourcePath);
	            scope.manualSourcePath = null;
	        }, function() {
	            scope.validatingSourcePath = false;

	            DialogService.dialog('Path not found', 'The path does not appear to exist, do you want to add it anyway?', ['No', 'Yes'], function(ix) {
	            	if (ix == 1) {
		                scope.Backup.Sources.push(scope.manualSourcePath);
		                scope.manualSourcePath = null;
	            	}
	            });
	        })        	
        };

        if (scope.manualSourcePath.substr(scope.manualSourcePath.length - 1, 1) != dirsep) {
        	DialogService.dialog('Include a file?', "The path does not end with a '" + dirsep + "' character, which means that you include a file, not a folder.\n\nDo you want to include the specified file?", ['No', 'Yes'], function(ix) {
        		if (ix == 1)
        			continuation();
        	});
        } else {
        	continuation();
		}




    };

	$scope.toggleArraySelection = function (lst, value) {
	    var ix = lst.indexOf(value);

	    if (ix > -1)
			lst.splice(ix, 1);
	    else
			lst.push(value);
	};

	$scope.save = function() {
		var result = {
			Backup: angular.copy($scope.Backup),
			Schedule: angular.copy($scope.Schedule)
		};

		var opts = angular.copy($scope.Options);

		if (!$scope.ExcludeLargeFiles)
			delete opts['--skip-files-larger-than'];

		if (($scope.ExcludeAttributes || []).length > 0) {
			opts['--exclude-files-attributes'] = $scope.ExcludeAttributes.join(',');
			if (opts['--exclude-files-attributes'] == '')
				delete opts['--exclude-files-attributes'];
		}

		if ((opts['encryption-module'] || '').length == 0)
			opts['--no-encryption'] = 'true';

		if (!AppUtils.parse_extra_options(scope.ExtendedOptions, opts))
			return false;


		result.Backup.Settings = [];
		for(var k in opts) {
			var origfilter = "";
			var origarg = null;
			for(var i in $scope.rawddata.Backup.Settings)
				if ($scope.rawddata.Backup.Settings[i].Name == k) {
					origfilter = $scope.rawddata.Backup.Settings[i].Filter;
					origarg = $scope.rawddata.Backup.Settings[i].Argument;
					break;
				}

			result.Backup.Settings.push({
				Name: k,
				Value: opts[k],
				Filter: origfilter,
				Argument: origarg
			});
		}

		var filterstrings = result.Backup.Filters || [];
		result.Backup.Filters = [];
		for(var f in filterstrings)
			result.Backup.Filters.push({
				Order: result.Backup.Filters.length,
				Include: filterstrings[f].substr(0, 1) == '+',
				Expression: filterstrings[f].substr(1)
			});

		if ($routeParams.backupid == null) {
			AppService.post('/backups', result, {'headers': {'Content-Type': 'application/json'}}).then(function() {
				$location.path('/');
			}, AppUtils.connectionError);
		} else {
			AppService.put('/backup/' + $routeParams.backupid, result, {'headers': {'Content-Type': 'application/json'}}).then(function() {
				$location.path('/');
			}, AppUtils.connectionError);
		}
	};


	function setupScope(data) {
		$scope.Backup = angular.copy(data.Backup);
		$scope.Schedule = angular.copy(data.Schedule);

		$scope.Options = {};
		var extopts = {};

		for(var n in $scope.Backup.Settings) {
			var e = $scope.Backup.Settings[n];
			if (e.Name.indexOf('--') == 0)
				extopts[e.Name] = e.Value;
			else
				$scope.Options[e.Name] = e.Value;
		}

		var filters = $scope.Backup.Filters;
		$scope.Backup.Filters = [];

		$scope.Backup.Sources = $scope.Backup.Sources || [];

		for(var ix in filters)
			$scope.Backup.Filters.push((filters[ix].Include ? '+' : '-') + filters[ix].Expression);

		$scope.ExcludeLargeFiles = $scope.Options['--skip-files-larger-than'];
		$scope.ExcludeAttributes = ($scope.Options['--exclude-files-attributes'] || '').split(',');

		$scope.RepeatPasshrase = $scope.Options['passphrase'];

		delete extopts['--skip-files-larger-than'];
		delete extopts['--exclude-files-attributes'];
		delete extopts['--no-encryption'];

		$scope.ExtendedOptions = AppUtils.serializeAdvancedOptionsToArray(extopts);

		var now = new Date();
		if ($scope.Schedule != null) {
			var time = AppUtils.parseDate($scope.Schedule.Time);
            if (isNaN(time)) {
                time = AppUtils.parseDate("1970-01-01T" + $scope.Schedule.Time);
                if (!isNaN(time))
                	time = new Date(now.getFullYear(), now.getMonth(), now.getDate(), time.getHours(), time.getMinutes(), time.getSeconds());

                if (time < now)
                	time = new Date(time.setDate(time.getDate() + 1));
            }

            if (isNaN(time)) {
                time = new Date(now.getFullYear(), now.getMonth(), now.getDate(), 13, 0, 0);
                if (time < now)
                	time = new Date(time.setDate(time.getDate() + 1));
            }

            $scope.Schedule.Time = time;
		} else {
            time = new Date(now.getFullYear(), now.getMonth(), now.getDate(), 13, 0, 0);
            if (time < now)
            	time = new Date(time.setDate(time.getDate() + 1));

			oldSchedule = {
				Repeat: '1D',
				Time: time
			};
		}
	}

	function reloadOptionsList()
	{
		if ($scope.Options == null)
			return;

		var encmodule = $scope.Options['encryption-module'] || '';
		var compmodule = $scope.Options['compression-module'] || $scope.Options['--compression-module'] || 'zip';
		var backmodule = $scope.Backup.TargetURL || '';
		var ix = backmodule.indexOf(':');
		if (ix > 0)
			backmodule = backmodule.substr(0, ix);

		$scope.ExtendedOptionList = AppUtils.buildOptionList($scope.SystemInfo, encmodule, compmodule, backmodule);
	};

	$scope.$watch("Options['encryption-module']", reloadOptionsList);
	$scope.$watch("Options['compression-module']", reloadOptionsList);
	$scope.$watch("Options['--compression-module']", reloadOptionsList);
	$scope.$watch("Backup.TargetURL", reloadOptionsList);
	$scope.$on('systeminfochanged', reloadOptionsList);

	if ($routeParams.backupid == null) {

		AppService.get('/backupdefaults').then(function(data) {

			$scope.rawddata = data.data.data;
			setupScope($scope.rawddata);

		}, function(data) {
			AppUtils.connectionError('Failed to read backup defaults', data);
			$location.path('/');
		});

	} else {

		AppService.get('/backup/' + $routeParams.backupid).then(function(data) {

			$scope.rawddata = data.data.data;
			setupScope($scope.rawddata);

		}, function() {
			AppUtils.connectionError.apply(AppUtils, arguments);
			$location.path('/');
		});
	}
});