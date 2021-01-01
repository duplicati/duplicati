backupApp.directive('externalLink', function() {
  return {
    restrict: 'E',
    transclude: true,
    scope: {
        link: '=link',
        title: '=title'
    },
    templateUrl: 'templates/externallink.html'
}});
