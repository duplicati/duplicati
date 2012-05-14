$(document).ready(function(){

	$('#appname').each(function(i, e) { e.innerHTML =  APP_SCOPE.AppName;});
	jQuery.timeago.settings.allowFuture = true;

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
	
	APP_SCOPE.getCurrentState(function(data) {
		APP_SCOPE.currentState = data;
		APP_SCOPE.afterInitialRequest();
	});
		
	if (APP_SCOPE.external)
		APP_SCOPE.external.setMinSize(775, 200);
		
	APP_SCOPE.add_new_backup = function()
	{
		window.activateFunction("new-backup");
	}
	
	
	APP_SCOPE.scheduleHtmlTemplate = '' +
	    '<div id="%id%" class="status-window-row %oddorevenclass%">' +
		'    <div class="status-window-row-inner status-window-row-inner-left" >' +
		'        <div class="status-window-row-inner-content">%schedule%</div>' +
		'    </div>' +
		'    <div class="status-window-row-inner status-window-row-inner-middle">' +
		'        <div>' +
		'            <div class="ui-state-default ui-corner-all vertical-center status-window-name-bubble"><div><span>%name%</span><span class="ui-button-icon-primary ui-icon ui-icon-triangle-1-s" style="display: inline-block"></span></div></div>' +
		'            <div class="ui-state-default ui-corner-all vertical-center status-window-detail-area">' +
		'                <div>%metadata%</div>' +
		'                <div>%labels%</div>' +
		'            </div>' +
		'        </div>' +
		'    </div>' +
		'    <div class="status-window-row-inner status-window-row-inner-right">' +
		'        <div class="status-window-row-inner-content">' +
		'            <div>%error%</div>' +
		'        </div>' +
		'    </div>' +
		'</div>';

	
});

(function(){
	APP_SCOPE.afterInitialRequest = function() {
		//Check that all required data is loaded, this prevents
		if (this.backupSchedules != null && this.applicationSettings != null && this.currentState != null) {
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

			var templateFilled = APP_SCOPE.scheduleHtmlTemplate.replace("%name%", sc.Name).replace("%id%", "id").replace("%oddorevenclass%", oddClass);

			var labels = '';
			if ((sc.Tags != null && sc.Tags != '') || (sc.Path != null && sc.Path != ''))
			{
				labels = 'Labels: ';
				if (sc.Tags != null && sc.Tags != '')
					labels += sc.Tags;
				if (sc.Path != null && sc.Path != '')
					labels += sc.Path;
			}
			
			//TODO: Figure out what the scheduler state is
			var schedule = '';
			
			//Is the backup running?
			if (sc.ID == APP_SCOPE.currentState.ActiveScheduleId) {
				schedule = '<div>Running</div><div class="link">Cancel</div>';
			} else {
	
				//Is the backup in queue?
				var queueIndex = -1;
				if (APP_SCOPE.currentState.SchedulerQueueIds != null)
					for(var sci = 0; sci < APP_SCOPE.currentState.SchedulerQueueIds.length; sci++)
						if (APP_SCOPE.currentState.SchedulerQueueIds[sci] == sc.ID)
						{
							queueIndex = sci;
							break;
						}
						
				if (queueIndex >= 0) {
					schedule = "No. " + (queueIndex + 1) + " in queue";
				} else {
					//Is the backup scheduled?
					if (sc.NextScheduledTime != null)
					{
						var scdate = APP_SCOPE.toJsDate(sc.NextScheduledTime);
						schedule = '<span title="' + scdate.toLocaleString() + '">' + jQuery.timeago(scdate) + '</span>';
					}
				}

			}
			
			//TODO: Make some logic to extract the last error message
			var error = 'placeholder for error message';
			
			//TODO: Extract some metadata
			var metadata = 'placeholder for metadata';
			
			templateFilled = templateFilled.replace("%labels%", labels).replace("%schedule%", schedule).replace("%error%", error).replace("%metadata%", metadata);

			html += templateFilled;
		}
		
		
		container.innerHTML = html;
	};

}());

