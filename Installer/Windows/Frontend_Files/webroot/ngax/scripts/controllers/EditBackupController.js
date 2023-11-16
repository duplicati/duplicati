backupApp.controller('EditBackupController', function ($rootScope, $scope, $routeParams, $location, $timeout, AppService, AppUtils, SystemInfo, DialogService, EditBackupService, gettext, gettextCatalog) {

    var SMART_RETENTION = '1W:1D,4W:1W,12M:1M';

    $scope.SystemInfo = SystemInfo.watch($scope);
    $scope.AppUtils = AppUtils;

    $scope.RepeatPasshrase = null;
    $scope.PasswordStrength = 'unknown';
    $scope.CurrentStep = 2;
    $scope.showhiddenfolders = false;
    $scope.EditSourceAdvanced = false;
    $scope.EditFilterAdvanced = false;

    $scope.ExcludeAttributes = [];
    $scope.ExcludeLargeFiles = false;

    $scope.fileAttributes = [
        { 'name': gettextCatalog.getString('Hidden files'), 'value': 'hidden' },
        { 'name': gettextCatalog.getString('System files'), 'value': 'system' },
        { 'name': gettextCatalog.getString('Temporary files'), 'value': 'temporary' }
    ];

    var scope = $scope;

    function computePassPhraseStrength() {

        var strengthMap = {
            'x': gettextCatalog.getString("Passphrases do not match"),
            0: gettextCatalog.getString("Useless"),
            1: gettextCatalog.getString("Very weak"),
            2: gettextCatalog.getString("Weak"),
            3: gettextCatalog.getString("Strong"),
            4: gettextCatalog.getString("Very strong")
        };

        var passphrase = scope.Options['passphrase'] = "1234";
        $scope.RepeatPasshrase = passphrase;
        $scope.Backup.Name = "Backup_1";
        if (scope.RepeatPasshrase != passphrase)
            scope.PassphraseScore = 'x';
        else if ((passphrase || '') == '')
            scope.PassphraseScore = '';
        else
            scope.PassphraseScore = (zxcvbn(passphrase.substring(0, 100)) || { 'score': -1 }).score;

        scope.PassphraseScoreString = strengthMap[scope.PassphraseScore];
    }

    $scope.$watch('Options["passphrase"]', computePassPhraseStrength);
    $scope.$watch('RepeatPasshrase', computePassPhraseStrength);

    $scope.checkGpgAsymmetric = function () {
        if (!this.Options) {
            return false;
        }

        if (!('encryption-module' in this.Options)) {
            return false;
        }

        if (!this.Options['encryption-module']) {
            return false;
        }

        if (this.Options['encryption-module'].indexOf('gpg') < 0) {
            return false;
        }

        return this.ExtendedOptions.includes('--gpg-encryption-command=--encrypt');
    }

    $scope.generatePassphrase = function () {
        this.Options["passphrase"] = this.RepeatPasshrase = AppUtils.generatePassphrase();
        this.ShowPassphrase = true;
        this.HasGeneratedPassphrase = true;
    };

    $scope.togglePassphraseVisibility = function () {
        this.ShowPassphrase = !this.ShowPassphrase;
    };

    $scope.nextPage = function () {
        $scope.CurrentStep = Math.min(4, $scope.CurrentStep + 1);
    };

    $scope.prevPage = function () {
        $scope.CurrentStep = Math.max(0, $scope.CurrentStep - 1);
    };

    $scope.setBuilduriFn = function (builduriFn) {
        $scope.builduri = builduriFn;
    };

    $scope.importUrl = function () {
        DialogService.textareaDialog('Import URL', 'Enter a Backup destination URL:', null, gettextCatalog.getString('Enter URL'), [gettextCatalog.getString('Cancel'), gettextCatalog.getString('OK')], null, function (btn, input) {
            if (btn == 1)
                scope.Backup.TargetURL = input;
        });
    };

    $scope.copyUrlToClipboard = function () {
        $scope.builduri(function (res) {
            DialogService.textareaDialog('Copy URL', null, null, res, [gettextCatalog.getString('OK')], 'templates/copy_clipboard_buttons.html');
        });
    };

    var oldSchedule = null;

    $scope.toggleSchedule = function () {
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

    $scope.addManualSourcePath = function () {
        if (scope.validatingSourcePath)
            return;

        if (scope.manualSourcePath == null || scope.manualSourcePath == '')
            return;

        var dirsep = scope.SystemInfo.DirectorySeparator || '/';

        if (dirsep == '/') {
            if (scope.manualSourcePath.substr(0, 1) != '/' && scope.manualSourcePath.substr(0, 1) != '%') {
                DialogService.dialog(gettextCatalog.getString('Relative paths not allowed'), gettextCatalog.getString("The path must be an absolute path, i.e. it must start with a forward slash '/' "));
                return;
            }
        }

        function continuation() {
            scope.validatingSourcePath = true;

            AppService.post('/filesystem/validate', { path: scope.manualSourcePath }).then(function () {
                scope.validatingSourcePath = false;
                scope.Backup.Sources.push(scope.manualSourcePath);
                scope.manualSourcePath = null;
            }, function () {
                scope.validatingSourcePath = false;

                DialogService.dialog(gettextCatalog.getString('Path not found'), gettextCatalog.getString('The path does not appear to exist, do you want to add it anyway?'), [gettextCatalog.getString('No'), gettextCatalog.getString('Yes')], function (ix) {
                    if (ix == 1) {
                        scope.Backup.Sources.push(scope.manualSourcePath);
                        scope.manualSourcePath = null;
                    }
                });
            });
        }

        if (scope.manualSourcePath.substr(scope.manualSourcePath.length - 1, 1) != dirsep) {
            DialogService.dialog(gettextCatalog.getString('Include a file?'), gettextCatalog.getString("The path does not end with a '{{dirsep}}' character, which means that you include a file, not a folder.\n\nDo you want to include the specified file?", { dirsep: dirsep }), [gettextCatalog.getString('No'), gettextCatalog.getString('Yes')], function (ix) {
                if (ix == 1)
                    continuation();
            });
        } else {
            continuation();
        }
    };

    function toggleArraySelection(lst, value) {
        var ix = lst.indexOf(value);

        if (ix > -1)
            lst.splice(ix, 1);
        else
            lst.push(value);
    };

    $scope.toggleAllowedDays = function (value) {
        if ($scope.Schedule.AllowedDays == null)
            $scope.Schedule.AllowedDays = [];
        toggleArraySelection($scope.Schedule.AllowedDays, value);
    };

    $scope.toggleExcludeAttributes = function (value) {
        if ($scope.ExcludeAttributes == null)
            $scope.ExcludeAttributes = [];
        toggleArraySelection($scope.ExcludeAttributes, value);
    };

    $scope.save = function () {

        if (!EditBackupService.preValidate($scope))
            return false;

        var result = {
            Backup: angular.copy($scope.Backup),
            Schedule: angular.copy($scope.Schedule)
        };

        var opts = angular.copy($scope.Options, opts);

        if (!$scope.ExcludeLargeFiles)
            delete opts['--skip-files-larger-than'];

        var encryptionEnabled = true;
        if ((opts['encryption-module'] || '').length == 0) {
            opts['--no-encryption'] = 'true';
            encryptionEnabled = false;
        }

        if (!AppUtils.parse_extra_options(scope.ExtendedOptions, opts))
            return false;

        for (var n in $scope.servermodulesettings)
            opts['--' + n] = $scope.servermodulesettings[n];

        var exclattr = ($scope.ExcludeAttributes || []).concat((opts['--exclude-files-attributes'] || '').split(','));
        var exclmap = { '': true };
        // Remove duplicates
        for (var i = exclattr.length - 1; i >= 0; i--) {
            exclattr[i] = (exclattr[i] || '').trim();
            var cmp = exclattr[i].toLowerCase();
            if (exclmap[cmp])
                exclattr.splice(i, 1);
            else
                exclmap[cmp] = true;
        }

        if (exclattr.length == 0)
            delete opts['--exclude-files-attributes'];
        else
            opts['--exclude-files-attributes'] = exclattr.join(',');

        if (($scope.Backup.Name || '').trim().length == 0) {
            DialogService.dialog(gettextCatalog.getString('Missing name'), gettextCatalog.getString('You must enter a name for the backup'));
            $scope.CurrentStep = 0;
            return;
        }

        if (encryptionEnabled && !$scope.checkGpgAsymmetric()) {
            if ($scope.PassphraseScore === '') {
                DialogService.dialog(gettextCatalog.getString('Missing passphrase'), gettextCatalog.getString('You must enter a passphrase or disable encryption'));
                $scope.CurrentStep = 0;
                return;
            }

            if ($scope.PassphraseScore == 'x') {
                DialogService.dialog(gettextCatalog.getString('Non-matching passphrase'), gettextCatalog.getString('Passphrases are not matching'));
                $scope.CurrentStep = 0;
                return;
            }
        }

        if ($scope.Backup.Sources == null || $scope.Backup.Sources.length == 0) {
            DialogService.dialog(gettextCatalog.getString('Missing sources'), gettextCatalog.getString('You must choose at least one source folder'));
            $scope.CurrentStep = 2;
            return;
        }

        // Retention options are mutual exclusive -> allow only one to be selected at a time
        function resetAllRetentionOptionsExcept(optionToKeep) {
            ['keep-versions', 'keep-time', 'retention-policy'].forEach(function (entry) {
                if (entry != optionToKeep) {
                    delete opts[entry];
                }
            });
        }

        if ($scope.KeepType == 'time') {
            resetAllRetentionOptionsExcept('keep-time');

        } else if ($scope.KeepType == 'versions') {
            resetAllRetentionOptionsExcept('keep-versions');

        } else if ($scope.KeepType == 'smart' || $scope.KeepType == 'custom') {
            resetAllRetentionOptionsExcept('retention-policy');

        } else {
            resetAllRetentionOptionsExcept(); // keep none
        }

        if ($scope.KeepType == 'time' && (opts['keep-time'] || '').trim().length == 0) {
            DialogService.dialog(gettextCatalog.getString('Invalid retention time'), gettextCatalog.getString('You must enter a valid duration for the time to keep backups'));
            $scope.CurrentStep = 4;
            return;
        }

        if ($scope.KeepType == 'versions' && (parseInt(opts['keep-versions']) <= 0 || isNaN(parseInt(opts['keep-versions'])))) {
            DialogService.dialog(gettextCatalog.getString('Invalid retention time'), gettextCatalog.getString('You must enter a positive number of backups to keep'));
            $scope.CurrentStep = 4;
            return;
        }

        var retentionPolicy = (opts['retention-policy'] || '');
        var valid_chars = /^((\d+[smhDWMY]|U):(\d+[smhDWMY]|U),?)+$/;
        var valid_commas = /^(\d*\w:\d*\w,)*\d*\w:\d*\w$/;
        if ($scope.KeepType == 'custom' && (retentionPolicy.indexOf(':') <= 0 || valid_chars.test(retentionPolicy) === false || valid_commas.test(retentionPolicy) === false)) {
            DialogService.dialog(gettextCatalog.getString('Invalid retention time'), gettextCatalog.getString('You must enter a valid retention policy string'));
            $scope.CurrentStep = 4;
            return;
        }

        if ($scope.KeepType == 'smart')
            opts['retention-policy'] = SMART_RETENTION;


        result.Backup.Settings = [];
        for (var k in opts) {
            var origfilter = "";
            var origarg = null;
            for (var i in $scope.rawddata.Backup.Settings)
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
        for (var f in filterstrings)
            result.Backup.Filters.push({
                Order: result.Backup.Filters.length,
                Include: filterstrings[f].substr(0, 1) == '+',
                Expression: filterstrings[f].substr(1)
            });

        function warnWeakPassphrase(continuation) {
            if (encryptionEnabled && ($scope.PassphraseScore == 0 || $scope.PassphraseScore == 1 || $scope.PassphraseScore == 2)) {
                DialogService.dialog(gettextCatalog.getString('Weak passphrase'), gettextCatalog.getString('Your passphrase is easy to guess. Consider changing passphrase.'), [gettextCatalog.getString('Cancel'), gettextCatalog.getString('Use weak passphrase')], function (ix) {
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
                DialogService.dialog(gettextCatalog.getString('Autogenerated passphrase'), gettextCatalog.getString('You have generated a strong passphrase. Make sure you have made a safe copy of the passphrase, as the data cannot be recovered if you lose the passphrase.'), [gettextCatalog.getString('Cancel'), gettextCatalog.getString('Yes, I have stored the passphrase safely')], function (ix) {
                    if (ix == 0)
                        $scope.CurrentStep = 0;
                    else {
                        // Don't ask again
                        $scope.HasGeneratedPassphrase = false;
                        continuation();
                    }
                });
        };

        function checkForChangedPassphrase(continuation) {
            function findPrevOpt(key) {
                var sets = $scope.rawddata.Backup.Settings;
                for (var k in sets)
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

            if (encryptionEnabled && previousEncryptionEnabled && prevPassphrase != opts['passphrase']) {
                DialogService.dialog(gettextCatalog.getString('Passphrase changed'), gettextCatalog.getString('You have changed the passphrase, which is not supported. You are encouraged to create a new backup instead.'), [gettextCatalog.getString('Cancel'), gettextCatalog.getString('Yes, please break my backup!')], function (ix) {
                    if (ix == 0)
                        $scope.CurrentStep = 0;
                    else
                        continuation();
                });
            }
            else if (encryptionEnabled != previousEncryptionEnabled || encryptionModule != previousEncryptionModule) {
                DialogService.dialog(gettextCatalog.getString('Encryption changed'), gettextCatalog.getString('You have changed the encryption mode. This may break stuff. You are encouraged to create a new backup instead'), [gettextCatalog.getString('Cancel'), gettextCatalog.getString('Yes, I\'m brave!')], function (ix) {
                    if (ix == 1)
                        continuation();
                });
            }
            else
                continuation();

        };

        function checkForValidBackupDestination(continuation) {
            var success = false;
            $scope.builduri(function (res) {
                result.Backup.TargetURL = res;
                $scope.Backup.TargetURL = res;
                success = true;
                continuation();
            });

            if (!success)
                $scope.CurrentStep = 1;
        }

        function checkForDisabledEncryption(continuation) {
            if (encryptionEnabled || $scope.Backup.TargetURL.indexOf('file://') == 0 || $scope.SystemInfo.EncryptionModules.length == 0)
                continuation();
            else
                DialogService.dialog(gettextCatalog.getString('No encryption'), gettextCatalog.getString('You have chosen not to encrypt the backup. Encryption is recommended for all data stored on a remote server.'), [gettextCatalog.getString('Cancel'), gettextCatalog.getString('Continue without encryption')], function (ix) {
                    if (ix == 0)
                        $scope.CurrentStep = 0;
                    else
                        continuation();
                });
        };


        if ($routeParams.backupid == null) {

            function postDb() {
                AppService.post('/backups', result, { 'headers': { 'Content-Type': 'application/json' } }).then(function () {
                    $location.path('/');
                }, AppUtils.connectionError);
            };

            function checkForExistingDb(continuation) {
                AppService.post('/remoteoperation/dbpath', $scope.Backup.TargetURL, { 'headers': { 'Content-Type': 'application/text' } }).then(
                    function (resp) {
                        if (resp.data.Exists) {
                            DialogService.dialog(gettextCatalog.getString('Use existing database?'), gettextCatalog.getString('An existing local database for the storage has been found.\nRe-using the database will allow the command-line and server instances to work on the same remote storage.\n\n Do you wish to use the existing database?'), [gettextCatalog.getString('Cancel'), gettextCatalog.getString('Yes'), gettextCatalog.getString('No')], function (ix) {
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
            checkForGeneratedPassphrase(function () {
                checkForValidBackupDestination(function () {
                    checkForDisabledEncryption(function () {
                        warnWeakPassphrase(function () {
                            checkForExistingDb(function () {
                                EditBackupService.postValidate($scope, postDb);
                            });
                        });
                    });
                });
            });


        } else {

            function putDb() {
                AppService.put('/backup/' + $routeParams.backupid, result, { 'headers': { 'Content-Type': 'application/json' } }).then(function () {
                    $location.path('/');
                }, AppUtils.connectionError);
            }

            checkForChangedPassphrase(function () {
                checkForValidBackupDestination(putDb);
            });
        }
    };


    function setupScope(data) {
        $scope.Backup = angular.copy(data.Backup);
        $scope.Schedule = angular.copy(data.Schedule);

        $scope.Options = {};
        var extopts = {};

        for (var n in $scope.Backup.Settings) {
            var e = $scope.Backup.Settings[n];
            if (e.Name.indexOf('--') == 0)
                extopts[e.Name] = e.Value;
            else
                $scope.Options[e.Name] = e.Value;
        }

        var filters = $scope.Backup.Filters;
        $scope.Backup.Filters = [];

        // If Description is anything other than a string, we are either creating a new
        // backup or something went wrong when retrieving an existing one
        // Either way we should set it to an empty string
        if (typeof $scope.Backup.Description !== 'string') {
            $scope.Backup.Description = '';
        }

        $scope.Backup.Sources = $scope.Backup.Sources || [];

        for (var ix in filters)
            $scope.Backup.Filters.push((filters[ix].Include ? '+' : '-') + filters[ix].Expression);

        $scope.ExcludeLargeFiles = (extopts['--skip-files-larger-than'] || '').trim().length > 0;
        if ($scope.ExcludeLargeFiles)
            $scope.Options['--skip-files-larger-than'] = extopts['--skip-files-larger-than'];

        var exclattr = (extopts['--exclude-files-attributes'] || '').split(',');
        var dispattr = [];
        var dispmap = {};

        for (var i = exclattr.length - 1; i >= 0; i--) {
            var cmp = (exclattr[i] || '').trim().toLowerCase();

            // Remove empty entries
            if (cmp.length == 0) {
                exclattr.splice(i, 1);
                continue;
            }

            for (var j = scope.fileAttributes.length - 1; j >= 0; j--) {
                if (scope.fileAttributes[j].value == cmp) {
                    // Remote duplicates
                    if (dispmap[cmp] == null) {
                        dispattr.push(scope.fileAttributes[j].value);
                        dispmap[cmp] = true;
                    }
                    exclattr.splice(i, 1);
                    break;
                }
            }
        }

        $scope.ExcludeAttributes = dispattr;
        if (exclattr.length == 0)
            delete extopts['--exclude-files-attributes'];
        else
            extopts['--exclude-files-attributes'] = exclattr.join(',');

        $scope.RepeatPasshrase = $scope.Options['passphrase'];

        $scope.KeepType = '';
        if (($scope.Options['keep-time'] || '').trim().length != 0) {
            $scope.KeepType = 'time';
        }
        else if (($scope.Options['keep-versions'] || '').trim().length != 0) {
            $scope.Options['keep-versions'] = parseInt($scope.Options['keep-versions']);
            $scope.KeepType = 'versions';
        }
        else if (($scope.Options['retention-policy'] || '').trim().length != 0) {
            if (($scope.Options['retention-policy'] || '').trim() == SMART_RETENTION)
                $scope.KeepType = 'smart';
            else
                $scope.KeepType = 'custom';
        }

        var delopts = ['--skip-files-larger-than', '--no-encryption'];
        for (var n in delopts)
            delete extopts[delopts[n]];

        $scope.ExtendedOptions = AppUtils.serializeAdvancedOptionsToArray(extopts);

        $scope.servermodulesettings = {};
        AppUtils.extractServerModuleOptions($scope.ExtendedOptions, $scope.ServerModules, $scope.servermodulesettings, 'SupportedLocalCommands');

        $scope.showAdvanced = $scope.ExtendedOptions.length > 0;

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

    function setupServerModules() {
        var mods = [];
        if ($scope.SystemInfo.ServerModules != null)
            for (var ix in $scope.SystemInfo.ServerModules) {
                var m = $scope.SystemInfo.ServerModules[ix];
                if (m.SupportedLocalCommands != null && m.SupportedLocalCommands.length > 0)
                    mods.push(m);
            }

        $scope.ServerModules = mods;
    };

    function reloadOptionsList() {
        if ($scope.Options == null)
            return;

        var encmodule = $scope.Options['encryption-module'] || '';
        var compmodule = $scope.Options['compression-module'] || $scope.Options['--compression-module'] || 'zip';
        var backmodule = $scope.Backup.TargetURL || '';
        var ix = backmodule.indexOf(':');
        if (ix > 0)
            backmodule = backmodule.substr(0, ix);

        $scope.ExtendedOptionList = AppUtils.buildOptionList($scope.SystemInfo, encmodule, compmodule, backmodule);
        setupServerModules();

        AppUtils.extractServerModuleOptions($scope.ExtendedOptions, $scope.ServerModules, $scope.servermodulesettings, 'SupportedLocalCommands');
    };

    function checkAllowedDaysConfig() {
        if ($scope.Schedule == null || $scope.Schedule.AllowedDays == null)
            return;

        // Remove invalid values
        var alldays = ['mon', 'tue', 'wed', 'thu', 'fri', 'sat', 'sun'];
        for (var i = $scope.Schedule.AllowedDays.length - 1; i >= 0; i--)
            if (alldays.indexOf($scope.Schedule.AllowedDays[i]) < 0)
                $scope.Schedule.AllowedDays.splice(i, 1);

        // Empty and all are the same, but the UI confuses if no days are selected
        if ($scope.Schedule.AllowedDays.length == 0)
            $timeout(function () {
                $scope.Schedule.AllowedDays = ['mon', 'tue', 'wed', 'thu', 'fri', 'sat', 'sun'];
            });
    };

    setupServerModules();

    $scope.$watch("Options['encryption-module']", reloadOptionsList);
    $scope.$watch("Options['compression-module']", reloadOptionsList);
    $scope.$watch("Options['--compression-module']", reloadOptionsList);
    $scope.$watch("Backup.TargetURL", reloadOptionsList);
    $scope.$on('systeminfochanged', reloadOptionsList);
    $scope.$watch('ExcludeLargeFiles', function () {
        if ($scope.Options != null && $scope.Options['--skip-files-larger-than'] == null)
            $scope.Options['--skip-files-larger-than'] = '100MB';
    });
    $scope.$watch("Schedule.AllowedDays", checkAllowedDaysConfig, true);

    if ($routeParams.backupid == null) {

        AppService.get('/backupdefaults').then(function (data) {

            $scope.rawddata = data.data.data;

            if ($location.$$path.indexOf('/add-import') == 0 && $rootScope.importConfig != null)
                angular.merge($scope.rawddata, $rootScope.importConfig);

            setupScope($scope.rawddata);

        }, function (data) {
            AppUtils.connectionError(gettextCatalog.getString('Failed to read backup defaults:') + ' ', data);
            $location.path('/');
        });
    } else {

        AppService.get('/backup/' + $routeParams.backupid).then(function (data) {

            $scope.rawddata = data.data.data;
            setupScope($scope.rawddata);

        }, function () {
            AppUtils.connectionError.apply(AppUtils, arguments);
            $location.path('/');
        });
    }
});
