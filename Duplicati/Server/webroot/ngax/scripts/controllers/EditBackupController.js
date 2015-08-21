backupApp.controller('EditBackupController', function ($scope, $routeParams, $location, AppService, AppUtils, SystemInfo) {

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

    $scope.daysOfWeek = [
    	{name: 'Mon', value: 'mon'}, 
    	{name: 'Tue', value: 'tue'}, 
    	{name: 'Wed', value: 'wed'}, 
    	{name: 'Thu', value: 'thu'}, 
    	{name: 'Fri', value: 'fri'}, 
    	{name: 'Sat', value: 'sat'}, 
    	{name: 'Sun', value: 'sun'}
	];

	var scope = $scope;

	function computePassPhraseStrength() {

        var strengthMap = {
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

	$scope.save = function() {
		alert('Save me!');
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
        		alert("The path must be an absolute path, i.e. it must start with a forward slash '/' ");
        		return;
        	}
        }

        if (scope.manualSourcePath.substr(scope.manualSourcePath.length - 1, 1) != dirsep) {
        	if (!confirm("The path does not end with a '" + dirsep + "' character, which means that you include a file, not a folder.\n\nDo you want to include the specified file?" ))
        		return;
        }

        scope.validatingSourcePath = true;

        AppService.post('/filesystem/validate', {path: scope.manualSourcePath}).then(function() {
            scope.validatingSourcePath = false;
            scope.Backup.Sources.push(scope.manualSourcePath);
            scope.manualSourcePath = null;
        }, function() {
            scope.validatingSourcePath = false;
            if (confirm('The path does not appear to exist, do you want to add it anyway?')) {
                scope.Backup.Sources.push(scope.manualSourcePath);
                scope.manualSourcePath = null;
            }
        })

    };

	$scope.toggleArraySelection = function (lst, value) {
	    var ix = lst.indexOf(value);

	    if (ix > -1)
			lst.splice(ix, 1);
	    else
			lst.push(value);
	};

	function setupScope(data) {
		$scope.Backup = angular.copy(data.Backup);
		$scope.Schedule = angular.copy(data.Schedule);

		$scope.Options = {};
		for(var n in $scope.Backup.Settings) {
			var e = $scope.Backup.Settings[n];
			$scope.Options[e.Name] = e.Value;
		}

		var filters = $scope.Backup.Filters;
		$scope.Backup.Filters = [];

		$scope.Backup.Sources = $scope.Backup.Sources || [];

		for(var ix in filters)
			$scope.Backup.Filters.push((filters[ix].Include ? '+' : '-') + filters[ix].Expression);

		$scope.ExcludeLargeFiles = $scope.Options['--skip-files-larger-than'];
		$scope.ExcludeAttributes = ($scope.Options['--exclude-files-attributes'] || '').split(',');		
	}

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