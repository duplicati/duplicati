Ext.define('Duplicati.view.AboutWindow', {
	statics: {
		dialogWindow: null,
		show: function() {
			if (this.dialogWindow == null)
				this.dialogWindow = Ext.create('Ext.window.Window', {
				    title: 'About Duplicati',
				    height: 200,
				    width: 400,
				    modal: true,
				    resizable: false,
				    buttonAlign: 'center',
				    closeAction: 'hide',
				    layout: 'auto',
				    items: [{ 
				        xtype: 'label',
				        text: 'Duplicati 2.0'
				    }],
				    buttons: [{
				     	xtype: 'button',
				     	text: 'OK',
				     	handler: function() {
						   Duplicati.view.AboutWindow.dialogWindow.hide();
						}
				    }]
				});
			
			this.dialogWindow.show();
		}
	}
});

