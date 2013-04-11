Ext.define('Duplicati.Service', {
    requires: [
		'Duplicati.CountDownHelper'
	],
    mixins: {
        observable: 'Ext.util.Observable'
    },

	controlUrl: '/control.cgi',
	pauseCountDownHelper: null,
	lostConnectionCountDownHelper: null,
	updateRequestParams: {
		action: 'get-current-state',
		longpoll: true,
		lasteventid: '-1',
		duration: '5m'
	},
	updateRequestRunning: false,
	updateRequestFailureCount: 0,

	//Cache for server responses
	serverdata: {
		'current-state': null,
		'installed-backends': null,
		'installed-compression-modules': null,
		'installed-encryption-modules': null,
		'installed-generic-modules': null,
		'system-info': null
	},
    
    constructor: function(config) {
		this.mixins.observable.constructor.call(this, config);
    	this.addEvents('current-state-updated', 'count-down-pause-timer', 'lost-connection', 'reconnected', 'lost-connection-retry', 'lost-connection-retry-delay');
    	this.lostConnectionCountDownHelper = new Duplicati.CountDownHelper();
    	this.pauseCountDownHelper = new Duplicati.CountDownHelper();
    	
    	this.pauseCountDownHelper.on({
    		'count-down-tick': function(secondsleft) { 
    				this.fireEvent('count-down-pause-timer', secondsleft); 
    		},
    		'count-down-complete': function() { 
    			this.fireEvent('count-down-pause-timer', 0); 
    		},
    		scope: this
    	});
    	
    	this.lostConnectionCountDownHelper.on({
    		'count-down-tick': function(seconds) { 
    			this.fireEvent('lost-connection-retry-delay', seconds); 
    		},
    		'count-down-complete': function() { 
    			this.updateCurrentState(); 
    			this.fireEvent('lost-connection-retry'); 
    		},
    		scope: this
    	})
    	
    	this.loadSystemInfo(false);
    },
    
    loadSystemInfo: function(reload) {
    	
    	if (reload || this.serverdata['system-info'] == null) {
	    	
			Ext.Ajax.request({
				url: this.controlUrl,
				params: {action: 'system-info'},
				scope: this,
				success: function(response) {
					var res = eval('(' + response.responseText + ')');
					if (res != null)
						this.serverdata['system-info'] = res;
						
					if (this.serverdata['system-info'] == null)
						window.setTimeout(this.loadSystemInfo, 1000);
				},
				
				error: function() {
					if (this.serverdata['system-info'] == null)
						window.setTimeout(this.loadSystemInfo, 1000);
				}
			});
		}
    },
    
    reconnectNow: function() {
    	if (this.lostConnectionCountDownHelper.isRunning()) {
    		this.lostConnectionCountDownHelper.stop(true);
    	}
    },
	
	updateCurrentState: function(force) {
		if (this.updateRequestRunning && !force)
			return;
		
		var params = force ? { action: 'get-current-state' } : this.updateRequestParams;
		var timeout = force ?  5000 : (1000 * ((60 * 5) + 5));
		
		if (!force)
		{
			this.updateRequestRunning = true;
			if (this.updateRequestFailureCount > 0)
			{
				//Last request failed, so lets retry,
				// with a short timeout and make sure we
				// don't wait for an update 
				timeout = 10000; //10 sec
				this.updateRequestParams.lasteventid = -1;
			}
		}
		
		Ext.Ajax.request({
			url: this.controlUrl,
			params: params,
			timeout: timeout,
			scope: this,
			success: function(response) {
				var res = eval('(' + response.responseText + ')');
				if (res != null)
				{
					if (this.updateRequestFailureCount >= 2)
						this.fireEvent('reconnected');
						
					this.updateRequestFailureCount = 0;
					Ext.log('Got update: ' + res.LastEventID + ', last: ' + this.updateRequestParams.lasteventid);
					var realUpdate = this.updateRequestParams.lasteventid != res.LastEventID;
					this.serverdata['current-state'] = res;
					this.updateRequestParams.lasteventid = res.LastEventID;

					//Register next request
					if (!force) 
					{
						this.updateRequestRunning = false;
						this.updateCurrentState();
					}

					if (realUpdate)
						this.fireEvent('current-state-updated', this.serverdata['current-state']);
					if (this.getExpectedPauseEnd() != null)
						this.pauseCountDownHelper.start(this.getExpectedPauseEnd());
					else if (this.pauseCountDownHelper.isRunning())
						this.pauseCountDownHelper.stop(true);
			
				}					
			},
			failure: function(response, opts) {
				//Register next request
				if (!force) 
				{
					Ext.log('Failure in longpoll: ' + response.status + ', timeout: ' + response.timedout);

					this.updateRequestFailureCount++;
					this.updateRequestRunning = false;

					if (this.updateRequestFailureCount == 2)
						this.fireEvent('lost-connection');

					if (this.updateRequestFailureCount >= 2) {
						
						this.lostConnectionCountDownHelper.start(Ext.Date.add(new Date(), Ext.Date.SECOND, 30));
					}
					else
					{
						this.updateCurrentState();
					}
				}
			}
		});
	},
			
	getExpectedPauseEnd: function() {
		var pauseEnd = this.serverdata['current-state'].EstimatedPauseEnd;
		if (pauseEnd != null) {
			pauseEnd = Duplicati.Utility.parseJsonDate(pauseEnd);
			if (pauseEnd != null && pauseEnd.getTime() > new Date().getTime())
				return pauseEnd;
		}
		return null;
	},
		
	getSchedulePosition: function(id) {
		var state = this.serverdata['current-state'];
		if (state == null)
			return -1;

		if (id == state.ActiveScheduleId)
			return 0;
		else if (state.SchedulerQueueIds != null)
			for(var i = 0; i < state.SchedulerQueueIds.length; i++)
				if (state.SchedulerQueueIds[i] == id)
					return i+1;
					
		return -1;
	},
	
	pause: function(duration) {
		var self = this;
		Ext.Ajax.request({
			url: this.controlUrl,
			params: { 
				action: 'send-command',
				command: 'pause',
				duration: duration
			},
			success: function(response) { }
		});
	},

	resume: function() {
		var self = this;
		Ext.Ajax.request({
			url: this.controlUrl,
			params: { 
				action: 'send-command',
				command: 'resume'
			},
			success: function(response) { }
		});
	},
	
	isPaused: function() {
		var state = this.serverdata['current-state'];
		if (state != null)
			return state.ProgramState != 'Running';
		
		return true;
	},
	
	togglePause: function() {
		if (this.isPaused())
			this.resume();
		else
			this.pause();
	},

	getInstalledCompressionModules: function(callback) {
		if (this.serverdata['installed-compression-modules'] == null) {
			var self = this;
			Ext.Ajax.request({
				url: this.controlUrl,
				params: { action: 'list-installed-compression-modules' },
				success: function(response) { 
					self.serverdata['installed-compression-modules'] = eval('(' + response.responseText + ')');
					callback(self.serverdata['installed-compression-modules']);
				}
			});
		} else {
			callback(this.serverdata['installed-compression-modules']);
		}
	},
	
	getInstalledGenericModules: function(callback) {
		if (this.serverdata['installed-generic-modules'] == null) {
			Ext.Ajax.request({
				url: this.controlUrl,
				params: { action: 'list-installed-generic-modules' },
				success: function(response) { 
					self.serverdata['installed-generic-modules'] = eval('(' + response.responseText + ')');
					callback(self.serverdata['installed-generic-modules']);
				}
			});
		} else {
			callback(this.serverdata['installed-generic-modules']);
		}
	},

	getInstalledEncryptionModules: function(callback) {
		if (this.serverdata['installed-encryption-modules'] == null) {
			Ext.Ajax.request({
				url: this.controlUrl,
				params: { action: 'list-installed-encryption-modules' },
				success: function(response) { 
					self.serverdata['installed-encryption-modules'] = eval('(' + response.responseText + ')');
					callback(self.serverdata['installed-encryption-modules']);
				}
			});
		} else {
			callback(this.serverdata['installed-encryption-modules']);
		}
	},
	
	runBackup: function(id, full) {
		var self = this;
		
		Ext.Ajax.request({
			url: this.controlUrl,
			params: { 
				action: 'send-command',
				command: 'run-backup',
				id: id,
				full: full 
			},
			success: function(response) {
			}
		});
		
	}

});