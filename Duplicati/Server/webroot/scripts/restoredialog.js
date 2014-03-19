$(document).ready(function() {

    var curpage = 0;
    var backupId = 0;
    var dirSep = '/';
    var pathSep = ':';
    var trees = { };
    var searchdata = {};
    var nodedata = {};
    var missingfiles = null;
    var missingfiles_search = null;

    var performRestore = function(tasks) {
        var dlg = $('<div></div>').attr('title', 'Restoring files ...');
        dlg.dialog({
            autoOpen: true,
            modal: true,
            closeOnEscape: false
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

        var currentProgressTask = null;
        var remainingTasks = tasks.length;

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
            task['action'] = 'restore-files';
            task['HTTP_METHOD'] = 'POST';
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

                    if ($('#restore-overwrite-target-other').is(':checked'))
                        restorePath = $('#restore-target-path').val();

                    var includes = buildIncludeMap();

                    var tasks = [];
                    for(var n in includes) {
                        var t = {
                            time: n,
                            id: backupId,
                            'restore-path': restorePath,
                            'overwrite': overwrite,
                            paths: []
                        }
                        for(var p in includes[n]) {
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

                    performRestore(tasks);
                } else {
                    var els = 0;
                    var includes = buildIncludeMap();

                    for(var t in includes)
                        for(var i in includes[t])
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
                s.html(replace_insensitive(node.text, term, '<div class="search-match">$1</div>'));
                s.prepend(o);
            }
        });
    };

    var getRootPrefixes = function(tree) {
        var roots = tree.children('ul').children('li');
        var prefixes = [];
        roots.each(function(i, e) {
            var node = tr.get_node(e);
            prefixes.push(node.original.filepath);
        });

        return prefixes;
    };

    var getRootNode = function(tree, path) {
        var roots = tree.children('ul').children('li');
        var tr = tree.jstree();
        var root = null;

        roots.each(function(i, e) {
            var node = tr.get_node(e);
            if (node && path.indexOf(node.original.filepath) == 0)
                root = e;
        });

        return root;
    };

    var expandNodes = function(tree, path, callback) {
        var root = getRootNode(tree, path);

        if (root != null) {
            var tr = tree.jstree();
            var node = tr.get_node(root);
            var prefix = node.original.filepath;
            if (prefix.lastIndexOf(dirSep) != prefix.length - 1)
                prefix += dirSep;
            var remainder = path.substr(prefix.length);
            var parts = remainder.split(dirSep);
            var rebuilt = prefix;
            for(var i in parts) {
                var p = parts[i];
                if (p == '')
                    continue;

                var selfprefix = rebuilt;
                rebuilt += p;
                if (parseInt(i) != parts.length - 1)
                    rebuilt += dirSep;

                var next = null;

                if ((node.children === undefined || (node.children !== false && node.children.length == 0)) && callback)
                    callback(node, selfprefix, p);

                for(var j in node.children) {
                    var c = tr.get_node(node.children[j]);
                    if (c.original.filepath == rebuilt) {
                        tr.open_node(node);
                        next = c;
                        break;
                    }
                }

                if (next == null)
                    return {
                        path: rebuilt,
                        node: node
                    }

                node = next;
            }

        }

        return null;
    };

    var prepareFolderData = function(data, time) {
        var tree = trees[time];
        var roots = getRootPrefixes(tree);

        if (!nodedata[time])
            nodedata[time] = {};

        var nd = nodedata[time];

        for(var n in data) {
            var p = data[n].Path;
            var root = null;
            for(var r in roots)
                if (p.indexOf(roots[r]) == 0) {
                    root = roots[r];
                    break;
                }

            if (root == null)
                continue;




            //createNodeFromData;
        }
    };

    var loadSearchIntermediates = function(missing, files, time, search) {
        var filter = [];
        for(var n in missing)
            filter.push(n + '*');

        filter = filter.join(pathSep);

        $.ajax({
            'url': APP_CONFIG.server_url,
            'data': {
                'action': 'search-backup-files',
                'id': backupId,
                'time': time,
                'prefix-only': 'false',
                'filter': filter
            },
            'dataType': 'json'
        })
        .done(function(data, status, xhr) {

            var tree = trees[time];

            for(var f in files)
                expandNodes(tree, files[f].Path, function(node, p, path) {
                    var c = [];

                    if (p.lastIndexOf(dirSep) != p.length - 1)
                        p += dirSep;

                    for(var f in data.Files) {
                        var rp = data.Files[f].Path;
                        if (rp != p && rp.indexOf(p) == 0) {
                            var np = rp.substr(p.length);
                            var lix = np.lastIndexOf(dirSep);
                            if (lix == np.length - 1)
                                lix = np.substr(0, np.length - 1).lastIndexOf(dirSep);
                            if (lix < 0)
                                c.push(createNodeFromData(rp, p, time));
                        }

                    }

                    if (c.length > 0)
                        node.children = c;
                });

            inSearch = false;
            $('#restore-search-loader').hide();
            colorize(tree, search);
        })
        .fail(function() {
            $('#restore-search-loader').hide();
            inSearch = false;
            alert('Search failed ...');
        });
    };

    var expandAndLoad = function(tree) {
        if (missingfiles == null)
            return;

        var missing = {};
        var mf = [];
        var anymissing = false;

        for(var f in missingfiles) {
            var p = expandNodes(tree, missingfiles[f]);
            if (p) {
                missing[p.path] = p.node;
                mf.push(missingfiles[f]);
                anymissing = true;
            }
        }

        if (missingfiles_search)
            colorize(tree, missingfiles_search);
        if (!anymissing) {
            inSearch = false;
            $('#restore-search-loader').hide();
            missingfiles = null;
            missingfiles_search = null;
        } else {
            missingfiles = mf;
            var tr = tree.jstree();
            for(var m in missing)
                tr.open_node(missing[m]);
        }
    };

    var inSearch = false;
    var doSearch = function(time, search) {
        if (inSearch)
            return;

        $('#restore-search-loader').show();
        inSearch = true;
        var tree = trees[time];
        var tr = tree.jstree();
        colorize(tree, search);

        if (searchdata[time]) {
            var t = searchdata[time];
            for(var p in t) {
                if (search.indexOf(p) == 0) {
                    colorize(tree, search);
                    inSearch = false;
                    return;
                }
            }
        }

        $.ajax({
            'url': APP_CONFIG.server_url,
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

            searchdata[time][search] = data;

            missingfiles = [];
            missingfiles_search = search;
            for(var f in data.Files)
                missingfiles.push(data.Files[f].Path);

            expandAndLoad(tree, search);

        })
        .fail(function() {
            $('#restore-search-loader').hide();
            inSearch = false;
            alert('Search failed ...');
        });
    };

    var createNodeFromData = function(path, prefix, time) {
        var disp = path.substr(prefix.length);
        var isFolder = disp.lastIndexOf(dirSep) == disp.length - 1;
        var icon = null;
        var state = null;
        if (isFolder)
            disp = disp.substr(0, disp.length - 1);
        else
            icon = 'icon-file icon-file-' + disp.substr(disp.lastIndexOf('.')+1);

        if (prefix == '') {
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
                'filter': node.id === '#' ? '*' : '[' + node.original.filepath + '[^\\' + dirSep + ']+\\' + dirSep + '?]',
                'Prefix': node.id === '#' ? '' : node.original.filepath
            },
            'dataType': 'json'
        })
        .done(function(data, status, xhr) {
            var nodes = [];
            data.Files = data.Files || [];
            for(var i = 0; i < data.Files.length; i++) {
                var o = data.Files[i];
                nodes.push(createNodeFromData(data.Files[i].Path, data.Prefix, time));
            }

            callback(nodes, data);
            expandAndLoad(trees[time]);
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
                        } else {
                            loadNodes(node, function(nodes, data) { 
                                callback(nodes);
                                if (data.Prefix == '')
                                    treeel.jstree("open_node", treeel.find('li').first());
                            }, 
                            time); 
                        }
                    }
                },
                'plugins' : [ 'wholerow', "checkbox" ]
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
        var m = {};

        for(var t in trees) {
            m[t] = {};
            var tr = trees[t].jstree();

            trees[t].find('.jstree-clicked').each(function(i, e) {
                var p = tr.get_node(e).original.filepath;
                if (p)
                    m[t][p] = 1;
            });
        }

        return m;
    };

    $('#restore-dialog').on('setup-data', function(e, id) {
        backupId = id;
        trees = { };
        searchdata = { };
        $('#restore-files-tree').empty();
        $('#restore-search-loader').hide();
        $('#restore-form').each(function(i, e) { e.reset(); });
        $('#restore-overwrite-overwrite').each(function(i, e) { e.checked = true; });
        $('#restore-overwrite-target-original').each(function(i, e) { e.checked = true; });

        curpage = 0; 
        updatePageNav();

        APP_DATA.getServerConfig(function(serverdata) {

            dirSep = serverdata.DirectorySeparator;
            pathSep = serverdata.PathSeparator;

            APP_DATA.callServer({ 'action': 'list-backup-sets', id: id }, function(data, success, message) {
                    $('#restore-version').empty();

                    if (data == null || data.length == 0) {
                        alert('Failed to get list of backup times');
                        $('#restore-dialog').dialog('close');
                    }

                    $('#restore-version').append($("<option></option>").attr("value", data[0].Time).text('Latest - ' + $.timeago(data[0].Time)));
                    for(var i in data)
                        if (i != '0')
                            $('#restore-version').append($("<option></option>").attr("value", data[i].Time).text($.timeago(data[i].Time)));

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
        if (e.which == 13 && $('#restore-search').val().trim() != '')
            doSearch($('#restore-version').val(), $('#restore-search').val());
        else if ($('#restore-search').val().trim() != '')
            colorize(trees[$('#restore-version').val()], $('#restore-search').val());
    });

    $('#restore-search').keyup(function(e) {
        colorize(trees[$('#restore-version').val()], $('#restore-search').val());
    });

    $('#restore-search').change(function(e) {
        colorize(trees[$('#restore-version').val()], $('#restore-search').val());
    });

    $('#restore-search').on('search', function(e) {
        if ($('#restore-search').val() == '')
            colorize(trees[$('#restore-version').val()], $('#restore-search').val());
        else
            doSearch($('#restore-search').val(), $('#restore-search').val());
    });

    $('#restore-version').change(function() {
        $('#restore-search').val('');
        setupTree($('#restore-version').val());
    });
    
    $('#restore-target-path').keypress(function() { $('#restore-overwrite-target-other').each(function(i,e) { e.checked = true; }); });
    $('#restore-target-path').change(function() { $('#restore-overwrite-target-other').each(function(i,e) { e.checked = true; }); });

    $('#restore-overwrite-target-other').click(function() { $('#restore-target-path-browse').trigger('click'); } );

    $('#restore-target-path-browse').click(function(e) {
        $.browseForFolder({
            title: 'Select restore folder',
            resolvePath: true,
            callback: function(path, display) {
                $('#restore-target-path').val(path);
                $('#restore-overwrite-target-other').each(function(i, e) { e.checked = true; });
            }
        });
    });

   
});