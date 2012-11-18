Ext.define('Duplicati.view.LostConnectionWindow', {
	statics: {
		dialogWindow: null,
		setStatusAsRunning: function() {
			if (this.dialogWindow != null) {
				var txt = this.dialogWindow.queryById('status-window-lost-connection-text');
				var btn = this.dialogWindow.queryById('status-window-lost-connection-retry-button');
				btn.setDisabled(true);
				txt.setText('Connection to server is lost, retrying ...');
			}
		},
		
		setStatusAsWaiting: function(seconds) {
			if (Duplicati.service.updateRequestRunning) 
			{ 
				this.setStatusAsRunning();
			}
			else
			{
				if (this.dialogWindow != null) {
					var txt = this.dialogWindow.queryById('status-window-lost-connection-text');
					var btn = this.dialogWindow.queryById('status-window-lost-connection-retry-button');
					txt.setText('Connection to server is lost, retry in ' + Duplicati.Utility.formatSecondsAsTime(seconds));
					btn.setDisabled(false);
				}
			}
		},
		
		hide: function() {
			if (this.dialogWindow != null)
				this.dialogWindow.hide();

		},
		
		show: function() {
			if (this.dialogWindow == null)
				this.dialogWindow = Ext.create('Ext.window.Window', {
				    title: 'Lost connection to server',
				    height: 200,
				    width: 400,
				    modal: true,
				    closable: false,
				    resizable: false,
				    buttonAlign: 'center',
				    closeAction: 'hide',
				    layout: 'fit',
				    items: [{ 
				        xtype: 'label',
				        id: 'status-window-lost-connection-text',
				        text: 'Connection to server is lost, retrying ...'
				    }],
				    buttons: [{
				     	xtype: 'button',
				     	id: 'status-window-lost-connection-retry-button',
				     	text: 'Retry now',
				     	disabled: true,
				     	handler: function() {
				     		Duplicati.service.reconnectNow();
						}
				    }]
				});
			
			this.dialogWindow.show();
		}
	}
});

