backupApp.service('LogService', function(AppService, DialogService, gettextCatalog) {
    var loadingData = false;

    this.LoadMoreData = async function(url, current, idfield, pageSize) {
        if (loadingData)
            return;

        var last = null;
        if (current != null && current.length > 0 )
            last = current[current.length - 1][idfield];

        loadingData = true;
        try {
            const resp = await AppService.get(url + '?pagesize=' + pageSize + (last == null ? '' : ('&offset=' + last)));
            if (current == null)
                current = [];
            current.push.apply(current, resp.data);
            loadingData = false;
            // $scope[key + 'Complete'] = resp.data.length < PAGE_SIZE;
            // if ($scope.BackupID != null)
            //     $scope.Backup = BackupList.lookup[$scope.BackupID];
            return { current, complete: resp.data.length < pageSize };
        } catch (err) {
            console.log(err);
            var message = err.statusText;
            if (err.data != null && err.data.Message != null)
                message = err.data.Message;

            loadingData = false;
            DialogService.dialog('Error', gettextCatalog.getString('Failed to connect: {{message}}', { message: message }));
        };        
    };
});
