Ext.define('BackupApp.CountDownHelper', {
    mixins: {
        observable: 'Ext.util.Observable'
    },

	statics: {
		callbacklookup: [],
		callbackhelper: function(id) {
			this.callbacklookup[id].onCallback();
		}
	},

	expireTime: null,
	intervalId : null,

	//Bugfix for extjs
	hasListeners: {},
	
	constructor: function(config) {
    	this.addEvents('count-down-tick', 'count-down-complete');
    },

	isRunning: function() { 
		return this.intervalId != null; 
	},

	start: function(time) {
		if (this.intervalId != null)
			this.stop(false);

		this.expireTime = time;
		BackupApp.CountDownHelper.callbacklookup.push(this);
		var selfid = BackupApp.CountDownHelper.callbacklookup.length - 1;
		this.intervalId = window.setInterval('BackupApp.CountDownHelper.callbackhelper(' + selfid + ');', 500);
	},
	
	onCallback: function() {
		var secondsLeft = parseInt((this.expireTime.getTime() - new Date().getTime()) / 1000);
		this.fireEvent('count-down-tick', Math.max(0, secondsLeft));
		if (secondsLeft <= 0)
			this.stop(true);
	},
	
	stop: function(activateEvent) {
		if (this.intervalId != null) {
			window.clearInterval(this.intervalId);
			this.intervalId = null;
		}
		
		for(var i = 0; i < BackupApp.CountDownHelper.callbacklookup.length; i++) {
			if (BackupApp.CountDownHelper.callbacklookup[i] == this) {
				BackupApp.CountDownHelper.callbacklookup.splice(i, 1);
				break;
			}				
		}
		
		if (activateEvent || activateEvent === undefined)
			this.fireEvent('count-down-complete');
	}
	
})
