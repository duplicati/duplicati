backupApp.service('EditBackupService', function() {
    // Provide hooks for validation on save
    this.preValidate = function(scope) { return true; };
    this.postValidate = function(scope, continuation) { continuation(); };
});
