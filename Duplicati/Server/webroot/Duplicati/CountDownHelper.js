Ext.define('Duplicati.CountDownHelper', {
    mixins: {
        observable: 'Ext.util.Observable'
    },

	statics: {
		callbackkeycounter: 0,
		callbacklookup: {},
		callbackhelper: function(id) {
			this.callbacklookup["" + id].onCallback();
		}
	},

	expireTime: null,
	intervalId : null,
	
	constructor: function(config) {
		this.mixins.observable.constructor.call(this, config);
    	this.addEvents('count-down-tick', 'count-down-complete');
    },

	isRunning: function() { 
		return this.intervalId != null; 
	},

	start: function(time) {
		if (this.intervalId != null)
			this.stop(false);

		this.expireTime = time;
		var selfid = Duplicati.CountDownHelper.callbackkeycounter++;
		
		Duplicati.CountDownHelper.callbacklookup["" + selfid] = this;
		this.intervalId = window.setInterval('Duplicati.CountDownHelper.callbackhelper(' + selfid + ');', 500);
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
		
		for(var k in Duplicati.CountDownHelper.callbacklookup) {
			if (Duplicati.CountDownHelper.callbacklookup[k] == this) {
				delete Duplicati.CountDownHelper.callbacklookup[k];
				break;
			}				
		}
		
		if (activateEvent || activateEvent === undefined)
			this.fireEvent('count-down-complete');
	}
	
})
