$(document).ready(function() {

    var curpage = 0;
    var backupId = 0;
    var dirSep = '/';
    var treeels = { };
    var includes = { };
    var searchTree = null;
    var searchdata = {};

    var performRestore = function(tasks) {
        var dlg = $('<div></div>').attr('title', 'Restoring files ...');
        dlg.dialog({
            autoOpen: true,
            modal: true,
            closeOnEscape: false
        });

        var pg = $('<div></div>');
        pg.progressbar({ value: false });
        var pgtxt = $('<div></div>');
        pgtxt.text('Starting ....');

        dlg.append(pg);
        dlg.append(pgtxt);

        var processTask = function() {
            if (tasks.length == 0) {
                curpage = Math.min(2, curpage+1);
                updatePageNav();
                dlg.dialog('close');
                dlg.remove();
                return;
            }

            var task = tasks.pop();
            task['action'] = 'restore-files';

            APP_DATA.callServer(task, function() {
                processTask();
            },
            function(d, s, m) {
                alert('Error: ' + m);
                dlg.dialog('close');
                dlg.remove();
            });

        }

        setTimeout(processTask, 50000);
        //processTask();
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

                    var tasks = [];

                    for(var n in includes) {
                        var t = {
                            time: n,
                            id: backupId,
                            paths: []
                        }
                        for(var p in includes[n])
                            t.paths.push(p);

                        if (t.paths.length > 0)
                            tasks.push(t);
                    }

                    performRestore(tasks);
                } else {
                    var els = 0;
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
            dlg_buttons.last().button('option', 'label', 'Next >');
        } else if (curpage == 1) {
            $('#restore-files-page').hide();
            $('#restore-path-page').show();
            $('#restore-complete-page').hide();
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

    var colorize = function(tree, term) {
        $(tree).find('a.jstree-anchor').each(function(i, e) {
            var s = $(e);
            var o = s.children();
            s.html(replace_insensitive(s.text(), term, '<div class="search-match">$1</div>'));
            s.prepend(o);
        });
    };

    var recolor =  function(tree, time, lst) {
        lst = lst || tree.find('li');
        if (!includes[time])
            includes[time] = {};

        lst.each(function(i, e) {
            var node = tree.jstree().get_node($(e));
            if (!node)
                return;
            var path = node.original.filepath;
            var time = node.original.time;

            var full = includes[time][path];
            var partial = full;

            if (!full)
                for(var n in includes[time])
                    if (n.indexOf(path) == 0) {
                        partial = true;
                        break;
                    }

            if (full) {
                $(e).find('a').first().addClass('restore-included restore-included-full');
            } else if (partial) {
                $(e).find('a').first().removeClass('restore-included-full').addClass('restore-included');
            } else {
                $(e).find('a').first().removeClass('restore-included-full restore-included');
            }
        });
    };

    var recolor_search = function(tree) {
        var nodes = $(tree).find("i.icon-clock");
        nodes.each(function(i, e) {
            var el = $(e).parent();
            var node = searchTree.jstree().get_node(el);
            if (!node)
                return;

            if (node.original.isTime) {
                var path = node.original.filepath;
                var time = node.original.time;
                if (includes[time][path])
                    el.addClass('restore-included-full');
            }
        });
    };    

    var inSearch = false;
    var doSearch = function(search) {

        if (inSearch)
            return;

        inSearch = true;
        if (searchTree != null) {
            searchTree.remove();
            searchTree = null;
        }

        for(var t in treeels)
            treeels[t].hide();


        var processData = function (callback, data) {

            var roots = [];

            var createNode = function(disp, path, isFolder) {
                return {
                    text: disp,
                    children: [],
                    filepath: path, 
                    isFolder: isFolder,
                    state: {opened: true }
                };
            };

            var appendNode = function(path) {
                var isFolder = path.lastIndexOf(dirSep) == path.length - 1;
                var parts = path.split(dirSep);
                var cur = roots;
                var rebuilt = '';
                var lastEntry = null;

                for(var i in parts) {
                    var found = false;
                    if (parts[i] == '')
                        continue;

                    rebuilt += '/' + parts[i];

                    for(var j in cur) {
                        if (cur[j].text == parts[i]) {
                            found = true;
                            cur = cur[j].children;
                        }
                    }

                    if (!found) {
                        lastEntry = createNode(parts[i], rebuilt, true);
                        cur.push(lastEntry);
                        cur = lastEntry.children;
                    }
                }

                if (!isFolder && lastEntry != null) {
                    lastEntry.isFolder = false;
                    lastEntry.state.opened = false;
                    lastEntry.icon = 'icon-file icon-file-' + lastEntry.text.substr(lastEntry.text.lastIndexOf('.')+1);
                    for(var x in data.Filesets)
                        cur.push({
                            isTime: true,
                            time: data.Filesets[x].Time,
                            filepath: lastEntry.filepath,
                            text: $.timeago(data.Filesets[x].Time),
                            icon: 'icon-clock'
                        });
                }
            };

            for(var n in data.Files)
                appendNode(data.Files[n].Path);

            var packNodes = function() {
                for(var rx in roots) {
                    var r = roots[rx];
                    
                    while(r.children.length == 1 && r.children[0].isFolder) {
                        var c = r.children[0];
                        r.text = r.text + dirSep + c.text;
                        r.filepath = r.text;
                        r.children = c.children;
                    }

                    if (dirSep == '/' && !r.text.substr(0, 1) != dirSep)
                        r.text = dirSep + r.text;
                }
            };

            packNodes();

            callback(roots);

            colorize(searchTree, search);
            recolor_search(searchTree);    

            inSearch = false;
        }

        var loadData = function(callback) {
            for(var k in searchdata)
                if (search.indexOf(k) == 0) {
                    var els = [];
                    for(var n in searchdata[k].Files)
                        if (searchdata[k].Files[n].Path.toLowerCase().indexOf(search.toLowerCase()) >= 0)
                            els.push(searchdata[k].Files[n]);

                    processData(callback, {Files: els, Filesets: searchdata[k].Filesets});
                    return;
                }

            APP_DATA.callServer({
                    action: 'search-backup-files',
                    id: backupId,
                    filter: '*' + search + '*',
                    'all-versions': true
                },
                function(data) {
                    processData(callback, data);
                    searchdata[search] = data;
                },
                function(data, success, message) {
                    alert('Search failed: ' + message);
                    inSearch = false;
                }
            );
        }

        searchTree = $('<div></div>');
        $('#restore-files-tree').append(searchTree);
        searchTree.jstree({
            'plugins' : [ 'wholerow' ],
            'core': {
                'data': function(node, callback) {
                    if (node.id === '#')
                        loadData(callback);
                }
            }
        });

        searchTree.bind('open_node.jstree', function (event, node) {
            recolor_search(searchTree);
        });


        searchTree.bind('dblclick.jstree', function (event) {
            var node = searchTree.jstree().get_node($(event.target).closest("li"));
            var path = node.original.filepath;
            var time = node.original.time;

            if (!node.original.isTime) {
                searchTree.jstree("open_node", $(event.target).closest("li"));
                return;
            }

            if (!includes[time])
                includes[time] = {};


            var e = $(event.target).closest("li");
            if (includes[time][path]) {
                delete includes[time][path];

                e.find('a').first().removeClass('restore-included-full');
                //recolor(searchTree, e.parents('li'));
            } else {
                includes[time][path] = true;
                e.find('a').first().addClass('restore-included-full');
                //recolor(searchTree, e.parents('li'));
            }
        });        

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
                var disp = o.Path.substr(data.Prefix.length);
                var isFolder = disp.lastIndexOf(dirSep) == disp.length - 1;
                var icon = null;
                var state = null;
                if (isFolder)
                    disp = disp.substr(0, disp.length - 1);
                else
                    icon = 'icon-file icon-file-' + disp.substr(disp.lastIndexOf('.')+1);

                if (data.Prefix == '') {
                    disp = APP_DATA.getBackupName(backupId) || disp;
                    //state = {opened: true};
                }

                nodes.push({
                    text: disp,
                    filepath: o.Path,
                    time: time,
                    children: isFolder,
                    state: state,
                    icon: icon
                });
            }

            callback(nodes, data);
        });
    };

    var setupFullTree = function(time) {

        var treeel = treeels[time];

        if (!treeels[time]) {
            var treeel = $('<div></div>');
            treeels[time] = treeel;
            $('#restore-files-tree').append(treeel);

            treeel.jstree({
                'core': {
                    'data': function(node, callback) { 
                        loadNodes(node, function(nodes, data) { 
                            callback(nodes);
                            recolor(treeel, time);

                            if (data.Prefix == '')
                                treeel.jstree("open_node", treeel.find('li').first());
                        }, 
                        time); 
                    }
                },
                'plugins' : [ 'wholerow' ]
            });

            treeel.bind('open_node.jstree', function (event, node) {
                var nodes = $(event.target).find("li");
                recolor(treeel, time, nodes);
            });

            treeel.bind('dblclick.jstree', function (event) {
                var node = treeel.jstree().get_node($(event.target).closest("li"));
                var path = node.original.filepath;
                var time = node.original.time;
                if (!includes[time])
                    includes[time] = {};

                var e = $(event.target).closest("li");
                if (includes[time][path]) {
                    delete includes[time][path];

                    e.find('a').first().removeClass('restore-included-full');
                    recolor(treeel, time, e.parents('li'));
                } else {
                    includes[time][path] = true;
                    e.find('a').first().addClass('restore-included-full');
                    recolor(treeel, time, e.parents('li'));
                }
            });
        }

        for(var t in treeels)
            if (t != time)
                treeels[t].hide();
            else {
                treeels[t].show();
                recolor(treeels[t], time);
            }

        if (searchTree != null)
            searchTree.hide();

    };

    $('#restore-dialog').on('setup-data', function(e, id) {
        backupId = id;
        treeels = { };
        includes = { };
        searchTree = null;
        searchdata = { };
        $('#restore-files-tree').empty();
        $('#restore-form').each(function(i, e) { e.reset(); });
        curpage = 0; 
        updatePageNav();    


        APP_DATA.getServerConfig(function(serverdata) {

            dirSep = serverdata.DirectorySeparator;

            APP_DATA.callServer({ 'action': 'list-backup-sets', id: id }, function(data, success, message) {
                    $('#restore-version').empty();

                    if (data == null || data.length == 0) {
                        alert('Failed to get list of backup times');
                        $('#restore-dialog').dialog('close');
                    }

                    $('#restore-version').append($("<option></option>").attr("value", data[0].Time).text('Latest'));
                    for(var i in data)
                        $('#restore-version').append($("<option></option>").attr("value", data[i].Time).text($.timeago(data[i].Time)));

                    $('#restore-version').trigger('change');

                }, function(data, success, message) {
                    alert('Failed to get list of backup times');
                    $('#restore-dialog').dialog('close');
                });
        }, function() {
            alert('Failed to get server config');
            $('#restore-dialog').dialog('close');
        });
    });

    var doQuickSearch = function(search) {
        if (searchTree != null && searchTree.css('display') == 'block') {
            var search = $('#restore-search').val();
            for(var k in searchdata) {
                if (search.indexOf(k) == 0) {
                    doSearch(search);
                    return true;
                }
            }
        }

        return false;
    };

    $('#restore-search').keypress(function(e) {
        if (e.which == 13 && $('#restore-search').val().trim() != '')
            doSearch($('#restore-search').val());
        else
            doQuickSearch($('#restore-search').val());
    });

    $('#restore-search').keydown(function(e) {
        doQuickSearch($('#restore-search').val());
    });

    $('#restore-search').change(function(e) {
        if ($('#restore-search').val() == '')
            $('#restore-version').trigger('change');
    });

    $('#restore-search').on('search', function(e) {
        if ($('#restore-search').val() == '')
            $('#restore-version').trigger('change');
        else
            doSearch($('#restore-search').val());
    });

    $('#restore-version').change(function() {
        $('#restore-search').val('');
        setupFullTree($('#restore-version').val());
    });

    $('#restore-overwrite-overwrite').attr('checked', true);
    $('#restore-overwrite-target-original').attr('checked', true);

    $('#restore-target-path-browse').click(function(e) {
        $.browseForFolder({
            title: 'Select restore folder',
            callback: function(path, display) {
                $('#restore-target-path').val(path);
                $('#restore-overwrite-target-other').attr('checked', true);
            }
        });
    });

   
});