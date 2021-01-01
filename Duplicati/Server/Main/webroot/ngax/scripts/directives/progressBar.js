backupApp.directive('progressBar', function() {
  return {
    restrict: 'E',
    scope: {
        ngProgress: '=ngProgress',
        ngText: '=ngText'
    },
    templateUrl: 'templates/progressbar.html',
    controller: function($scope) {
        
    }
  }
});
