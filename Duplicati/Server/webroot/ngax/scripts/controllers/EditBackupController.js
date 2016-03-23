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
    	{'name': 'Hidden files', 'value': 'hidden'}, 
    	{'name': 'System files', 'value': 'system'}, 
    	{'name': 'Temporary files', 'value': 'temporary'}
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
			scope.PassphraseScore = (zxcvbn(passphrase) || {'score': -1}).score;

		scope.PassphraseScoreString = strengthMap[scope.PassphraseScore] || 'Unknown';	
	}

	$scope.$watch('Options["passphrase"]', computePassPhraseStrength);
	$scope.$watch('RepeatPasshrase', computePassPhraseStrength);

	$scope.generatePassphrase = function() {
		this.Options["passphrase"] = this.RepeatPasshrase = AppUtils.generatePassphrase();
		this.ShowPassphrase = true;
		this.HasGeneratedPassphrase = true;
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
	        });
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

		var encryptionEnabled = true;
		if ((opts['encryption-module'] || '').length == 0) {
			opts['--no-encryption'] = 'true';
			encryptionEnabled = false;
		}

		if (!AppUtils.parse_extra_options(scope.ExtendedOptions, opts))
			return false;

		if (($scope.Backup.Name || '').trim().length == 0) {
			DialogService.dialog('Missing name', 'You must enter a name for the backup');
			$scope.CurrentStep = 0;
			return;
		}


		if (encryptionEnabled) {
			if ($scope.PassphraseScore === '') {
				DialogService.dialog('Missing passphrase', 'You must enter a passphrase or disable encryption');
				$scope.CurrentStep = 0;
				return;
			}

			if ($scope.PassphraseScore == 'x') {
				DialogService.dialog('Non-matching passphrase', 'Passphrases are not matching');
				$scope.CurrentStep = 0;
				return;
			}
		}

		if (($scope.Backup.TargetURL || '').trim().length == 0) {
			DialogService.dialog('Missing destination', 'You must enter a destination where the backups are stored');
			$scope.CurrentStep = 0;
			return;
		}

		if ($scope.Backup.Sources == null || $scope.Backup.Sources.length == 0) {
			DialogService.dialog('Missing sources', 'You must choose at least one source folder');
			$scope.CurrentStep = 1;
			return;
		}

		if ($scope.KeepType == 'time' || $scope.KeepType == '')
			delete opts['keep-versions'];
		if ($scope.KeepType == 'versions' || $scope.KeepType == '')
			delete opts['keep-time'];

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

		function warnWeakPassphrase(continuation) {
			if (encryptionEnabled && ($scope.PassphraseScore == 0 || $scope.PassphraseScore == 1 || $scope.PassphraseScore == 2)) {
				DialogService.dialog('Weak passphrase', 'Your passphrase is easy to guess. Consider changing passphrase.', ['Cancel', 'Use weak passphrase'], function(ix) {
					if (ix == 0)
						$scope.CurrentStep = 0;
					else
						continuation();
				});
			}
			else
				continuation();
		};

		function checkForGeneratedPassphrase(continuation) {
			if (!$scope.HasGeneratedPassphrase || !encryptionEnabled)
				continuation();
			else
				DialogService.dialog('Autogenerated passphrase', 'You have generated a strong passphrase. Make sure you have made a safe copy of the passphrase, as the data cannot be recovered if you loose the passphrase.', ['Cancel', 'Yes, I have stored the passphrase safely'], function(ix) {
					if (ix == 0)
						$scope.CurrentStep = 0;
					else
					{
						// Don't ask again
						$scope.HasGeneratedPassphrase = false;
						continuation();
					}
				});
		};

		function checkForChangedPassphrase(continuation) {
			function findPrevOpt(key) {
				var sets = $scope.rawddata.Backup.Settings;
				for(var k in sets)
					if (sets[k].Name == key)
						return sets[k];

				return null;
			};

			var previousEncryptionOpt = findPrevOpt('--no-encryption');
			var prevPassphraseOpt = findPrevOpt('passphrase');
			var previousEncryptionModuleOpt = findPrevOpt('encryption-module');

			var prevPassphrase = prevPassphraseOpt == null ? null : prevPassphraseOpt.Value;
			var previousEncryptionEnabled = previousEncryptionOpt == null ? true : !AppUtils.parseBoolString(previousEncryptionOpt.Value, true);
			var previousEncryptionModule = (!previousEncryptionEnabled || previousEncryptionModuleOpt == null) ? '' : (previousEncryptionModuleOpt.Value || '');

			var encryptionModule = opts['encryption-module'] || '';

			if (encryptionEnabled && previousEncryptionEnabled && prevPassphrase != opts['passphrase'])
			{
				DialogService.dialog('Passphrase changed', 'You have changed the passphrase, which is not supported. You are encouraged to create a new backup instead.', ['Cancel', 'Yes, please break my backup!'], function(ix) {
					if (ix == 0)
						$scope.CurrentStep = 0;
					else
						continuation();
				});				
			}
			else if (encryptionEnabled != previousEncryptionEnabled || encryptionModule != previousEncryptionModule)
			{
				DialogService.dialog('Encryption changed', 'You have changed the encryption mode. This may break stuff. You are encouraged to create a new backup instead', ['Cancel', 'Yes, I\'m brave!'], function(ix) {
					if (ix == 1)
						continuation();
				});	
			}
			else
				continuation();

		};

		function checkForDisabledEncryption(continuation) {
			if (encryptionEnabled || $scope.Backup.TargetURL.indexOf('file://') == 0 || $scope.SystemInfo.EncryptionModules.length == 0)
				continuation();
			else
				DialogService.dialog('No encryption', 'You have chosen not to encrypt the backup. Encryption is recommended for all data stored on a remote server.', ['Cancel', 'Continue without encryption'], function(ix) {
					if (ix == 0)
						$scope.CurrentStep = 0;
					else
						continuation();
				});
		};


		if ($routeParams.backupid == null) {

			function postDb() {
				AppService.post('/backups', result, {'headers': {'Content-Type': 'application/json'}}).then(function() {
					$location.path('/');
				}, AppUtils.connectionError);								
			};

			function checkForExistingDb(continuation) {
				AppService.post('/remoteoperation/dbpath', $scope.Backup.TargetURL, {'headers': {'Content-Type': 'application/text'}}).then(
					function(resp) {
						if (resp.data.Exists) {
							DialogService.dialog('Use existing database?', 'An existing local database for the storage has been found.\nRe-using the database will allow the command-line and server instances to work on the same remote storage.\n\n Do you wish to use the existing database?', ['Cancel', 'Yes', 'No'], function(ix) {
								if (ix == 2)
									result.Backup.DBPath = resp.data.Path;

								if (ix == 1 || ix == 2)
									continuation();
							});
						}
						else
							continuation();

					}, AppUtils.connectionError
				);
			};

			// Chain calls
			checkForGeneratedPassphrase(function() {
				checkForDisabledEncryption(function() {
					warnWeakPassphrase(function() {
						checkForExistingDb(postDb);
					});
				});
			});


		} else {

			function putDb() {
				AppService.put('/backup/' + $routeParams.backupid, result, {'headers': {'Content-Type': 'application/json'}}).then(function() {
					$location.path('/');
				}, AppUtils.connectionError);
			};

			checkForChangedPassphrase(putDb);
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

		$scope.KeepType = '';
		if (($scope.Options['keep-time'] || '').trim().length != 0)
		{
			$scope.KeepType = 'time';
		}
		else if (($scope.Options['keep-versions'] || '').trim().length != 0)
		{
			$scope.Options['keep-versions'] = parseInt($scope.Options['keep-versions']);
			$scope.KeepType = 'versions';
		}

		var delopts = ['--skip-files-larger-than', '--exclude-files-attributes', '--no-encryption']
		for(var n in delopts)
			delete extopts[delopts[n]];

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