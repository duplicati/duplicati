backupApp.service('EditBackupService', function() {
	// Provide hooks for validation on save
	this.preValidate = function() { return true; };
	this.postValidate = function(continuation) { continuation(); };
});