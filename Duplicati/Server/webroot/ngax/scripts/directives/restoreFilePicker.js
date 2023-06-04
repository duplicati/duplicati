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

                if (node.root || node.iconCls == 'x-tree-icon-locked')
                    return;

                if (node.children || node.loading)
                    return;

                node.loading = true;

                if (node.nodeType === 'dir') {
                    getFilesUnderDirectory(node);
                }
                else {
                    getVersionsUnderFile(node);
                }
            }

            function getFilesUnderDirectory(dirNode) {
                // Prefix filter with "@" to prevent Duplicati from mistaking literal '*' and '?' characters in paths for glob wildcard characters
                AppService.get(`/backup/${$scope.ngBackupId}/files/${encodeURIComponent(dirNode.id)}?prefix-only=false&folder-contents=true&time=${encodeURIComponent($scope.ngTimestamp)}&filter=${encodeURIComponent('@' + dirNode.id)}`).then(function (data) {
                    let children = []

                    for (const item of data.data.Files) {
                        let text = item.Path.substr(dirNode.id.length);
                        let isDirectory = false;

                        if (text.slice(-1) == dirsep) {
                            text = text.substr(0, text.length - 1);
                            isDirectory = true;
                        }

                        children.push({
                            text: text,
                            id: item.Path,
                            compareId: compareablePath(item.Path),
                            size: item.Sizes[0],
                            iconCls: isDirectory ? '' : 'x-tree-icon-file',
                            entrytype: AppUtils.getEntryTypeFromIconCls(isDirectory ? '' : 'x-tree-icon-file'),
                            parent: dirNode,
                            nodeType: isDirectory ? 'dir' : 'file',
                            leaf: false,
                            include: ''
                        });
                    }

                    dirNode.children = children;
                    dirNode.loading = false;

                    updateNodesFromMap(children);
                }, function () {
                    dirNode.loading = false;
                    dirNode.expanded = false;
                    AppUtils.connectionError.apply(AppUtils, arguments);
                });
            }

            function getVersionsUnderFile(fileNode) {
                AppService.get(`/backup/${$scope.ngBackupId}/fileversions?file=${encodeURIComponent(fileNode.id)}`).then(function(data) {
                    let children = []

                    for(const version of data.data.FileVersions)
                    {
                        const id = data.data.file + dirsep + '&fileid=' + version.FileId;

                        children.push({
                            text: moment(version.LastModified).format('L LT'),
                            id: id,
                            compareId: compareablePath(id),
                            size: AppUtils.formatSizeString(version.FileSize),
                            iconCls: 'x-tree-icon-leaf',
                            entrytype: AppUtils.getEntryTypeFromIconCls('x-tree-icon-leaf'),
                            leaf: true,
                            include: '',
                            parent: fileNode,
                            nodeType: 'version',
                            timestamp: version.Timestamp
                        });
                    }

                    fileNode.children = children;
                    fileNode.loading = false;

                    setDefaultVersionNode(fileNode);

                    updateNodesFromMap(children);
                }, function() {
                    fileNode.loading = false;
                    fileNode.expanded = false;
                    AppUtils.connectionError.apply(AppUtils, arguments);
                });
            }

            $scope.toggleSelected = function(node) {
                if (scope.selectednode != null)
                    scope.selectednode.selected = false;

                scope.selectednode = node;
                scope.selectednode.selected = true;
            };

            function forAllChildren(node, includeVersions, callback) {
                let q = [node];
                while (q.length > 0) {
                    let x = q.pop();
                    if (x.children && (x.nodeType === 'dir' || (includeVersions && x.nodeType === 'file')))
                        for (const c in x.children) {
                            q.push(x.children[c]);
                            callback(x.children[c]);
                        }
                }
            }

            function propagateDeselectDown(startNode) {
                if (!startNode.children)
                    return;

                forAllChildren(startNode, true, function (node) {
                    node.include = startNode.include;
                });
            }

            function propagateSelectDown(startNode) {
                if (!startNode.children)
                    return;

                forAllChildren(startNode, false, function (node) {
                    node.include = startNode.include;

                    if (node.children !== undefined && node.nodeType === 'file') {
                        const defaultVersion = node.children.find(version => version.isDefault);

                        for (const version of node.children) {
                            if ($scope.ngSelected[version.compareId] || (version === defaultVersion && !anyDirectChildSelected(node))) {
                                version.include = node.include;
                            } else {
                                version.include = '';
                            }
                        }
                    }
                });
            }

            function anyDirectChildSelected(node) {
                if (node.children === undefined)
                    return false;

                for (const child of node.children) {
                    if (child.compareId in $scope.ngSelected)
                        return true
                }

                return false;
            }

            function buildPartialMap() {
                partialMap.clear();

                for (const selectedPath in $scope.ngSelected) {
                    const parts = selectedPath.split(dirsep);
                    if (parts.length < 1)
                        continue;

                    const isDir = selectedPath.slice(-1) == dirsep;
                    const isVersion = parts[parts.length - 1].startsWith('&fileid=');
                    const isFile = !isDir && !isVersion;
                    let path = '';

                    for (let j = 0; j < parts.length; j++) {
                        // The full path of a file or version does not need to be in the partial map
                        if (!isDir && j == parts.length - 1) {
                            break;
                        }

                        path += parts[j];
                        if (isDir || isFile || (isVersion && j != parts.length - 2))
                            path += dirsep;

                        partialMap.set(path, true);
                    }
                }
            }

            function setDefaultVersionNode(fileNode) {
                let previousVersion = null;

                for (const version of fileNode.children) {
                    if (previousVersion === null) {
                        if ($scope.ngTimestamp >= version.timestamp) {
                            version.isDefault = true;
                            return;
                        }
                    }
                    else if ($scope.ngTimestamp < previousVersion.timestamp && $scope.ngTimestamp >= version.timestamp) {
                        version.isDefault = true;
                        return;
                    }

                    previousVersion = version;
                }

                if (fileNode.children.length > 0)
                    fileNode.children[0].isDefault = true;
            }

            $scope.toggleCheck = function (node) {
                if (node.include != '+') {
                    let parent = node.parent || node;
                    let child = node;

                    while (parent != null) {
                        if (child.nodeType === 'version') {
                            const fileNode = child.parent;
                            const anyVersionSelected = anyDirectChildSelected(fileNode);

                            if (anyVersionSelected) {
                                $scope.ngSelected[child.compareId] = true;
                                return;
                            }

                            const defaultVersion = fileNode.children.find(version => version.isDefault);
                            if (defaultVersion !== child) {
                                $scope.ngSelected[child.compareId] = true;

                                if (anyParentSelected(child)) {
                                    swapDefaultVersionToDirectlySelected(fileNode);
                                    return;
                                }
                            }

                            // Pretend like the file was clicked to let propagation logic work
                            child = parent;
                            parent = parent.parent;
                        }

                        removePathAndSubPathsForAddition(child);

                        let allSelectedForParent = true;
                        if (parent.children) {
                            for (const item of parent.children) {
                                if (item.compareId !== child.compareId && !(item.compareId in $scope.ngSelected || (item.nodeType === 'file' && anyDirectChildSelected(item)))) {
                                    allSelectedForParent = false;
                                    break;
                                }
                            }
                        }

                        if (!allSelectedForParent || parent == node || $scope.ngSearchMode) {
                            if (child.nodeType === 'file')
                                selectFile(child);
                            else
                                $scope.ngSelected[child.compareId] = true;
                            break;
                        }

                        child = parent;
                        parent = parent.parent;

                        if (parent == null && allSelectedForParent && !$scope.ngSearchMode) {
                            removePathAndSubPathsForAddition(child);
                            $scope.ngSelected[child.compareId] = true;
                        }
                    }
                } else {
                    if (node.nodeType === 'version') {
                        delete $scope.ngSelected[node.compareId];

                        let anyVersionSelected = false;
                        let numberSelected = 0;
                        for (const version of node.parent.children) {
                            if (version.compareId in $scope.ngSelected) {
                                anyVersionSelected = true;
                                numberSelected++;
                            }
                        }

                        if (anyVersionSelected) {
                            if (numberSelected == 1) {
                                swapDefaultVersionToDefaultSelected(node.parent);
                            }

                            return;
                        }

                        // Pretend like the file was clicked to let propagation logic work
                        node = node.parent;
                        if (!anyParentSelected(node)) {
                            $scope.ngSelected[node.compareId] = true;
                        }
                    } else if (node.children && node.nodeType === 'file') {
                        for (const version of node.children) {
                            delete $scope.ngSelected[version.compareId];
                        }

                        // Pretend like the file was clicked to let propagation logic work
                        if (!anyParentSelected(node)) {
                            $scope.ngSelected[node.compareId] = true;
                        }
                    }

                    // This item is no longer included, include remainder of parents children
                    let backtrace = [];
                    let parent = node;

                    while (parent != null && !(parent.compareId in $scope.ngSelected)) {
                        backtrace.push(parent);
                        parent = parent.parent;
                    }

                    delete $scope.ngSelected[parent.compareId];

                    if (parent.nodeType === 'dir') {
                        // Remove any sub paths. i.e. version nodes since they are always selected independently
                        let selectedToDelete = []
                        for (const selected in $scope.ngSelected) {
                            if (selected.indexOf(node.compareId) == 0) {
                                selectedToDelete.push(selected);
                            }
                        }

                        for (const selected of selectedToDelete) {
                            delete $scope.ngSelected[selected];
                        }
                    }

                    while (backtrace.length > 0) {
                        let backtraceNode = backtrace.pop();
                        for (const child of parent.children) {
                            if (backtraceNode != child) {
                                if (child.nodeType === 'file') {
                                    selectFile(child);
                                } else {
                                    $scope.ngSelected[child.compareId] = true;
                                }
                            }
                        }

                        parent = backtraceNode;
                    }
                }
            };

            function selectFile(fileNode) {
                if(fileNode.children === undefined) {
                    $scope.ngSelected[fileNode.compareId] = true;
                    return;
                }

                for (const version of fileNode.children) {
                    if (version.compareId in $scope.ngSelected)
                        return;
                }

                $scope.ngSelected[fileNode.compareId] = true;
            }

            function swapDefaultVersionToDirectlySelected(fileNode) {
                delete $scope.ngSelected[fileNode.compareId];

                const version = fileNode.children.find(version => version.isDefault);
                $scope.ngSelected[version.compareId] = true;
            }

            function swapDefaultVersionToDefaultSelected(fileNode) {
                const defaultVersion = fileNode.children.find(version => version.isDefault);

                if (defaultVersion.compareId in $scope.ngSelected) {
                    delete $scope.ngSelected[defaultVersion.compareId];

                    if (fileNode.parent.compareId in $scope.ngSelected) {
                        return;
                    }

                    if (partialMap.has(fileNode.parent.compareId)) {
                        $scope.ngSelected[fileNode.compareId] = true;
                    }
                }
            }

            function removePathAndSubPathsForAddition(node) {
                let selectedToDelete = [];

                for (const selected in $scope.ngSelected) {
                    // If adding a new file or dir, versions should not be removed
                    if (selected.includes('&fileid=')) {
                        continue
                    }

                    if ((node.nodeType === 'dir' && selected.indexOf(node.compareId) == 0) || selected === node.compareId) {
                        selectedToDelete.push(selected);
                    }
                }

                for (const selected of selectedToDelete) {
                    delete $scope.ngSelected[selected];
                }
            }

            function updateNodesFromMap(nodes) {
                const toUpdate = [];
                toUpdate.push.apply(toUpdate, nodes || $scope.treedata.children);

                while (toUpdate.length > 0) {
                    const node = toUpdate.pop();

                    if (node.compareId in $scope.ngSelected) {
                        node.include = '+';
                        if (node.nodeType === 'file') {
                            includeVersionNodesFromMap(node);
                        } else {
                            propagateSelectDown(node);
                        }
                    } else if (partialMap.has(node.compareId)) {
                        if (node.nodeType === 'file') {
                            node.include = '+';
                            includeVersionNodesFromMap(node);
                        } else {
                            node.include = ' ';
                            if (node.children)
                                toUpdate.push.apply(toUpdate, node.children);
                        }
                    } else if(anyParentSelected(node)) {
                        node.include = '+';

                        if (node.nodeType === 'version') {
                            const defaultVersion = node.parent.children.find(version => version.isDefault);
                            if (defaultVersion !== node)
                                node.include = '';
                            continue;
                        }
                        else {
                            if (node.nodeType === 'file') {
                                includeVersionNodesFromMap(node);
                            } else {
                                propagateSelectDown(node);
                            }
                        }
                    } else {
                        node.include = '';
                        propagateDeselectDown(node);
                    }
                }
            }

            function anyParentSelected(node) {
                let parent = node.parent;
                while(parent) {
                    if (parent.compareId in $scope.ngSelected)
                        return true;
                    parent = parent.parent;
                }

                return false;
            }

            function includeVersionNodesFromMap(fileNode) {
                if (!fileNode.children || fileNode.children.length === 0)
                    return;

                let any = false;
                for (const version of fileNode.children) {
                    if (version.compareId in $scope.ngSelected) {
                        version.include = '+';
                        any = true;
                    } else {
                        version.include = '';
                    }
                }

                if (!any)
                {
                    const defaultVersion = fileNode.children.find(version => version.isDefault);
                    defaultVersion.include = '+';
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
