Ext.define('BackupApp.controller.Schedules', {
    extend: 'Ext.app.Controller',
	stores: [ 'Schedules' ],
	views: [ 'schedule.List' ],

	refs: [{
		selector: 'viewport > panel',
		ref: 'basicPanel'
	}],

	lastEventID: -1,

    init: function() {
        this.control({
            'viewport > panel': {
                render: this.onPanelRendered
            }
        });
                
        this.application.on({
        	'current-state-updated': function(data) { 
        			        	
	        	//Update the data store if there is new metadata
	        	if (data.LastEventID != this.lastEventID) 
	        		this.getSchedulesStore().load();
	        	
				this.lastEventID = data.LastEventID;	        		
	        },
	        scope: this
        });
    },

    onPanelRendered: function() {
        //console.log('The panel was rendered');
    }
});