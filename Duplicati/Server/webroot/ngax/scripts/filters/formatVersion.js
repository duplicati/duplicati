backupApp.filter('formatVersion', function () {
    return function (versionNode) {
        return `${versionNode.text}&emsp;${versionNode.size}`;
    };
});
