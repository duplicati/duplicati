{
// This helps in a debug setup
var extdefined = false;
try { extdefined = Ext !== undefined; } catch (e) {}
if (!extdefined) { alert('ExtJS failed to load!\n If this is a debug build,\nplease download and extract:\n\nExtJS 4.2.0\n\ninto the folder:\nDuplicati/Server/webroot/extjs-4.2.0'); }
delete extdefined;
}

Ext.application({
    requires: [
		'Ext.container.Viewport', 
		'Ext.layout.container.Border', 
		'Duplicati.Service', 
		'Duplicati.Utility', 
		'Duplicati.ListUtility',
		'Duplicati.view.AboutWindow',
		'Duplicati.view.LostConnectionWindow',

		// Override for handling nested Model 2 Form
		'Duplicati.view.override.BackupConfig',
		'Duplicati.model.override.BackupJob'
    ],
	name: 'Duplicati',
	appFolder: 'Duplicati',

	controllers: [
		'Schedules',
		'StatuswindowHeader',
		'StatuswindowFooter',
		'BackupConfig'
	],

	stores: [
		'LabelStore',
		'EncryptionMethodStore',
		'AdditionalPathStore',
		'MyComputerTreeStore',
		'BackupJob',
		'DefaultTimeRangesStore',
		'NewBackupChainWhenStore',
		'DeleteOldChainsWhenStore',
		'DefaultSizeRangeStore'
	],

	views: [
		'BackupConfig',
		'wndAddLocation',
		'wndAddFilter',
		'wndCreateConnection'
	],

	models: [
        'BackupJob',
        'Schedule',
    	'Task',
        'TaskItems.BackupLocation',
        'TaskItems.CompressionSettings',
    	'TaskItems.EncryptionSettings',
        'TaskItems.Overrides',
    	'TaskItems.Extensions',
        'TaskItems.BackendSettings',
    	'ScheduleItems.Metadata'
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
		this.service = new Duplicati.Service();
		//Make the service globally accesible
		Duplicati.service = this.service;
    	Duplicati.instance = this;
    	Duplicati.utility = this.utility;
    	
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
	    		Duplicati.view.LostConnectionWindow.show();
	    	},

	    	'reconnected': function() {
	    		Duplicati.view.LostConnectionWindow.hide();
	    	},
	    	
	    	'lost-connection-retry': function() {
	    		Duplicati.view.LostConnectionWindow.setStatusAsRunning();
	    	},

	    	'lost-connection-retry-delay': function(seconds) {
	    		Duplicati.view.LostConnectionWindow.setStatusAsWaiting(seconds);
	    	},

			'count-down-pause-timer': function(e) { 
    			Duplicati.instance.fireEvent('count-down-pause-timer', e); 
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
            }],

		renderTo: Ext.getBody()
	
        });
    }
});
