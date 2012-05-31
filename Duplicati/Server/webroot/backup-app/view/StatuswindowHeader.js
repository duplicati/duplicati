Ext.define('BackupApp.view.StatuswindowHeader' ,{
    extend: 'Ext.panel.Panel',
    alias : 'widget.statuswindowheader',
    
	title : 'Main App controls',
	header: false,
	border: 0,
	layout: 'column',
	cls: 'header-panel',
	
	items: [{
		xtype: 'splitbutton',
		cls: 'main-button',
		text: 'Duplicati',
		id: 'status-window-header-main-button',
		margin: 10,
		
		handler: function() { this.showMenu(); },
		menu: [{
			id: 'status-window-header-pause-menu',
			text: 'Pause Duplicati',
			handler: function() { BackupApp.service.togglePause(); }
		},{
			text: 'Pause for x minutes',
			menu: [{
				text: 'Pause for 5 minutes',
				handler: function() { BackupApp.service.pause('5m'); }
			},{
				text: 'Pause for 15 minutes',
				handler: function() { BackupApp.service.pause('15m'); }
			},{
				text: 'Pause for 30 minutes',
				handler: function() { BackupApp.service.pause('30m'); }
			},{
				text: 'Pause for 60 minutes',
				handler: function() { BackupApp.service.pause('60m'); }
			}]
		},{
			text: '*** Throttle ...'
		},{
			xtype: 'menuseparator'
		},{
			text: '*** Run backups',
			menu: [{
				text: 'All backups'
			},{
				text: 'Label: Work'
			},{
				text: 'Label: Home'
			},{
				text: 'Label: Amazon S3'
			}]
		},{
			xtype: 'menuseparator'
		},{
			text: '*** Settings'
		},{
			text: 'About',
			handler: function() { BackupApp.view.AboutWindow.show(); }
		}]
	},{
		xtype: 'label',
		html: '&nbsp;',
		columnWidth: 1
	},{
		xtype: 'panel',
		layout: 'vbox',
		align: 'center',
		margin: 5,
		border: 0,
		items: [{
			xtype: 'label',
			cls: 'throttle-label',
			text: '*** Throttle upload'
		},{
			xtype: 'slider',
			width: 200,
			value: 80,
			minValue: 0,
			maxValue: 100
		}]
	},{
		xtype: 'button',
		cls: 'add-new-backup-button',
		text: '*** Add new backup',
		margin: 10
	},{
		xtype: 'button',
		id: 'status-window-header-pause-button',
		cls: 'pause-backup-button',
		iconCls: 'backups-paused',
		text: 'Pause',
		margin: 10,
		handler: function() { BackupApp.service.togglePause(); }
	}],

	initComponent: function() {

        this.callParent(arguments);
    }
});