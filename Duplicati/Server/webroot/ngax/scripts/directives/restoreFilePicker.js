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

        controller: function ($scope, SystemInfo, AppService, AppUtils) {
            let scope = $scope;
            let dirsep = '/';
            let partialMap = new Map();

            $scope.systeminfo = SystemInfo.watch($scope);
            $scope.treedata = [];

            function compareablePath(path) {
                return scope.systeminfo.CaseSensitiveFilesystem ? path : path.toLowerCase();
            }

            $scope.toggleExpanded = function(node) {
                node.expanded = !node.expanded;

                if (node.root || node.iconCls == 'x-tree-icon-leaf' || node.iconCls == 'x-tree-icon-locked')
                    return;

                if (node.children || node.loading)
                    return;

                node.loading = true;

                // Prefix filter with "@" to prevent Duplicati from
                // mistaking literal '*' and '?' characters in paths
                // for glob wildcard characters
                AppService.get('/backup/' + $scope.ngBackupId + '/files/' + encodeURIComponent(node.id) + '?prefix-only=false&folder-contents=true&time=' + encodeURIComponent($scope.ngTimestamp) + '&filter=' + encodeURIComponent('@' + node.id)).then(function (data) {
                    let children = []

                    for (const item of data.data.Files) {
                        let text = item.Path.substr(node.id.length);
                        let leaf = true;
                        let isDirectory = false;

                        if (text.slice(-1) == dirsep) {
                            text = text.substr(0, text.length - 1);
                            leaf = false;
                            isDirectory = true;
                        }

                        children.push({
                            text: text,
                            id: item.Path,
                            compareId: compareablePath(item.Path),
                            size: item.Sizes[0],
                            iconCls: isDirectory ? '' : 'x-tree-icon-leaf',
                            entrytype: AppUtils.getEntryTypeFromIconCls(isDirectory ? '' : 'x-tree-icon-leaf'),
                            parent: node,
                            nodeType: isDirectory ? 'dir' : 'file',
                            leaf: leaf,
                            include: ''
                        });
                    }

                    node.children = children;
                    node.loading = false;

                    updateNodesFromMap(children);
                }, function () {
                    node.loading = false;
                    node.expanded = false;
                    AppUtils.connectionError.apply(AppUtils, arguments);
                });
            };

            $scope.toggleSelected = function(node) {
                if (scope.selectednode != null)
                    scope.selectednode.selected = false;

                scope.selectednode = node;
                scope.selectednode.selected = true;
            };

            function forAllChildren(node, callback) {
                let q = [node];
                while (q.length > 0) {
                    let x = q.pop();
                    if (x.children != null)
                        for (const c in x.children) {
                            q.push(x.children[c]);
                            callback(x.children[c]);
                        }
                }
            }

            function propagateCheckDown(startNode) {
                forAllChildren(startNode, function (n) { n.include = startNode.include; });
            }

            function buildPartialMap() {
                partialMap.clear();

                for (const selectedPath in $scope.ngSelected) {
                    const is_dir = selectedPath.slice(-1) == dirsep;
                    const parts = selectedPath.split(dirsep);
                    let path = '';

                    for (let j = 0; j < parts.length; j++) {
                        path += parts[j];
                        if (is_dir || j != parts.length - 1)
                            path += dirsep;

                        partialMap.set(path, true);
                    }
                }
            }

            $scope.toggleCheck = function (node) {
                if (node.include != '+') {
                    let parent = node.parent || node;
                    let child = node;

                    while (parent != null) {
                        // Remove path and all sub-paths
                        let selectedToDelete = []

                        for (const selected in $scope.ngSelected) {
                            if (selected === child.compareId || (child.nodeType === 'dir' && selected.indexOf(child.compareId) == 0)) {
                                selectedToDelete.push(selected);
                            }
                        }

                        for (const selected of selectedToDelete) {
                            delete $scope.ngSelected[selected];
                        }

                        let all = true;
                        for (let i = parent.children.length - 1; i >= 0; i--)
                            if (parent.children[i].compareId !== child.compareId && !(parent.children[i].compareId in $scope.ngSelected)) {
                                all = false;
                                break;
                            }

                        if (!all || parent == node || $scope.ngSearchMode) {
                            $scope.ngSelected[child.compareId] = true;
                            break;
                        }

                        child = parent;
                        parent = parent.parent;

                        if (parent == null && all && !$scope.ngSearchMode) {
                            $scope.ngSelected[child.compareId] = true;
                        }
                    }
                } else {
                    // This item is no longer included, include parents children
                    let backtrace = [];
                    let parent = node;

                    while (parent != null && !(parent.compareId in $scope.ngSelected)) {
                        backtrace.push(parent);
                        parent = parent.parent;
                    }

                    delete $scope.ngSelected[parent.compareId];

                    while (backtrace.length > 0) {
                        let backtraceNode = backtrace.pop();
                        for (const child of parent.children)
                            if (backtraceNode != child) {
                                $scope.ngSelected[child.compareId] = true;
                            }

                        parent = backtraceNode;
                    }
                }
            };

            function updateNodesFromMap(nodes) {
                nodes = nodes || $scope.treedata.children;

                let toUpdate = [];
                toUpdate.push.apply(toUpdate, $scope.treedata.children);
                while (toUpdate.length > 0) {
                    let n = toUpdate.pop();

                    if (n.compareId in $scope.ngSelected) {
                        n.include = '+';
                        propagateCheckDown(n);
                    } else if (partialMap.has(n.compareId)) {
                        n.include = ' ';
                        if (n.children != null)
                            toUpdate.push.apply(toUpdate, n.children);
                    }
                    else {
                        n.include = '';
                        propagateCheckDown(n);
                    }
                }
            }

            function buildnodes(items, parentpath) {
                let nodes = [];
                parentpath = parentpath || '';

                for (const n in items) {
                    const txt = items[n].Path.substr(parentpath.length);
                    let isDirectory = false;

                    if (txt.slice(-1) == dirsep) {
                        txt.length--;
                        isDirectory = true;
                    }

                    let rootnode = {
                        text: txt,
                        expanded: items[n].expanded,
                        iconCls: items[n].iconCls,
                        leaf: items[n].leaf,
                        id: items[n].Path,
                        compareId: compareablePath(items[n].Path),
                        entrytype: AppUtils.getEntryTypeFromIconCls(items[n].iconCls),
                        nodeType: isDirectory ? 'dir' : 'file'
                    };

                    if (items[n].Children) {
                        rootnode.children = buildnodes(items[n].Children, items[n].Path);
                        for (let child of rootnode.children) {
                            child.parent = rootnode;
                        }
                        delete items[n].Children;
                    }

                    nodes.push(rootnode);
                }

                return nodes;
            }

            function updateRoots() {
                if ($scope.ngSources == null || $scope.ngSources.length == 0)
                    dirsep = scope.systeminfo.DirectorySeparator || '/';
                else
                    dirsep = $scope.ngSources[0].Path[0] == '/' ? '/' : '\\';

                let roots = buildnodes($scope.ngSources);
                $scope.treedata = $scope.treedata || {};
                $scope.treedata.children = roots;

                if (roots.length == 1)
                    $scope.toggleExpanded(roots[0]);

                updateNodesFromMap();
            }

            $scope.$watchCollection('ngSources', updateRoots);
            updateRoots();

            $scope.$watchCollection('ngSelected', selectedNodesChanged);

            function selectedNodesChanged() {
                buildPartialMap();
                updateNodesFromMap();
            }
        }
    };
});
