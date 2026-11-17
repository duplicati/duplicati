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

        if (cur.validate && !cur.validate(index, input, cur))
            return;

        DialogService.dismissCurrent();
        if (cur.callback)
            cur.callback(index, input, cur);
    };

    // A dialog with a single button is dismissed with Enter, like clicking its button
    function hasSingleButton(cur) {
        return cur != null && cur.title != null && !cur.buttonTemplate
            && cur.buttons != null && cur.buttons.length == 1;
    }

    function onKeyDown(e) {
        if (e.key !== 'Enter' || e.repeat || e.isComposing || e.altKey || e.ctrlKey || e.metaKey || e.shiftKey)
            return;
        if (!hasSingleButton($scope.state.CurrentItem))
            return;

        // Keys pressed inside the dialog, such as in a text field, keep their usual meaning
        var dialog = document.querySelector('div.modal-dialog');
        if (dialog != null && dialog.contains(e.target))
            return;

        // The page behind the dialog must not see the key, or a focused button could reopen the dialog
        e.preventDefault();
        e.stopPropagation();
        $scope.$apply(function() {
            $scope.onButtonClick(0);
        });
    }

    document.addEventListener('keydown', onKeyDown, true);
    $scope.$on('$destroy', function() {
        document.removeEventListener('keydown', onKeyDown, true);
    });
    
});
