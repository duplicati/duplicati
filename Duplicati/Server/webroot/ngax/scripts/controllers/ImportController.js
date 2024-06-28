backupApp.controller('ImportController', function($rootScope, $scope, $timeout, $location, AppService, DialogService, gettextCatalog) {
    $scope.Connecting = false;
    $scope.Completed = false;

    $scope.restoremode = $location.$$path.indexOf('/restore-import') == 0;
    $scope.form = {
        config: '',
        cmdline: false,
        import_metadata: false,
        direct: false,
        passphrase: ''
    };

    $scope.doSubmit = function() {
        var files = document.querySelector('#import-form > div > #config')?.files;
        if (files == null || files.length == 0) {
            DialogService.dialog(gettextCatalog.getString('Error'), gettextCatalog.getString('Please select a file to import'));
            return false;
        }

        var reader = new FileReader();
        $scope.Connecting = true;
        
        reader.onload = function () {
            $scope.form.config = btoa(String.fromCharCode.apply(null, new Uint8Array(reader.result)));
            AppService.postJson('/backups/import', $scope.form)
                .then(function (response) {
                    $scope.Connecting = false;

                    if (response.data != null && response.data.Id != null) {
                        $location.path('/');
                    } else if (response.data != null && response.data.data != null) {
                        $rootScope.importConfig = response.data.data;
                        if ($scope.restoremode)
                            $location.path('/restoredirect-import'); 
                        else
                            $location.path('/add-import');
                    } else {
                        DialogService.dialog(gettextCatalog.getString('Error'), gettextCatalog.getString('Failed to import: {{message}}', { message: AppService.responseErrorMessage(response) }));
                    }
                }, function (response) {
                    $scope.Connecting = false;
                    DialogService.dialog(gettextCatalog.getString('Error'), gettextCatalog.getString('Failed to import: {{message}}', { message: AppService.responseErrorMessage(response) }));
                });
        };
        
        reader.onerror = function (error) {
            $scope.Connecting = false;
            DialogService.dialog(gettextCatalog.getString('Error'), gettextCatalog.getString('Failed to read file: {{message}}', { message: error }));
        };

        reader.readAsArrayBuffer(files[0]);

        return false;
    };

});
