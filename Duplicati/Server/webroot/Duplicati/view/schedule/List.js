Ext.define('Duplicati.view.schedule.List' ,{
    extend: 'Ext.grid.Panel',
    alias : 'widget.schedulelist',

	title : 'All Schedules',
	header: false,
	border: 0,
	id: 'status-window-schedule-list',

	hideHeaders: true,
	selType: 'rowmodel',
	
	store: 'Schedules',
	
	elementTemplateStr: 
		'<span class="backup-schedule-name" title="Click for actions - {Name}" onclick="Duplicati.ListUtility.showContextMenu({ID}, this);"><span class="backup-schedule-name-inner">{Name}<div class="backup-schedule-drop-arrow"></div></span></span>' +
		'<span class="backup-schedule-details">' +
			'<span class="backup-schedule-details-inner">' +
				/* The template for this became un-readable, so now it is a function */
				'{[Duplicati.ListUtility.formatBackupScheduleDetails(values)]}' +
			'</span>' +
			'<tpl if="Path != null && Path != \'\'">' +
				'<div class="backup-schedule-tags">' +
						'<div class="schedule-tags-icon"></div><span>Labels: <span>{Path}</span></span>' +
				'</div>' +
			'</tpl>' +
		'</span>', 
	
	
	elementTemplate: null,
	
	elementContextMenu: Ext.create('Ext.menu.Menu', {
		floating: true,
		items: [{
			text: '*** Show files'
		},{
			text: 'Run now',
			handler: function() {
				Ext.log('Running backup ' + Duplicati.ListUtility.currentScheduleId);
				Duplicati.service.runBackup(Duplicati.ListUtility.currentScheduleId, false); 
			}
		},{
			text: 'Run a full backup now',
			handler: function() {
				Ext.log('Running full backup ' + Duplicati.ListUtility.currentScheduleId);
				Duplicati.service.runBackup(Duplicati.ListUtility.currentScheduleId, true); 
			}
		},{
			text: '*** Cancel'
		},{
			xtype: 'menuseparator'
		},{
			text: 'Edit settings',
			handler: function() {    			
    			Ext.create('Ext.window.Window', {
    				layout: 'fit',
    				bodyBorder: false,
    				title: 'Edit backup',
    				items: [
    					Ext.create('Duplicati.view.BackupConfig', {
		    				scheduleId: Duplicati.ListUtility.currentScheduleId
		    			})
    				]
    			}).show();
			}
		},{
			text: '*** Delete backup'
		},{
			xtype: 'menuseparator'
		},{
			text: '*** Create a copy'
		}]
	}),
	
	columns: 
	[
		{header: 'ScheduleTime',  dataIndex: 'NextScheduledTime',  flex: 0.8, align: 'center', renderer: 
			function(value, metaData, record) {
				var schedulePos = Duplicati.service.getSchedulePosition(record.get("ID"));
				if (schedulePos == 0) {
					return '<div class="schedule-activity-text schedule-running-info"><div>In progress</div><div><a href="#" onclick="Duplicati.service.cancelJob('+ record.get("ID") +')">Cancel</a></div></div><div class="info-icon schedule-active"></div>';
				} else if (schedulePos > 0) {
					return '<div class="schedule-activity-text schedule-queue-info">No. ' + schedulePos + ' in queue</div><div class="info-icon schedule-in-queue"></div>';
				} else if (value != null) {
					var scdate = Duplicati.Utility.parseJsonDate(value);
					if (scdate > new Date().getTime())
						return '<div class="schedule-activity-text schedule-time-info" title="' + scdate.toLocaleString() + '"><span class="marker-time-ago" title="' + scdate.toUTCString() + '"></span>' + Duplicati.Utility.timeAgo(scdate) + '</div><div class="info-icon schedule-waiting"></div>';
				}

				return '';
			} 
		},
		{header: 'Name', dataIndex: 'Name', flex: 1, align: 'center', renderer: 
			function(value, metaData, record) { 
				return this.elementTemplate.apply(record.data);
			} 
		},
		{header: 'Path', dataIndex: 'ID', flex: 0.8, align: 'left', renderer: 
			function(value) { return value; } 
		}
	],

	initComponent: function() {
		this.elementTemplate = new Ext.XTemplate(this.elementTemplateStr, {formatSizeString: function(v) { return Duplicati.Utility.formatSizeString(v); } });
        this.callParent(arguments);
    }
});