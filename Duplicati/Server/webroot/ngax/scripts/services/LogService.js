backupApp.service('LogService', function(AppService, DialogService, gettextCatalog) {
    var loadingData = false;
    
    this.LoadMoreData = function(url, current, idfield, pageSize) {
        return new Promise(function(resolve, reject) {
            if (loadingData) {
                return;
            }
            
            var last = null;
            if (current != null && current.length > 0 )
            last = current[current.length - 1][idfield];
            
            loadingData = true;	
            AppService.get(url + '?pagesize=' + pageSize + (last == null ? '' : ('&offset=' + last))).then(	
                function(resp) { 	
                    if (current == null)	
                    current = [];	
                    current.push.apply(current, resp.data);	
                    loadingData = false;	
                    // $scope[key + 'Complete'] = resp.data.length < pageSize;	
                    // if ($scope.BackupID != null)	
                    //     $scope.Backup = BackupList.lookup[$scope.BackupID];	
                    resolve({ current: current, complete: resp.data.length < pageSize });
                }, function(resp) {	
                    var message = resp.statusText;	
                    if (resp.data != null && resp.data.Message != null)
                    message = resp.data.Message;
                    
                    loadingData = false;
                    DialogService.dialog('Error', gettextCatalog.getString('Failed to connect: {{message}}', { message: message }));	
                });   
        });
    };
});
