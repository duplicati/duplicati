backupApp.factory('PauseModal', function (btfModal) {
  return btfModal({
    controller: 'PauseController',
    templateUrl: 'templates/pause.html'
  });
});