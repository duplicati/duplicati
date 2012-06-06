Ext.application({
    requires: [
    	'Ext.container.Viewport', 
    	'Ext.layout.container.Border', 
    	'BackupApp.Service', 
    	'BackupApp.Utility', 
    	'BackupApp.ListUtility',
		'BackupApp.view.AboutWindow',
		'BackupApp.view.LostConnectionWindow'
    ],

    name: 'BackupApp',
	appFolder: 'backup-app',
	
	controllers: [
		'Schedules',
		'StatuswindowHeader',
		'StatuswindowFooter'
	],

	//This object encapsulates all access to the web service
	service: null,
		
    launch: function() {

		Ext.log = function(msg) { 
			console.log(msg); 
		}

		//Pretend the app fires events too
    	this.addEvents('current-state-updated', 'count-down-pause-timer');
    	
		//Make a service object
		this.service = new BackupApp.Service();
		//Make the service globally accesible
		BackupApp.service = this.service;
    	BackupApp.instance = this;
    	BackupApp.utility = this.utility;
    	
		//Pretend the app can fire the update event, this helps with initialization in controllers
    	this.service.on({
    		'current-state-updated': function(e) { 
	    		var updatedTitle = false;
	    		if (e.ActiveScheduleId >= 0) {
	    			var store = Ext.getStore('Schedules');
	    			if (store != null) {
		    			var index = store.find('ID', e.ActiveScheduleId);
		    			if (index >= 0) {
		    				updatedTitle = true;
		    				document.title = 'Duplicati backup - running ' + store.getAt(index).get('Name');
		    			}
	    			}
	    		}
	    		
	    		if (!updatedTitle)
	    		{
	    			if (e.SuggestedStatusIcon == 'Paused')
		    			document.title = 'Duplicati backup - Paused';
	    			else
		    			document.title = 'Duplicati backup - Ready';
	    		}
	    			
	    		this.fireEvent('current-state-updated', e);
	    	},
	    	
	    	'lost-connection': function() {
	    		BackupApp.view.LostConnectionWindow.show();
	    	},

	    	'reconnected': function() {
	    		BackupApp.view.LostConnectionWindow.hide();
	    	},
	    	
	    	'lost-connection-retry': function() {
	    		BackupApp.view.LostConnectionWindow.setStatusAsRunning();
	    	},

	    	'lost-connection-retry-delay': function(seconds) {
	    		BackupApp.view.LostConnectionWindow.setStatusAsWaiting(seconds);
	    	},

			'count-down-pause-timer': function(e) { 
    			BackupApp.instance.fireEvent('count-down-pause-timer', e); 
    		},	    	
	    	scope: this
    	});

		//Fetch the current status
    	this.service.updateCurrentState();
    
        Ext.create('Ext.container.Viewport', {
            layout: 'border',
            items: [{
            	region: 'center',
            	xtype: 'schedulelist'
            },{
            	region: 'north',
            	xtype: 'statuswindowheader'
            },{
            	region: 'south',
            	xtype: 'statuswindowfooter'
            }]
        });
    }
});