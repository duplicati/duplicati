backupApp.controller('DialogController', function($scope, DialogService, gettextCatalog) {
    $scope.state = DialogService.watch($scope);

    function showTooltip(elem, msg) {
        elem.addEventListener('mouseleave', function(e) {
            e.currentTarget.setAttribute('class', 'button');
            e.currentTarget.removeAttribute('aria-label');
        });

        elem.setAttribute('class', 'button tooltipped tooltipped-w');
        elem.setAttribute('aria-label', msg);
    }

    $scope.onCopySuccess = function(e) {
        e.clearSelection();
        showTooltip(e.trigger, gettextCatalog.getString('Copied!'));
    };

    $scope.onCopyError = function(e) {
        showTooltip(e.trigger, gettextCatalog.getString('Copy failed. Please manually copy the URL'));
    };

    $scope.onButtonClick = function(index) {
        var cur = $scope.state.CurrentItem;
        var input = cur.textarea;
        DialogService.dismissCurrent();

        if (cur.callback)
            cur.callback(index, input, cur);
    };
    
});
