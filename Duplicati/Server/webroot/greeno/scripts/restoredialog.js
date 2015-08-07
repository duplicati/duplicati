$(document).ready(function() {

    var curpage = 0;
    var backupId = 0;
    var dirSep = '/';
    var pathSep = ':';
    var trees = {};
    var searchdata = {};
    var nodedata = {};

    var manualIncludes = {};
    var searchTrees = {};
    var commonPrefix = [];

    var dbversions = {};
    var isDirectRestore = false;

    var includeMap = null;

    var performRestore = function(tasks) {
        var dlg = $('<div></div>').attr('title', 'Restoring files ...');
        var cancelling = false;
        var currentProgressTask = null;
        var remainingTasks = tasks.length;

        dlg.dialog({
            autoOpen: true,
            modal: true,
            closeOnEscape: false,
            buttons: [
                { text: 'Cancel', click: function(event, ui) {
                    if (confirm('Stop the restore?')) {
                        cancelling = true;
                        if (currentProgressTask != null)
                            APP_DATA.stopTask(currentProgressTask.taskId, true);

                        updatePageNav();
                        dlg.dialog('close');
                        dlg.remove();
                    }
                }}
            ]
        });

        dlg.parent().find('.ui-dialog-titlebar-close').remove().first().remove();

        var pg = $('<div></div>');
        pg.progressbar({ value: false });
        var pgtxt = $('<div></div>');
        pgtxt.text('Sending jobs to server ...');

        dlg.append(pg);
        dlg.append(pgtxt);

        var onAllRestoresCompleted = function() {
            $(document).off('server-progress-updated', serverProgressUpdateMethod);
            $(document).off('server-state-updated', serverStateUpdateMethod);

            curpage = Math.min(2, curpage+1);
            updatePageNav();
            dlg.dialog('close');
            dlg.remove();
        };

        var serverProgressUpdateMethod = function(e, data) {
            if (currentProgressTask == null)
                pgtxt.text('Waiting for restore to begin ...');
            else {
                if (tasks.length == 1)
                    pgtxt.text('Restoring files ...');
                else
                    pgtxt.text('Restoring files (' + (tasks.length - remainingTasks) + ' of ' + tasks.length + ')');
            }

        };

        var serverStateUpdateMethod = function(e, data) {
            var activeTaskId = -1;
            var queuedTasks = [];

            if (cancelling)
                return;

            if (data.ActiveTask != null)
                activeTaskId = data.ActiveTask.Item1;

            if (data.SchedulerQueueIds != null)
                for(var n in data.SchedulerQueueIds)
                    queuedTasks.push(data.SchedulerQueueIds[n].Item1);

            var remaining = 0;
            for(var n in tasks) {

                if (tasks[n].taskId == activeTaskId) {
                    currentProgressTask = tasks[n];
                    remaining++;
                    continue;
                }

                for(var i in queuedTasks)
                    if (queuedTasks[i] == tasks[n].taskId) {
                        remaining++;
                        continue;
                    }
            }

            if (remaining == 0)
                onAllRestoresCompleted();
        };

        var curTask = 0;
        var registerTasks = function() {
            if (curTask == tasks.length) {
                pgtxt.text('Waiting for restore to begin ...');
                $(document).on('server-progress-updated', serverProgressUpdateMethod);
                $(document).on('server-state-updated', serverStateUpdateMethod);
                return;
            }

            var task = tasks[curTask];
            curTask++;

            APP_DATA.callServer(task, function(data) {
                task['taskId'] = data.TaskID;
                registerTasks();
            },
            function(d, s, m) {
                alert('Error: ' + m);
                dlg.dialog('close');
                dlg.remove();
            });

        }

        registerTasks();
    };

    $('#restore-dialog').dialog({
        minWidth: 320, 
        width: $('body').width > 600 ? 320 : 600, 
        minHeight: 480, 
        height: 500, 
        modal: true,
        autoOpen: false,
        closeOnEscape: true,
        buttons: [
            { text: '< Previous', disabled: true, click: function(event, ui) {
                curpage = Math.max(0, curpage-1);
                updatePageNav();

            }},
            { text: 'Next >', click: function(event, ui) {
                if (curpage == 2) {
                     $('#restore-dialog').dialog('close');
                } else if (curpage == 1) {

                    var restorePath = null;

                    var overwrite = $('#restore-overwrite-overwrite').is(':checked');

                    var restore_permissions = $('#restore-metadata-permissions').is(':checked');
                    var skip_metadata = $('#restore-metadata-none').is(':checked');

                    if ($('#restore-overwrite-target-other').is(':checked'))
                        restorePath = $('#restore-target-path').val();

                    var tasks = [];
                    for(var n in includeMap) {
                        var t = {
                            'action': 'restore-files',
                            time: $('#restore-version').val(),
                            'HTTP_METHOD': 'POST',
                            id: backupId,
                            'restore-path': restorePath,
                            'overwrite': overwrite,
                            'permissions': restore_permissions,
                            'skip-metadata': skip_metadata,
                            paths: []
                        }
                        for(var p in includeMap[n]) {
                            if (p.lastIndexOf(dirSep) == p.length -1)
                                t.paths.push(p + '*');
                            else
                                t.paths.push(p);
                        }

                        if (t.paths.length > 0)
                        {
                            t.paths = t.paths.join(pathSep);
                            tasks.push(t);
                        }
                    }

                    if (isDirectRestore) {

                        var backupid = null;

                        var dlg = APP_UTIL.create_modal_task_wait('Creating database', 'Building local database ... ', function(cancelled, taskid) {
                            if (!cancelled)
                                performRestore(tasks);
                        });

                        APP_DATA.callServer({ action: 'copy-backup-to-temp', 'id': backupId }, function(data) {
                            var bkid = data.ID;
                            for(var n in tasks)
                                tasks[n].id = bkid;

                            APP_DATA.callServer({ action: 'send-command', command: 'run-repair', 'id': bkid, 'time':  $('#restore-version').val(), 'paths': tasks[0].paths, 'HTTP_METHOD': 'POST' }, function(data) {
                                dlg.register_updates(data.ID);
                            }, function(a,b,msg) {
                                alert('Failed to connect: ' + msg);
                                dlg.dialog('close');
                                dlg.remove();
                            });                            
                        }, function(a,b,msg) {
                            alert('Failed to connect: ' + msg);
                            dlg.dialog('close');
                            dlg.remove();
                        });                        


                    } else {
                        performRestore(tasks);
                    }
                } else {
                    var els = 0;
                    includeMap = buildIncludeMap();

                    for(var t in includeMap)
                        for(var i in includeMap[t])
                            els++;

                    if (els == 0) {
                        alert('You must select at least one path to restore');
                        return;
                    }

                    curpage = Math.min(2, curpage+1);
                    updatePageNav();

                }
            }}
        ]        
    });

    var dlg_buttons = $('#restore-dialog').parent().find('.ui-dialog-buttonpane').find('.ui-button');
    var updatePageNav = function() {
        if (curpage == 0) {
            $('#restore-files-page').show();
            $('#restore-path-page').hide();
            $('#restore-complete-page').hide();
            dlg_buttons.first().show();
            dlg_buttons.last().button('option', 'label', 'Next >');
        } else if (curpage == 1) {
            $('#restore-files-page').hide();
            $('#restore-path-page').show();
            $('#restore-complete-page').hide();
            dlg_buttons.first().show();
            dlg_buttons.last().button('option', 'label', 'Restore');
        } else {
            $('#restore-files-page').hide();
            $('#restore-path-page').hide();
            $('#restore-complete-page').show();

            dlg_buttons.first().hide();
            dlg_buttons.last().button('option', 'label', 'OK');
        }

        dlg_buttons.first().button('option', 'disabled', curpage == 0);
    };

    $('#restore-search').watermark('Search for files & folders');

    $('#restore-files-page').show();
    $('#restore-path-page').hide();
    $('#restore-complete-page').hide();

    var colorize = function(tree, term, entry) {
        var tr = tree.jstree();
        $(entry || tree).find('a.jstree-anchor').each(function(i, e) {
            var s = $(e);
            var node = tr.get_node(s);
            if (node.text) {
                s.children('.search-match').remove();
                var o = s.children();
                s.html(replace_all_insensitive(node.text, term, '<div class="search-match">$1</div>'));
                s.prepend(o);
            }
        });
    };

    var buildSearchNodes = function(time, data, search) {
        var rootpath = commonPrefix[time];
        if (rootpath != '' && rootpath[rootpath.length - 1] != dirSep)
            rootpath += dirSep;
        var rootnode = createNodeFromData(commonPrefix[time], commonPrefix[time], time, true);
        rootnode.state = rootnode.state || {};
        rootnode.state.opened = true;


        for(var i = 0; i < data.Files.length; i++) {
            var n = rootnode;
            var p = rootpath;
            var sp = data.Files[i].Path;

            if (search && sp.toLowerCase().indexOf(search.toLowerCase()) < 0)
                continue;

            var isDir = sp.substr(sp.length - 1) == dirSep;
            if (isDir)
                sp = sp.substr(0, sp.length - 1);

            var items = sp.substr(rootpath.length).split(dirSep);

            for(var j = 0; j < items.length; j++) {
                p += items[j] + (!isDir && j == items.length - 1 ? '' : dirSep);
                
                var e = null;
                for(var x in n.children)
                    if (n.children[x].filepath == p) {
                        e = n.children[x];
                        break;
                    }

                if (e == null) {
                    e = createNodeFromData(p, commonPrefix[time], time, false);
                    
                    if (j != items.length - 1) {
                        e.state = e.state || {};
                        e.state.opened = true;
                    }

                    if (n.children === false || n.children === true)
                        n.children = [];
                    n.children.push(e);

                }
                n = e;
            }
        }

        return [rootnode];
    }

    var inSearch = false;
    var doSearch = function(time, search) {
        if (inSearch)
            return;

        $('#restore-search-loader').show();
        inSearch = true;

        var stree = searchTrees[time];
        var tree = trees[time];

        var buildSearchTree = function(nodes) {
            var streeel = $('<div></div>');
            searchTrees[time] = streeel;
            $('#restore-files-tree').append(streeel);

            var te = streeel.jstree({
                'plugins' : [ 'checkbox' ],
                'core': { 'data' : nodes }
            });

            colorize(streeel, search);
            streeel.show();
        };

        if (searchdata[time]) {
            var t = searchdata[time];
            for(var p in t) {
                if (search.indexOf(p) == 0) {
                    colorize(tree, search);

                    if (t[p].Files.length == 0) {
                        alert('No results matched the query');
                        tree.show();
                    } else if (stree) {
                        colorize(stree, search);
                        tree.hide();
                        stree.show();
                    } else {
                        buildSearchTree(buildSearchNodes(time, t[p], search));
                        tree.hide();
                    }
                    $('#restore-search-loader').hide();
                    inSearch = false;
                    return;
                }
            }
        }

        if (tree) {
            colorize(tree, search);
            tree.hide();
        }
        if (stree)
            colorize(stree, search);


        if (stree) {
            stree.remove();
            delete searchTrees[time];
        }

        $.ajax({
            'url': APP_CONFIG.server_url,
            headers: {'X-XSRF-Token': APP_DATA.xsrf_token},
            'data': {
                'action': 'search-backup-files',
                'id': backupId,
                'time': time,
                'prefix-only': 'false',
                'filter': '*' + search + '*'
            },
            'dataType': 'json'
        })
        .done(function(data, status, xhr) {
            
            if (!searchdata[time])
                searchdata[time] = {};

            if (!manualIncludes[time])
                manualIncludes[time] = [];

            searchdata[time][search] = data;

            $('#restore-search-loader').hide();
            inSearch = false;

            if (data.Files.length == 0) {
                alert('No results matched the query');
                tree.show();
                return;
            }

            buildSearchTree(buildSearchNodes(time, data));

        })
        .fail(function() {
            $('#restore-search-loader').hide();
            inSearch = false;
            alert('Search failed ...');
            tree.show();
        });
    };

    var createNodeFromData = function(path, prefix, time, rootnode) {
        var disp = path.substr(prefix.length);
        var isFolder = disp.lastIndexOf(dirSep) == disp.length - 1;
        var icon = null;
        var state = null;
        if (isFolder)
            disp = disp.substr(0, disp.length - 1);
        else
            icon = 'icon-file icon-file-' + disp.substr(disp.lastIndexOf('.')+1);

        if (rootnode) {
            disp = APP_DATA.getBackupName(backupId) || disp;
            //state = {opened: true};
        }

        return {
            text: disp,
            filepath: path,
            time: time,
            children: isFolder,
            state: state,
            icon: icon
        };
    };

    var loadNodes = function(node, callback, time) {
        $.ajax({
            'url': APP_CONFIG.server_url,
            'data': {
                'action': 'search-backup-files',
                'id': backupId,
                'time': time,
                'prefix-only': node.id === '#',
                'folder-contents': node.id !== '#',
                'filter': node.id === '#' ? '*' : node.original.filepath,
                'Prefix': node.id === '#' ? '' : node.original.filepath
            },
            headers: {'X-XSRF-Token': APP_DATA.xsrf_token},
            'dataType': 'json'
        })
        .done(function(data, status, xhr) {
            var nodes = [];
            data.Files = data.Files || [];
            for(var i = 0; i < data.Files.length; i++)
                nodes.push(createNodeFromData(data.Files[i].Path, data.Prefix, time, node.id === '#'));

            callback(nodes, data);

            if (node.id === '#')
                commonPrefix[time] = data.Files[0].Path;
        });
    };

    var setupTree = function(time) {
        var treeel = trees[time];

        if (!trees[time]) {
            var treeel = $('<div></div>');
            trees[time] = treeel;
            $('#restore-files-tree').append(treeel);

            treeel.jstree({
                'core': {
                    'data': function(node, callback) {
                        if (nodedata[node.id]) {
                            callback(nodedata[node.id]);
                            colorize(treeel, $('#restore-search').val());
                        } else {
                            loadNodes(node, function(nodes, data) { 
                                callback(nodes);
                                if (data.Prefix == '')
                                    treeel.jstree("open_node", treeel.find('li').first());
                                colorize(treeel, $('#restore-search').val());
                            }, 
                            time); 
                        }
                    }
                },
                'plugins' : [ 'checkbox' ]
            });
        }

        for(var t in trees)
            if (t != time)
                trees[t].hide();
            else {
                trees[t].show();
            }
    };

    var buildIncludeMap = function() {
        var time = $('#restore-version').val();
        var m = {};
        //strees = strees || trees;
        var strees = [ trees[time] ];
        var partialfolders = false;
        if (searchTrees[time] && searchTrees[time].is(':visible')) {
            partialfolders = true;
            strees = [ searchTrees[time] ];
        }

        for(var t in strees) {
            m[t] = {};
            var tr = strees[t].jstree();

            var roots = strees[t].children('ul').children('li');
            var follow = [];

            roots.each(function(i,e) {
                follow.push(tr.get_node(e));
            });

            while(follow.length > 0) {
                var n = follow.pop();
                if (n.state.selected && n.original.filepath) {
                    // If we have partial folder matches, 
                    // we only include the leaves
                    if (partialfolders) {
                        if (!n.children || n.children.length == 0) {
                            m[t][n.original.filepath] = 1;
                        } else {
                            // Non-leaf node, recurse it
                            for(var c in n.children)
                                follow.push(tr.get_node(n.children[c]));                            
                        }

                    } else {
                        m[t][n.original.filepath] = 1;
                    }
                } else {
                    //TODO: This traverses the entire tree,
                    // we could choose only those with indeterminate,
                    // but that state only exists in the DOM which is
                    // removed when collapsed
                    for(var c in n.children)
                        follow.push(tr.get_node(n.children[c]));
                }
            }
        }

        return m;
    };

    $('#restore-dialog').on('setup-dialog', function(e, data) {
        backupId = data.id;
        isDirectRestore = data.isDirectRestore;
        trees = { };
        searchdata = { };
        dbversions = { };

        $('#restore-files-tree').empty();
        $('#restore-search-loader').hide();
        $('#restore-form').each(function(i, e) { e.reset(); });
        $('#restore-overwrite-overwrite').each(function(i, e) { e.checked = true; });
        $('#restore-overwrite-target-original').each(function(i, e) { e.checked = true; });
        $('#restore-metadata-timestamps').each(function(i, e) { e.checked = true; });

        curpage = 0; 
        updatePageNav();

        APP_DATA.getServerConfig(function(serverdata) {

            // TODO: Should be the backup data versions, not the host OS
            dirSep = serverdata.DirectorySeparator;
            pathSep = serverdata.PathSeparator;

            APP_DATA.callServer({ 'action': 'list-backup-sets', id: backupId, 'from-remote-only': isDirectRestore }, function(data, success, message) {
                    $('#restore-version').empty();

                    if (data == null || data.length == 0) {
                        alert('Failed to get list of backup times');
                        $('#restore-dialog').dialog('close');
                    }

                    for(var n in data)
                        dbversions[data[n].Time] = isDirectRestore ? n : true;
                    dbversions[data[0].Time] = true;

                    var latest_group = $('<optgroup></optgroup>').attr('label', 'Newest - ' + $.timeago(data[0].Time));
                    latest_group.append($("<option></option>").attr("value", data[0].Time).text($.toDisplayDateAndTime($.parseDate(data[0].Time))));
                    $('#restore-version').append(latest_group);

                    var dateStamp = function(a) { return a.getFullYear() * 10000 + a.getMonth() * 100 + a.getDate(); }
                    var now = new Date();
                    var today = dateStamp(now);
                    var yesterday = dateStamp(new Date(new Date().setDate(now.getDate() - 1)));
                    var week = dateStamp(new Date(new Date().setDate(now.getDate() - 7)));
                    var thismonth = dateStamp(new Date(new Date().setMonth(now.getMonth() - 1)));
                    var lastmonth = dateStamp(new Date(new Date().setMonth(now.getMonth() - 2)));

                    var dateBuckets = [
                        {text:'Today', stamp: today, items: []}, 
                        {text: 'Yesterday', stamp: yesterday, items: []}, 
                        {text: 'This week', stamp: week, items: []}, 
                        {text: 'This month', stamp: thismonth, items: []},
                        {text: 'Last month', stamp: lastmonth, items: []}
                    ];

                    var yearBuckets = { };

                    for(var i in data) {
                        if (i == '0')
                            continue;
                        var dt = $.parseDate(data[i].Time);
                        var stamp = dateStamp(dt);
                        var inserted = false;

                        for(var t in dateBuckets) {
                            if (stamp >= dateBuckets[t].stamp) {
                                dateBuckets[t].items.push(data[i])
                                inserted = true;
                                break;
                            }
                        }

                        if (!inserted) {
                            var y = dt.getFullYear() + '';
                            if (yearBuckets[y] == null) {
                                yearBuckets[y] = {text: y, stamp: dateStamp(new Date(dt.getFullYear(), 0, 1)), items: []};
                                dateBuckets.push(yearBuckets[y]);
                            }
                            yearBuckets[y].items.push(data[i]);
                        }
                    }


                    for(var n in dateBuckets) {
                        var e = dateBuckets[n];
                        if (e.items.length == 0)
                            continue;

                        var group = $('<optgroup></optgroup>').attr('label', e.text);
                        for(var d in e.items)
                            group.append($("<option></option>").attr("value", e.items[d].Time).text($.toDisplayDateAndTime($.parseDate(e.items[d].Time))));

                        $('#restore-version').append(group);

                    }

                    $('#restore-version').trigger('change');

                }, function(data, success, message) {
                    alert('Failed to get list of backup times:\n' + message);
                    $('#restore-dialog').dialog('close');
                });
        }, function() {
            alert('Failed to get server config');
            $('#restore-dialog').dialog('close');
        });
    });

    var doQuickSearch = function(search) {
        var search = $('#restore-search').val();
        var time = $('#restore-version').val();
        if (searchdata[time]) {
            for(var k in searchdata[time]) {
                if (search.indexOf(k) == 0) {
                    colorize(trees[time], search);
                    return true;
                }
            }
        }

        return false;
    };

    $('#restore-search').keypress(function(e) {
        var time = $('#restore-version').val();
        if (e.which == 13 && $('#restore-search').val().trim() != '')
            doSearch(time, $('#restore-search').val());
        else if ($('#restore-search').val().trim() != '') {
            colorize(trees[time], $('#restore-search').val());
            if (searchTrees[time])
                colorize(searchTrees[time], $('#restore-search').val());

        }
    });

    $('#restore-search').keyup(function(e) {
        var time = $('#restore-version').val();
        colorize(trees[time], $('#restore-search').val());
        if (searchTrees[time])
            colorize(searchTrees[time], $('#restore-search').val());
    });

    $('#restore-search').change(function(e) {
        var time = $('#restore-version').val();
        colorize(trees[time], $('#restore-search').val());
        if (searchTrees[time])
            colorize(searchTrees[time], $('#restore-search').val());
    });

    $('#restore-search').on('search', function(e) {
        var time = $('#restore-version').val();
        if ($('#restore-search').val() == '') {
            trees[time].show();
            if (searchTrees[time]) {
                searchTrees[time].remove();
                delete searchTrees[time];
            }
            colorize(trees[time], $('#restore-search').val());
        } else
            doSearch(time, $('#restore-search').val());
    });

    $('#restore-version').change(function() {
        $('#restore-search').val('');

        var selectedversion = $('#restore-version').val();

        if (dbversions[selectedversion] !== true) {
            // Need to patch the db with data

            var dlg = APP_UTIL.create_modal_task_wait('Updating database', 'Updating local database ... ', function(cancelled, taskid) {
                if (!cancelled) {
                    dbversions[selectedversion] = true;
                    setupTree(selectedversion);
                }
            });

            APP_DATA.callServer({ action: 'send-command', command: 'run-repair-update', 'only-paths': true, 'id': backupId, 'time': selectedversion }, function(data) {
                dlg.register_updates(data.ID);
            }, function(a,b,msg) {
                alert('Failed to connect: ' + msg);
                dlg.dialog('close');
                dlg.remove();
            });           
        } else {
            setupTree(selectedversion);
        }
    });
    
    $('#restore-target-path').keypress(function() { $('#restore-overwrite-target-other').each(function(i,e) { e.checked = true; }); });
    $('#restore-target-path').change(function() { $('#restore-overwrite-target-other').each(function(i,e) { e.checked = true; }); });

    $('#restore-overwrite-target-other').click(function() { $('#restore-target-path-browse').trigger('click'); } );

    $('#restore-target-path-browse').click(function(e) {
        $.browseForFolder({
            title: 'Select restore folder',
            resolvePath: true,
            multiSelect: false,
            callback: function(path, display) {
                $('#restore-target-path').val(path);
                $('#restore-overwrite-target-other').each(function(i, e) { e.checked = true; });
            }
        });
    });

    $('#restore-direct-uri').watermark('webdavs://user:pass@example.com:995/backup?option=true');
    $('#restore-direct-encryption-password').watermark('Enter backup passphrase, if any');
    $('#restore-direct-advanced-options').watermark('Enter one option per line in command-line format, eg. --prefix=backup');

    $('#restore-direct-uri-label').click(function() {
        $('#connection-uri-dialog').dialog('open');
        $('#connection-uri-dialog').trigger('setup-dialog', $('#restore-direct-uri'));
    });

    $('#restore-direct-dialog').dialog({
        minWidth: 320, 
        width: $('body').width > 600 ? 320 : 600, 
        minHeight: 480, 
        height: 500, 
        modal: true,
        autoOpen: false,
        closeOnEscape: true,
        buttons: [
            { text: 'Cancel', click: function(event, ui) {
                $(this).dialog('close');
            }},
            { text: 'Connect', click: function(event, ui) {
                var uri = $('#restore-direct-uri').val();
                var passphrase = $('#restore-direct-encryption-password').val();

                if (uri == null || uri.trim().length == 1) {
                    alert('You must supply a connection URI');
                    return;
                }

                var options = {};
                if (passphrase != null && passphrase.trim().length > 0)
                    options['--passphrase'] = passphrase;
                else
                    options['--no-encryption'] = 'true';

                if (!EDIT_URI.parse_extra_options($('#restore-direct-advanced-options'), options))
                    return;

                var settings = [];
                for(var k in options)
                    settings.push({'Name': k, 'Value': options[k]});

                var item = {
                    'Schedule': null,
                    'Backup': {
                        'TargetURL': uri,
                        'Settings': settings
                    }
                };

                var self = this;
                var backupid = null;

                var dlg = APP_UTIL.create_modal_task_wait('Creating database', 'Building local database ... ', function(cancelled, taskid) {
                    if (!cancelled) {
                        APP_DATA.restoreBackup(backupid, true);
                        $(self).dialog('close');
                    }
                });

                APP_DATA.testConnection(uri, function() {
                    APP_DATA.addBackup(item, function(data) {
                        backupid = data.ID;

                        APP_DATA.callServer({ action: 'send-command', command: 'run-repair', 'only-paths': true, 'id': backupid, 'version': 0 }, function(data) {
                            dlg.register_updates(data.ID);

                        }, function(a,b,msg) {
                            alert('Failed to connect: ' + msg);
                            dlg.dialog('close');
                            dlg.remove();
                        });                            
                    }, function(a,b,msg) {
                        alert('Failed to create a backup set: ' + msg);
                        dlg.dialog('close');
                        dlg.remove();
                    }, {'temporary': true});
                }, function(a,b,msg) {
                    alert('Failed to connect: ' + msg);
                    dlg.dialog('close');
                    dlg.remove();
                });

            }}
        ]
    });

    $('#restore-direct-options-dialog').dialog({
        minWidth: 320,
        width: $('body').width > 600 ? 320 : 600,
        minHeight: 480,
        height: 500,
        modal: true,
        autoOpen: false,
        closeOnEscape: true,
        buttons: [
            { text: 'Close', disabled: false, click: function(event, ui) {
                $(this).dialog('close');
            }}
        ]
    });

    $('#restore-direct-options-link').click(function() {
        APP_DATA.getServerConfig(function(data) {
            $('#restore-direct-options-dialog').dialog('open');

            var baseOpts = data.Options;

            for(var n in data.BackendModules)
                baseOpts = baseOpts.concat(data.BackendModules[n].Options);

            for(var n in data.CompressionModules)
                baseOpts = baseOpts.concat(data.CompressionModules[n].Options);

            for(var n in data.EncryptionModules)
                baseOpts = baseOpts.concat(data.EncryptionModules[n].Options);

            for(var n in data.GenericModules)
                baseOpts = baseOpts.concat(data.GenericModules[n].Options);


            $('#restore-direct-options-dialog').trigger('configure', { Options: baseOpts, callback: function(id) {
                $('#restore-direct-options-dialog').dialog('close');

                var txt = $('#restore-direct-advanced-options').val().trim();
                if (txt.length > 0)
                    txt += '\n';

                var defaultvalue = '';
                for(var o in data.Options)
                    if (data.Options[o].Name == id) {
                        defaultvalue = data.Options[o].DefaultValue;
                        break;
                    }


                txt += '--' + id + '=' + defaultvalue;
                $('#restore-direct-advanced-options').val('').val(txt);
                $('#restore-direct-advanced-options').focus();

            }});
        }, function() {
        });
    });    

    $('#restore-direct-options-dialog').on('configure', function(e, data) {
        $('#restore-direct-options-dialog').empty();

        var s = data.Options.sort(function(a, b){
            if (a == null)
                return -1;
            if (b == null)
                return 1;
            if (a == null && b == null)
                return 0;

            if(a.Name < b.Name) return -1;
            if(a.Name > b.Name) return 1;
            return 0;
        });

        //Fill with jQuery template
        $.tmpl($('#backup-option-template'), s).prependTo($('#restore-direct-options-dialog'));
        $('#restore-direct-options-dialog').find('.backup-option-link').click(function(e) {
            data.callback(e.target.id);
        });

        $('#restore-direct-options-dialog').find('.backup-option-long').each(function(i, e) {
            $(e).html(nl2br($(e).html()));
        });
    });    

   
});