backupApp.controller('RestoreController', function ($rootScope, $scope, $routeParams, $location, AppService, AppUtils, SystemInfo, ServerStatus, DialogService, BackupList, gettextCatalog) {

    $scope.SystemInfo = SystemInfo.watch($scope);
    $scope.AppUtils = AppUtils;

    $scope.restore_step = 0;
    $scope.connecting = false;
    $scope.HideFolderBrowser = true;
    $scope.RestoreLocation = 'direct';
    $scope.RestoreMode = 'overwrite';
    $scope.passphrase = "";

    var filesetsBuilt = {};
    var filesetsRepaired = {};
    var filesetStamps = {};
    var inProgress = {};
    var dirsep = $scope.SystemInfo.DirectorySeparator || '/';

    $scope.filesetStamps = filesetStamps;
    $scope.treedata = {};
    $scope.Selected = [];

    function createGroupLabel(dt) {
        var dateStamp = function(a) { return a.getFullYear() * 10000 + a.getMonth() * 100 + a.getDate(); }

        var now       = new Date();
        var today     = dateStamp(now);
        var yesterday = dateStamp(new Date(new Date().setDate(now.getDate()   - 1)));
        var week      = dateStamp(new Date(new Date().setDate(now.getDate()   - 7)));
        var thismonth = dateStamp(new Date(new Date().setMonth(now.getMonth() - 1)));
        var lastmonth = dateStamp(new Date(new Date().setMonth(now.getMonth() - 2)));

        var dateBuckets = [
            {text: gettextCatalog.getString('Today'), stamp: today},
            {text: gettextCatalog.getString('Yesterday'), stamp: yesterday},
            {text: gettextCatalog.getString('This week'), stamp: week},
            {text: gettextCatalog.getString('This month'), stamp: thismonth},
            {text: gettextCatalog.getString('Last month'), stamp: lastmonth}
        ];

        var stamp = dateStamp(dt);

        for(var t in dateBuckets)
            if (stamp >= dateBuckets[t].stamp)
                return dateBuckets[t].text;

        return dt.getFullYear() + '';
    };

    $scope.parseBackupTimesData = function() {
        for(var n in filesetStamps)
            delete filesetStamps[n];

        for(var n in $scope.Filesets) {
            var item = $scope.Filesets[n];
            item.DisplayLabel = item.Version + ': ' + AppUtils.toDisplayDateAndTime(AppUtils.parseDate(item.Time));
            if (item.IsFullBackup === 0) {
                item.DisplayLabel = item.DisplayLabel + ' (partial)';
            }
            item.GroupLabel = n == 0 ? gettextCatalog.getString('Latest') : createGroupLabel(AppUtils.parseDate(item.Time));

            filesetStamps[item.Version + ''] = item.Time;
        }

        $scope.RestoreVersion = 0;
    };

    $scope.fetchBackupTimes = function() {
        $scope.connecting = true;
        $scope.ConnectionProgress = gettextCatalog.getString('Getting file versions …');

        var qp = '';
        if ($scope.IsBackupTemporary)
            qp += '?from-remote-only=true';

        AppService.get('/backup/' + $scope.BackupID + '/filesets' + qp).then(
            function(resp) {
                $scope.connecting = false;
                $scope.ConnectionProgress = '';
                $scope.Filesets = resp.data;
                $scope.parseBackupTimesData();
                $scope.fetchPathInformation();
            },

            function(resp) {
                var message = resp.statusText;
                if (resp.data != null && resp.data.Message != null)
                    message = resp.data.Message;

                $scope.connecting = false;
                $scope.ConnectionProgress = '';
                DialogService.dialog(gettextCatalog.getString('Error'), gettextCatalog.getString('Failed to connect: {{message}}', { message: message }));
            }
        );
    };

    $scope.fetchPathInformation = function() {
        var version = $scope.RestoreVersion + '';

        if ($scope.connecting)
            return;

        if (inProgress[version] || $scope.restore_step != 0)
            return;

        if (!$scope.IsBackupTemporary && $scope.temporaryDB == null) {
            // TODO: Register a temporary db here
        }

        function handleError(resp) {
            delete inProgress[version];
            $scope.connecting = false;
            $scope.ConnectionProgress = '';

            var message = resp.statusText;
            if (resp.data != null && resp.data.Message != null)
                message = resp.data.Message;
            DialogService.dialog(gettextCatalog.getString('Error'), gettextCatalog.getString('Failed to fetch path information: {{message}}', { message: message }));
        };

        if (filesetsBuilt[version] == null) {
            if ($scope.IsBackupTemporary && filesetsRepaired[version] == null) {
                $scope.connecting = true;
                $scope.ConnectionProgress = gettextCatalog.getString('Fetching path information …');
                inProgress[version] = true;

                AppService.post('/backup/' + $scope.BackupID + '/repairupdate', { 'only-paths': true, 'time': filesetStamps[version + '']}).then(
                    function(resp) {

                        var taskid = resp.data.ID;
                        inProgress[version] = taskid;
                        $scope.taskid = taskid;

                        ServerStatus.callWhenTaskCompletes(taskid, function() {

                            AppService.get('/task/' + taskid).then(function(resp) {
                                delete inProgress[version];
                                $scope.connecting = false;
                                $scope.ConnectionProgress = '';

                                if (resp.data.Status == 'Completed')
                                {
                                    filesetsRepaired[version] = true;
                                    $scope.fetchPathInformation();
                                }
                                else
                                {
                                    DialogService.dialog(gettextCatalog.getString('Error'), gettextCatalog.getString('Failed to fetch path information: {{message}}', { message: resp.data.ErrorMessage }));
                                }

                            }, handleError);

                        });
                    }, handleError);

            } else {
                var stamp = filesetStamps[version];
                // In case the times are not loaded yet
                if (stamp == null)
                    return;

                $scope.connecting = true;
                $scope.ConnectionProgress = gettextCatalog.getString('Fetching path information …');
                inProgress[version] = true;

                AppService.get('/backup/' + $scope.BackupID + '/files/*?prefix-only=true&folder-contents=false&time=' + encodeURIComponent(stamp)).then(
                    function(resp) {
                        delete inProgress[version];
                        $scope.connecting = false;
                        $scope.ConnectionProgress = '';

                        filesetsBuilt[version] = resp.data.Files;
                        $scope.Paths = filesetsBuilt[version];

                        dirsep = resp.data.Files[0].Path[0] == '/' ? '/' : '\\';

                    }, handleError);
            }
        } else {
            $scope.Paths = filesetsBuilt[version];
        }
    };

    $scope.$watch('RestoreVersion', function() { $scope.fetchPathInformation(); });

    $scope.$watch('RestorePath', function() {
        if (($scope.RestorePath || '').trim().length == 0)
            $scope.RestoreLocation = 'direct';
        else
            $scope.RestoreLocation = 'custom';
    });

    $scope.onClickNext = function() {
        var results =  $scope.Selected;
        if (results.length == 0) {
            DialogService.dialog(gettextCatalog.getString('No items selected'), gettextCatalog.getString('No items to restore, please select one or more items'));
        } else {
            $scope.restore_step = 1;
        }
    };

    $scope.onClickBack = function() {
        $location.path('/restoredirect')
    };


    $scope.clearSearch = function() {
        $scope.InSearchMode = false;
        $scope.fetchPathInformation();
    };

    $scope.doSearch = function() {
        if ($scope.Searching || $scope.restore_step != 0)
            return;

        if (($scope.SearchFilter || '').trim().length == 0) {
            $scope.clearSearch();
            return;
        }

        $scope.Searching = true;

        var version = $scope.RestoreVersion + '';
        var stamp = filesetStamps[version];

        AppService.get('/backup/' + $scope.BackupID + '/files/*' + $scope.SearchFilter + '*?prefix-only=false&time=' + encodeURIComponent(stamp) + '&filter=*' + encodeURIComponent($scope.SearchFilter) + '*').then(
            function(resp) {
                $scope.Searching = false;
                var searchNodes = [];

                function compareablePath(path) {
                    return $scope.SystemInfo.CaseSensitiveFilesystem ? path : path.toLowerCase();
                };

                for(var i in filesetsBuilt[version])
                    searchNodes[i] = { Path: filesetsBuilt[version][i].Path };

                var files = resp.data.Files;
                for(var i in files) {
                    var p = files[i].Path;
                    var cp = compareablePath(p);
                    var isdir = p[p.length - 1] == dirsep;

                    for(var j in searchNodes) {
                        var sn = searchNodes[j];
                        if (cp.indexOf(compareablePath(sn.Path)) == 0) {
                            var curpath = sn.Path;
                            var parts = p.substr(sn.Path.length).split(dirsep);
                            var col = sn;

                            for(var k in parts) {
                                var found = false;
                                curpath += parts[k];
                                if (isdir || k != parts.length - 1)
                                    curpath += dirsep;

                                if (!col.Children)
                                    col.Children = [];

                                for(var m in col.Children) {
                                    if (compareablePath(col.Children[m].Path) == compareablePath(curpath)) {
                                        found = true;
                                        col = col.Children[m];
                                    }
                                }

                                if (!found) {
                                    var n = { Path: curpath, expanded: true };
                                    if (!isdir && k == parts.length - 1) {
                                        n.iconCls = 'x-tree-icon-leaf';
                                        n.leaf = true;
                                    }

                                    col.Children.push(n);
                                    col = n;
                                }
                            }

                            break;
                        }
                    }
                }

                $scope.Paths = searchNodes;
                $scope.InSearchMode = true;
            },
            function(resp) {
                $scope.Searching = false;
                var message = resp.statusText;
                if (resp.data != null && resp.data.Message != null)
                    message = resp.data.Message;

                $scope.connecting = false;
                $scope.ConnectionProgress = '';
                DialogService.dialog(gettextCatalog.getString('Error'), gettextCatalog.getString('Failed to connect: {{message}}', { message: message }));
            }
        );
    };

    $scope.onStartRestore = function() {
        if ($scope.RestoreLocation == 'custom' && ($scope.RestorePath || '').trim().length == 0)
        {
            DialogService.alert(gettextCatalog.getString('You have chosen to restore to a new location, but not entered one'));
            return;
        }

        if ($scope.RestoreLocation != 'custom' && dirsep != $scope.SystemInfo.DirectorySeparator)
        {
            DialogService.confirm(gettextCatalog.getString('This backup was created on another operating system. Restoring files without specifying a destination folder can cause files to be restored in unexpected places. Are you sure you want to continue without choosing a destination folder?'), function(ix) {
                if (ix == 1)
                    $scope.onStartRestoreProcess();
            });
        }
        else
        {
            $scope.onStartRestoreProcess();
        }
    }

    $scope.onStartRestoreProcess = function() {

        var version = $scope.RestoreVersion + '';
        var stamp = filesetStamps[version];

        $scope.restore_step = 2;

        function handleError(resp) {
            var message = resp.statusText;
            if (resp.data != null && resp.data.Message != null)
                message = resp.data.Message;

            $scope.restore_step = 1;
            $scope.connecting = false;
            $scope.ConnectionProgress = '';
            DialogService.dialog(gettextCatalog.getString('Error'), gettextCatalog.getString('Failed to connect: {{message}}', { message: message }));
        };

        var p = {
            'time': stamp,
            'restore-path': $scope.RestoreLocation == 'custom' ? $scope.RestorePath : null,
            'overwrite': $scope.RestoreMode == 'overwrite',
            'permissions': $scope.RestorePermissions == null ? false : $scope.RestorePermissions,
            'passphrase' : $scope.passphrase
        };

        var paths = [];
        for(var n in $scope.Selected) {
            var item = $scope.Selected[n];
            if (item.substr(item.length - 1) == dirsep) {
                // To support the possibility of encountering paths
                // with literal wildcard characters, but also being
                // able to add the globbing "*" suffix, use a regular
                // expression filter

                // Escape regular expression metacharacters
                var itemRegex = item.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
                // Add "globbing" suffix
                paths.push('[' + itemRegex  + '.*]');
            } else {
                // To support the possibility of encountering paths
                // with literal wildcard characters, create a literal
                // filter
                paths.push('@' + item);
            }
        }

        if (paths.length > 0)
            p.paths = JSON.stringify(paths);

        if ($scope.IsBackupTemporary) {

            $scope.connecting = true;
            $scope.ConnectionProgress = gettextCatalog.getString('Creating temporary backup …');

            AppService.post('/backup/' + $scope.BackupID + '/copytotemp').then(function(resp) {
                var backupid = resp.data.ID;

                $scope.ConnectionProgress = gettextCatalog.getString('Building partial temporary database …');
                AppService.post('/backup/' + backupid + '/repair', p).then(function(resp) {
                    var taskid = $scope.taskid = resp.data.ID;
                    ServerStatus.callWhenTaskCompletes(taskid, function() {
                        AppService.get('/task/' + taskid).then(function(resp) {

                            $scope.ConnectionProgress = gettextCatalog.getString('Starting the restore process …');
                            if (resp.data.Status == 'Completed')
                            {
                                AppService.post('/backup/' + backupid + '/restore', p).then(function(resp) {
                                    $scope.ConnectionProgress = gettextCatalog.getString('Restoring files …');
                                    var t2 = $scope.taskid = resp.data.TaskID;
                                    ServerStatus.callWhenTaskCompletes(t2, function() { $scope.onRestoreComplete(t2); });
                                }, handleError);
                            }
                            else
                            {
                                DialogService.dialog(gettextCatalog.getString('Error'), gettextCatalog.getString('Failed to build temporary database: {{message}}', { message: resp.data.ErrorMessage }));
                                $scope.connecting = false;
                                $scope.ConnectionProgress = '';
                                $scope.restore_step = 1;
                            }
                        }, handleError);
                    });
                }, handleError);

            }, handleError);

        } else {
            $scope.connecting = true;
            $scope.ConnectionProgress = gettextCatalog.getString('Starting the restore process …');
            AppService.post('/backup/' + $scope.BackupID + '/restore', p).then(function(resp) {
                $scope.ConnectionProgress = gettextCatalog.getString('Restoring files …');
                var t2 = $scope.taskid = resp.data.TaskID;
                ServerStatus.callWhenTaskCompletes(t2, function() { $scope.onRestoreComplete(t2); });
            }, handleError);
        }
    };

    $scope.onRestoreComplete = function(taskid) {
        AppService.get('/task/' + taskid).then(function(resp) {
            $scope.connecting = false;
            $scope.ConnectionProgress = '';

            if (resp.data.Status == 'Completed')
            {
                $scope.restore_step = 3;
            }
            else
            {
                DialogService.dialog(gettextCatalog.getString('Error'), gettextCatalog.getString('Failed to restore files: {{message}}', { message: resp.data.ErrorMessage }));
            }
        }, function(resp) {
            var message = resp.statusText;
            if (resp.data != null && resp.data.Message != null)
                message = resp.data.Message;

            $scope.restore_step = 1;
            $scope.connecting = false;
            $scope.ConnectionProgress = '';
            DialogService.dialog(gettextCatalog.getString('Error'), gettextCatalog.getString('Failed to connect: {{message}}', { message: message }));
        });
    };

    $scope.onClickComplete = function () {
        $location.path('/');
    };

    $scope.trySetStep = function(pg) {
        if ($scope.restore_step < 2)
            $scope.restore_step = pg;
    };

    $scope.showInputPassphrase = function () {
        if (!this.Backup) {
            return false;
        }

        if (!('IsUnencryptedOrPassphraseStored' in this.Backup)) {
            return false;
        }

        return !this.Backup['IsUnencryptedOrPassphraseStored'];
    }

    $scope.BackupID = $routeParams.backupid;
    $scope.IsBackupTemporary = parseInt($scope.BackupID) != $scope.BackupID;

    if (!$scope.IsBackupTemporary) {
        $scope.$on('backuplistchanged', function() { $scope.Backup = BackupList.lookup[$scope.BackupID]; });
        $scope.Backup = BackupList.lookup[$scope.BackupID];
    }

    // We pass in the filelist through a global variable
    // ... bit ugly, but we do not want to do two remote queries,
    // ... nor do we want to pass the information through the url
    if ($scope.IsBackupTemporary && $rootScope.filesets && $rootScope.filesets[$scope.BackupID]) {
        $scope.Filesets = $rootScope.filesets[$scope.BackupID];
        $scope.parseBackupTimesData();
        $scope.fetchPathInformation();
    } else {
        $scope.fetchBackupTimes();
    }

});
