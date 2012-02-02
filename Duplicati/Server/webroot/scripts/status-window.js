$(document).ready(function(){

	APP_SCOPE.applyRoundedCornerClass();
	$('#appname').each(function(i, e) { e.innerHTML =  APP_SCOPE.AppName;});
	
	//Request some initial data for making the display
	// we execute these in parallel for efficiency
	APP_SCOPE.getApplicationSettings(function(data) {
		APP_SCOPE.applicationSettings = data;
		APP_SCOPE.afterInitialRequest();
	});

	APP_SCOPE.getBackupSchedules(function(data) {
		APP_SCOPE.backupSchedules = data;
		APP_SCOPE.afterInitialRequest();
	});
});

(function(){
	APP_SCOPE.afterInitialRequest = function() {
		//Check that all required data is loaded, this prevents
		if (this.backupSchedules != null && this.applicationSettings != null) {
			$('#modal-cover').hide();
			$('#progress-loader').hide();
			this.buildBackupDisplay();
		}
	};
	
	APP_SCOPE.buildBackupDisplay = function() {
		var container = $('#status-window-main-contents')[0];
		
		var html = '';
		
		for(var i = 0; i < APP_SCOPE.backupSchedules.length; i++) {
			var sc = APP_SCOPE.backupSchedules[i];
			var id = 'backup-schedule-' + sc.ID;
			var oddClass = (i % 2) == 1 ? 'odd' : 'even';
			html += '<div id="'+ id + '" class="backup-schedule ' + oddClass + '">';
			html += '<div class="backup-schedule-inner rounded-corner-box">';
			html += '<div class="backup-schedule-name">' + sc.Name + '</div>';
			if (sc.Tags != null && sc.Tags != '')
				html += '<div class="backup-schedule-tags">' + sc.Tags + '</div>';
			if (sc.Path != null && sc.Path != '')
				html += '<div class="backup-schedule-path">' + sc.Path + '</div>';
			html += '</div>';
			html += '</div>';
		}
		
		container.innerHTML = html;
		
		this.applyRoundedCornerClass();
	};

}());

