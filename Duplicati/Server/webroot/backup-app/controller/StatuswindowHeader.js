Ext.define('BackupApp.controller.StatuswindowHeader', {
    extend: 'Ext.app.Controller',
	views: [ 'StatuswindowHeader' ],
	
	refs: [{
		selector: '#status-window-header-pause-button',
		ref: 'pauseButton'
	}, {
		selector: '#status-window-header-pause-menu',
		ref: 'pauseMenu',
	}],

    init: function() {
		this.application.on({
			'current-state-updated': function() {
				var btn = this.getPauseButton();
				var mnu = this.getPauseMenu();
				var srv = this.application.service;
				var util = BackupApp.Utility;
				
				if (srv.isPaused()) {
					if (srv.getExpectedPauseEnd() != null)
						btn.setText('Resume (' + util.formatSecondsAsTime((srv.getExpectedPauseEnd().getTime() - new Date().getTime()) / 1000) + ')');
					else
						btn.setText('Resume');
					btn.setIconCls('backups-paused');
					mnu.setText('Resume Duplicati');
				} else {
					btn.setText('Pause');
					btn.setIconCls('backups-running');
					mnu.setText('Pause Duplicati');
				}
			}, 
			scope: this
		});

        this.application.on({
        	'count-down-pause-timer': function(data) {
				var btn = this.getPauseButton();
				var srv = this.application.service;
				var util = BackupApp.Utility;
				if (data <= 0)
					btn.setText('Pause');
				else
        			btn.setText('Resume (' + util.formatSecondsAsTime(data) + ')');
        	},
        	scope: this
        }); 
		
        this.control({
            'viewport > panel': {
                render: this.onPanelRendered
            }
        });
    },

    onPanelRendered: function() {
        //console.log('The header panel was rendered');
    }
});