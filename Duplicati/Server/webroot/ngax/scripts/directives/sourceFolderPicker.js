backupApp.directive('sourceFolderPicker', function() {
  return {
    restrict: 'E',
    require: ['ngSources', 'ngFilters'],
    scope: {
        ngSources: '=',
        ngFilters: '=',
        ngShowHidden: '='
    },
    templateUrl: 'templates/sourcefolderpicker.html',

    controller: function($scope, $timeout, SystemInfo, AppService, AppUtils, gettextCatalog) {

        var scope = $scope;
        scope.systeminfo = SystemInfo.watch($scope);
        var sourceNodeChildren = null;

        $scope.treedata = { };

        var sourcemap = {};
        var excludemap = {};
        var defunctmap = {};
        var partialincludemap = {};
        var filterList = null;
        var displayMap = {};

        var dirsep = scope.systeminfo.DirectorySeparator || '/';

        function compareablePath(path) {
            if (path.substr(0, 1) == '%' && path.substr(path.length - 1, 1) == '%')
                path += dirsep;

            return scope.systeminfo.CaseSensitiveFilesystem ? path : path.toLowerCase();
        }

        function setEntryType(n)
        {
            n.entrytype = AppUtils.getEntryTypeFromIconCls(n.iconCls);
        }

        function setIconCls(n) {
            var cp = compareablePath(n.id);

            if (cp == compareablePath('%MY_DOCUMENTS%'))
                n.iconCls = 'x-tree-icon-mydocuments';
            else if (cp == compareablePath('%MY_MUSIC%'))
                n.iconCls = 'x-tree-icon-mymusic';
            else if (cp == compareablePath('%MY_PICTURES%'))
                n.iconCls = 'x-tree-icon-mypictures';
            else if (cp == compareablePath('%DESKTOP%'))
                n.iconCls = 'x-tree-icon-desktop';
            else if (cp == compareablePath('%HOME%'))
                n.iconCls = 'x-tree-icon-home';
            else if (n.id.substr(0, 9) == "%HYPERV%\\" && n.id.length >= 10) {
                n.iconCls = 'x-tree-icon-hypervmachine';
                n.tooltip = gettextCatalog.getString("ID:") + " " + n.id.substring(9, n.id.length);
            }
            else if (n.id.substr(0, 8) == "%HYPERV%")
                n.iconCls = 'x-tree-icon-hyperv';
            else if (n.id.substr(0, 8) == "%MSSQL%\\" && n.id.length >= 9) {
                n.iconCls = 'x-tree-icon-mssqldb';
                n.tooltip = gettextCatalog.getString("ID:") + " " + n.id.substring(8, n.id.length);
            }
            else if (n.id.substr(0, 7) == "%MSSQL%")
                n.iconCls = 'x-tree-icon-mssql';
            else if (defunctmap[cp])
                n.iconCls = 'x-tree-icon-broken';
            else if (cp.substr(cp.length - 1, 1) != dirsep)
                n.iconCls = 'x-tree-icon-leaf';

            setEntryType(n);
        }

        function indexOfPathInArray(array, item) {
            item = compareablePath(item);
            for(var ix in array)
                if (compareablePath(array[ix]) == item)
                    return ix;

            return -1;

        }

        function removePathFromArray(array, item) {
            var ix = indexOfPathInArray(array, item);
            if (ix >= 0) {
                array.splice(ix, 1);
                return true;
            }

            return false;
        }

        function traversenodes(m, start) {
            var root = (start || scope.treedata);
            if (!root.children)
                return;

            var work = [];
            for(var v in root.children)
                work.push([root, root.children[v]]);

            while(work.length > 0) {
                var x = work.pop();
                var r = m(x[1], x[0]);
                
                // false == stop
                if (r === false)
                    return;

                // true == no recurse
                if (r !== true && x[1].children) {
                    for(var v in x[1].children)
                        work.push([x[1], x[1].children[v]]);
                }
            }
        }

        function buildidlookup(sources, map) {
            var dirsep = scope.systeminfo.DirectorySeparator || '/';            

            map = map || {};

            for(var n in sources) {
                var parts = compareablePath(n).split(dirsep);
                var p = [];

                for(var pi in parts) {
                    p.push(parts[pi]);
                    var r = p.join(dirsep);
                    var l = r.substr(r.length - 1, 1);
                    if (l != dirsep)
                        r += dirsep;
                    map[r] = true;
                }
            }

            return map;    
        }

        function updateIncludeFlags(root, parentFlag) {
            if (root != null)
                root = {children: [root], include: parentFlag};

            traversenodes(function(n, p) {
                if (n.root)
                    return null;

                if (sourcemap[compareablePath(n.id)])
                    n.include = '+';
                else if (p != null && p.include == '+') {
                    if (filterList == null) 
                        n.include = excludemap[compareablePath(n.id)] ? '-' : '+';
                    else
                        n.include = AppUtils.evalFilter(n.id, filterList) ? '+' : '-';
                }
                else if (p != null && p.include == '-')
                    n.include = '-';
                else if (partialincludemap[compareablePath(n.id)])
                    n.include = ' ';
                else
                    n.include = null;
            }, root);            
        }

        function syncTreeWithLists() {
            if (scope.ngSources == null || sourceNodeChildren == null)
                return;

            dirsep = scope.systeminfo.DirectorySeparator || '/';            

            sourcemap = {};
            excludemap = {};
            filterList = null;

            var anySpecials = false;

            for(var i = 0; i < (scope.ngFilters || []).length; i++) {
                var f = AppUtils.splitFilterIntoTypeAndBody(scope.ngFilters[i], dirsep);
                if (f != null) {
                    if (f[0] == '-path')
                        excludemap[compareablePath(f[1])] = true;
                    else if (f[0] == '-folder')
                        excludemap[compareablePath(f[1] + dirsep)] = true;
                    else
                        anySpecials = true;
                }
            }

            if (anySpecials)
                filterList = AppUtils.filterListToRegexps(scope.ngFilters, scope.systeminfo.CaseSensitiveFilesystem);

            sourceNodeChildren.length = 0;

            function findInList(lst, path) {
                for(var x in lst)
                    if (compareablePath(lst[x].id) == path)
                        return x;

                return false;
            }

            for(var i = 0; i < scope.ngSources.length; i++) {
                var k = compareablePath(scope.ngSources[i]);
                if (k.length == 0)
                    continue;

                sourcemap[k] = true;

                var txt = scope.ngSources[i];
                if (k.indexOf('%') == 0) {
                    var nx = k.substr(1).indexOf('%') + 2;
                    if (nx > 1) {
                        var key = compareablePath(k.substr(0, nx));
                        txt = displayMap[compareablePath(k)] || ((displayMap[compareablePath(key)] || key) + txt.substr(nx));
                    }
                }

                var n = {
                    text: txt,
                    id: scope.ngSources[i],
                    include: '+',
                    other: true,
                    leaf: true
                };

                setIconCls(n);

                sourceNodeChildren.push(n);

                if (defunctmap[k] == null && n.iconCls != "x-tree-icon-hyperv" && n.iconCls != "x-tree-icon-hypervmachine" && n.iconCls != "x-tree-icon-mssql" && n.iconCls != "x-tree-icon-mssqldb") {
                    defunctmap[k] = true;

                    var p = scope.ngSources[i];
                    if (p.substr(0, 1) == '%' && p.substr(p.length - 1, 1) == '%')
                        p += dirsep;

                    AppService.post('/filesystem/validate', {path: p}).then(function(data) {
                        defunctmap[compareablePath(data.config.data.path)] = false;

                    }, function(data) {
                        var p = data.config.data.path;
                        var ix = findInList(sourceNodeChildren, compareablePath(p));
                        if (ix != null && sourceNodeChildren[ix].id == p) {
                            sourceNodeChildren[ix].iconCls = 'x-tree-icon-broken';
                            setEntryType(sourceNodeChildren[ix]);
                        }
                    });
                }                
            }

            partialincludemap = buildidlookup(sourcemap);            

            updateIncludeFlags();
        }

        $scope.$watch('ngSources', syncTreeWithLists, true);
        $scope.$watch('ngFilters', syncTreeWithLists, true);

        function findParent(id) {
            var r = {};
            id = compareablePath(id);
            r[id] = true;

            var map = buildidlookup(r);
            var fit = null;

            traversenodes(function(n) {
                if (compareablePath(n.id) == id) {
                    fit = n;
                    return false;
                }

                if (!map[compareablePath(n.id)])
                    return true;
            });

            return fit;
        }

        $scope.toggleCheck = function(node) {
            dirsep = scope.systeminfo.DirectorySeparator || '/';            

            var c = compareablePath(node.id);
            var c_is_dir = c.substr(c.length - 1, 1) == dirsep;

            if (node.include == null || node.include == ' ') {

                if (c_is_dir) {
                    for(var i = scope.ngSources.length - 1; i >= 0; i--) {
                        var s = compareablePath(scope.ngSources[i]);

                        if (s == c)
                            return;

                        if (s.substr(s.length - 1, 1) == dirsep && c.indexOf(s) == 0) {
                                return;
                        } else if (s.indexOf(c) == 0) {
                            scope.ngSources.splice(i, 1);
                        }
                    }
                }

                scope.ngSources.push(node.id);

            } else if (node.include == '+') {
                if (sourcemap[c]) {
                    removePathFromArray(scope.ngSources, node.id);

                    for(var i = scope.ngFilters.length - 1; i >= 0; i--) {
                        var n = AppUtils.splitFilterIntoTypeAndBody(scope.ngFilters[i], dirsep);
                        if (n != null) {
                            if (c_is_dir) {
                                if (n[0] == '-path' || n[0] == '-folder') {
                                    if (compareablePath(n[1] + (n[0] == '-folder' ? dirsep : '')).indexOf(c) == 0)
                                        scope.ngFilters.splice(i, 1);
                                }
                            } else {
                                if (n[0] == '-path' && compareablePath(n[1]) == c)
                                    scope.ngFilters.splice(i, 1);
                            }
                        }
                    }
                } else {
                    scope.ngFilters.push("-" + node.id);
                }
            } else if (node.include == '-') {
                removePathFromArray(scope.ngFilters, '-' + node.id);
            }
        };

        $scope.toggleExpanded = function(node) {
            node.expanded = !node.expanded;

            if (node.root || node.iconCls == 'x-tree-icon-leaf' || node.iconCls == 'x-tree-icon-locked'
                || node.iconCls == 'x-tree-icon-hyperv' || node.iconCls == 'x-tree-icon-hypervmachine'
                || node.iconCls == 'x-tree-icon-mssql' || node.iconCls == 'x-tree-icon-mssqldb')
                return;

            if (!node.children && !node.loading) {
                node.loading = true;

                AppService.post('/filesystem?onlyfolders=false&showhidden=true', {path: node.id}).then(function(data) {
                    node.children = data.data;
                    node.loading = false;

                    if (node.children != null)
                        for(var i in node.children)
                            setEntryType(node.children[i]);
                    
                    updateIncludeFlags(node, node.include);

                }, function() {
                    node.loading = false;
                    node.expanded = false;
                    AppUtils.connectionError.apply(AppUtils, arguments);
                });
            }
        };

        $scope.toggleSelected = function(node) {
            if (scope.selectednode != null)
                scope.selectednode.selected = false;

            scope.selectednode = node;
            scope.selectednode.selected = true;
        };

        scope.treedata.children = [];

        AppService.post('/filesystem?onlyfolders=false&showhidden=true', {path: '/'}).then(function(data) {

            var usernode = {
                text: gettextCatalog.getString('User data'),
                root: true,
                iconCls: 'x-tree-icon-userdata',
                expanded: true,
                children: []
            };
            var systemnode = {
                text: gettextCatalog.getString('Computer'),
                root: true,
                iconCls: 'x-tree-icon-computer',
                children: []
            };
            var sourcenode = {
                text: gettextCatalog.getString('Source data'),
                root: true,
                iconCls: 'x-tree-icon-others',
                expanded: true,
                children: [],
                isSourcenode: true
            };

            sourceNodeChildren = sourcenode.children;
            scope.treedata.children.push(usernode, systemnode, sourcenode);

            for(var i = 0; i < data.data.length; i++) {
                if (data.data[i].id.indexOf('%') == 0) {
                    usernode.children.push(data.data[i]);
                    var cp = compareablePath(data.data[i].id);
                    displayMap[cp] = data.data[i].text;

                    setIconCls(data.data[i]);
                }
                else
                    systemnode.children.push(data.data[i]);
            }

            syncTreeWithLists();

        }, AppUtils.connectionError);

        AppService.get('/hyperv', {path: '/'}).then(function(data) {
            if (data.data != null && data.data.length > 0) {
                var hypervnode = {
                    text: gettextCatalog.getString('Hyper-V Machines'),
                    id: "%HYPERV%",
                    children: []
                };
                setIconCls(hypervnode);
                var cp = compareablePath(hypervnode.id);
                displayMap[cp] = gettextCatalog.getString('All Hyper-V Machines');

                // add HyperV at the beginning
                if (scope.treedata.children.length < 1)
                    scope.treedata.children.push(hypervnode);
                else
                    scope.treedata.children = [hypervnode].concat(scope.treedata.children);

                for (var i = 0; i < data.data.length; i++) {
                    var node = {
                        leaf: true,
                        id: "%HYPERV%\\" + data.data[i].id,
                        text: data.data[i].name};

                    cp = compareablePath(node.id);
                    displayMap[cp] = gettextCatalog.getString('Hyper-V Machine:') + " " + node.text;
                    setIconCls(node);
                    hypervnode.children.push(node);
                }
                syncTreeWithLists();
            }
        }, AppUtils.connectionError);

        AppService.get('/mssql', { path: '/' }).then(function (data) {
            if (data.data != null && data.data.length > 0) {
                var mssqlnode = {
                    text: gettextCatalog.getString('Microsoft SQL Databases'),
                    id: "%MSSQL%",
                    children: []
                };
                setIconCls(mssqlnode);
                var cp = compareablePath(mssqlnode.id);
                displayMap[cp] = gettextCatalog.getString('All Microsoft SQL Databases');

                // add MS SQL DB at the beginning
                if (scope.treedata.children.length < 1)
                    scope.treedata.children.push(mssqlnode);
                else
                    scope.treedata.children = [mssqlnode].concat(scope.treedata.children);

                for (var i = 0; i < data.data.length; i++) {
                    var node = {
                        leaf: true,
                        id: "%MSSQL%\\" + data.data[i].id,
                        text: data.data[i].name
                    };

                    cp = compareablePath(node.id);
                    displayMap[cp] = gettextCatalog.getString('Microsoft SQL Database:') + " " + node.text;
                    setIconCls(node);
                    mssqlnode.children.push(node);
                }
                syncTreeWithLists();
            }
        }, AppUtils.connectionError);
    }
  }
});
