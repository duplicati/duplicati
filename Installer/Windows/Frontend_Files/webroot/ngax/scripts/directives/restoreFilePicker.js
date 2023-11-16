backupApp.directive('restoreFilePicker', function() {
      return {
        restrict: 'E',
        require: ['ngSources', 'ngFilters', 'ngBackupId', 'ngTimestamp', 'ngSelected'],
        scope: {
            ngSources: '=',
            ngFilters: '=',
            ngBackupId: '=',
            ngTimestamp: '=',
            treedata: '=',
            ngSelected: '=',
            ngSearchFilter: '=',
            ngSearchMode: '='
        },
        templateUrl: 'templates/restorefilepicker.html',

        controller: function($scope, $timeout, SystemInfo, AppService, AppUtils) {

            var scope = $scope;
            var dirsep = '/';            

            $scope.systeminfo = SystemInfo.watch($scope);
            $scope.treedata = [];

            function compareablePath(path) {
                return scope.systeminfo.CaseSensitiveFilesystem ? path : path.toLowerCase();
            };

            $scope.toggleExpanded = function(node) {
                node.expanded = !node.expanded;

                if (node.root || node.iconCls == 'x-tree-icon-leaf' || node.iconCls == 'x-tree-icon-locked')
                    return;

                if (!node.children && !node.loading) {
                    node.loading = true;

                    // Prefix filter with "@" to prevent Duplicati from
                    // mistaking literal '*' and '?' characters in paths
                    // for glob wildcard characters
                    AppService.get('/backup/' + $scope.ngBackupId + '/files/' + encodeURIComponent(node.id) + '?prefix-only=false&folder-contents=true&time=' + encodeURIComponent($scope.ngTimestamp) + '&filter=' + encodeURIComponent('@' + node.id)).then(function(data) {
                        var children = []

                         for(var n in data.data.Files)
                         {
                            var disp = data.data.Files[n].Path.substr(node.id.length);
                            var leaf = true;
                            if (disp.substr(disp.length - 1, 1) == dirsep) {
                                disp = disp.substr(0, disp.length - 1);
                                leaf = false;
                            }

                            children.push({
                                text: disp,
                                id: data.data.Files[n].Path,
                                size: data.data.Files[n].Sizes[0],
                                iconCls: leaf ? 'x-tree-icon-leaf' : '',
                                entrytype: AppUtils.getEntryTypeFromIconCls(leaf ? 'x-tree-icon-leaf' : ''),
                                leaf: leaf
                            });
                        }


                        node.children = children;
                        node.loading = false;

                        updateCheckState(children);
                        
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

            function forAllChildren(node, callback) {
                var q = [node];
                while(q.length > 0) {
                    var x = q.pop();
                    if (x.children != null)
                        for(var c in x.children) {
                            q.push(x.children[c]);
                            callback(x.children[c]);
                        }
                }
            };

            function propagateCheckDown(node) {
                forAllChildren(node, function(n) { n.include = node.include; });
            };

            function findParent(node) {
                var e = [];
                e.push.apply(e, $scope.treedata.children);
                var p = compareablePath(node.id);

                while(e.length > 0) {
                    var el = e.pop();
                    if (p.length > el.id.length && el.id.substr(el.id.length - 1, 1) == dirsep && el.children != null) {
                        if (p.indexOf(compareablePath(el.id)) == 0) {
                            for(var n in el.children) {
                                if (el.children[n] == node)
                                    return el;
                                else
                                    e.push(el.children[n]);
                            }
                        }
                    }
                }

                return null;
            };

            function propagateCheckUp(node) {
                var p = findParent(node);
                while(p != null) {
                    var all = true;
                    var any = false;
                    for(var i in p.children) {
                        if (p.children[i].include != '+')
                            all = false;
                        if (p.children[i].include == '+' || p.children[i].include == ' ')
                            any = true;

                        if (all == false && any == true)
                            break;
                    }

                    if (all) {
                        p.include = '+';
                    } else if (any) {
                        p.include = ' ';
                    } else {
                        p.include = '';
                    }

                    p = findParent(p);
                }
            };

            function buildSelectedMap() {
                var map = {};
                for (var i = $scope.ngSelected.length - 1; i >= 0; i--)
                    map[compareablePath($scope.ngSelected[i])] = true;
                return map;                
            };

            function buildPartialMap() {
                var map = {};
                for (var i = $scope.ngSelected.length - 1; i >= 0; i--) {
                    var is_dir = $scope.ngSelected[i].substr($scope.ngSelected[i].length - 1, 1) == dirsep;
                    var parts = $scope.ngSelected[i].split(dirsep);
                    var cur = '';
                    for (var j = 0; j < parts.length; j++) {
                        cur += parts[j];
                        if (j != parts.length - 1 || is_dir)
                            cur += dirsep;

                        map[compareablePath(cur)] = true;
                    };
                }
                return map;
            };

            function removePathFromArray(arr, path) {
                path = compareablePath(path);

                for (var i = arr.length - 1; i >= 0; i--) {
                    var n = compareablePath(arr[i]);
                    if (n == path)
                        arr.splice(i, 1);
                };                
            };

            $scope.toggleCheck = function(node) {

                if (node.include != '+') {
                    var p = findParent(node) || node;

                    var cur = node;
                    while(p != null) {
                        // Remove path and all sub-paths

                        var c = compareablePath(cur.id);
                        var is_dir = c.substr(c.length - 1, 1) == dirsep;

                        for (var i = $scope.ngSelected.length - 1; i >= 0; i--) {
                            var n = compareablePath($scope.ngSelected[i]);
                            if (n == c || (n.indexOf(c) == 0 && is_dir))
                                $scope.ngSelected.splice(i, 1);
                        };

                        var all = true;
                        var pp = compareablePath(p.id);
                        var map = buildSelectedMap();
                        map[c] = true;

                        for (var i = p.children.length - 1; i >= 0; i--)
                            if (!map[compareablePath(p.children[i].id)]) {
                                all = false;
                                break;
                            }

                        if (!all  || p == node || $scope.ngSearchMode) {
                            $scope.ngSelected.push(cur.id);
                            break;
                        }

                        cur = p;
                        p = findParent(p);

                        if (p == null && all && !$scope.ngSearchMode)
                            $scope.ngSelected.push(cur.id);
                    }

                } else {

                    var map = buildSelectedMap();

                    // This item is not included, include parents children

                    var backtrace = [];

                    var p = node;                        
                    while(p != null && !map[compareablePath(p.id)]) {
                        backtrace.push(p);
                        p = findParent(p);
                    }

                    removePathFromArray($scope.ngSelected, p.id);

                    while(backtrace.length > 0) {
                        var t = backtrace.pop();
                        for(var n in p.children)
                            if (t != p.children[n]) {
                                $scope.ngSelected.push(p.children[n].id);
                            }

                        p = t;
                    }
                }

                //updateCheckState();
            };

            var updateCheckState = function(nodes) {
                var map = buildSelectedMap();                
                var partialmap = buildPartialMap();

                nodes = nodes || $scope.treedata.children;

                var w = [];
                w.push.apply(w, $scope.treedata.children);

                while(w.length > 0) {
                    var n = w.pop();
                    var cp = compareablePath(n.id);

                    if (map[cp]) {
                        n.include = '+';
                        propagateCheckDown(n);
                    } else if (partialmap[cp]) {
                        n.include = ' ';
                        if (n.children != null)
                            w.push.apply(w, n.children);
                    }
                    else {
                        n.include = '';
                        propagateCheckDown(n);
                    }
                }
            };

            var buildnodes = function(items, parentpath) {
                var res = [];

                parentpath = parentpath || '';

                for(var n in items) {
                    var txt = items[n].Path.substr(parentpath.length);
                    if (txt[txt.length] == dirsep)
                        txt.length--;

                    var rootnode = {
                        text: txt,
                        expanded: items[n].expanded,
                        iconCls: items[n].iconCls,
                        leaf: items[n].leaf,
                        id: items[n].Path,
                        entrytype: AppUtils.getEntryTypeFromIconCls(items[n].iconCls)
                    };
                    
                    if (items[n].Children) {
                        rootnode.children = buildnodes(items[n].Children, items[n].Path);
                        delete items[n].Children;
                    }

                    res.push(rootnode);
                }

                return res;
            };

            var updateRoots = function()
            {
                if ($scope.ngSources == null || $scope.ngSources.length == 0)
                    dirsep = scope.systeminfo.DirectorySeparator || '/';
                else
                    dirsep = $scope.ngSources[0].Path[0] == '/' ? '/' : '\\';

                var roots = buildnodes($scope.ngSources);

                $scope.treedata = $scope.treedata || {};

                $scope.treedata.children = roots;
                $scope.treedata.forAll = function(callback) { forAllChildren(this, callback); };
                $scope.treedata.allSelected = function() {
                    var q = [];
                    var r = [];
                    q.push.apply(q, this.children);
                    while(q.length > 0) {
                        var n = q.pop();
                        if (n.include == '+')
                            r.push(n.id);
                        else if (n.include == ' ')
                            q.push.apply(q, n.children);
                    }

                    return r;
                };
                if (roots.length == 1)
                     $scope.toggleExpanded(roots[0]);

                 updateCheckState();
            };

            $scope.$watchCollection('ngSources', updateRoots);
            updateRoots();

            $scope.$watchCollection('ngSelected', updateCheckState)
            
        }
    };
});
