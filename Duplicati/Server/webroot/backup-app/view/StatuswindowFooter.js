Ext.define('BackupApp.view.StatuswindowFooter' ,{
    extend: 'Ext.panel.Panel',
    alias : 'widget.statuswindowfooter',

	title : 'Status line',
	header: false,
	border: 0,
	layout: 'column',
	cls: 'footer-panel',
	
	items: [{
		xtype: 'label',
		html: 'A <a href="http://www.duplicati.com/news/duplicati132available" target="_blank">new version</a> is available',
		handler: function() { window.open('https://www.facebook.com/pages/Duplicati/105118456272281') },
		columnWidth: 1,
		margin: 10
	},{
		xtype: 'button',
		text: 'Facebook',
		handler: function() { window.open('https://www.facebook.com/pages/Duplicati/105118456272281') },
		margin: 10
	},{
		xtype: 'button',
		text: 'Google+',
		handler: function() { window.open('https://plus.google.com/105271984558189185842/posts') },
		margin: 10
	},{
		xtype: 'button',
		text: 'Donate!',
		handler: function() { window.open('https://www.paypal.com/cgi-bin/webscr?cmd=_xclick&business=paypal%40hexad%2edk&item_name=Duplicati%20Donation&no_shipping=2&no_note=1&tax=0&currency_code=EUR&bn=PP%2dDonationsBF&charset=UTF%2d8') },
		margin: 10
	}],

	initComponent: function() {
        this.callParent(arguments);
    }
});