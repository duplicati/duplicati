Ext.define('BackupApp.ListUtility', {
	statics: {
		
		currentScheduleId: null,
		
		showContextMenu: function(id, el) {
			var lst = Ext.getCmp('status-window-schedule-list');
			var mnu = lst.elementContextMenu;
			this.currentScheduleId = id;
			var elx = Ext.get(el);
			mnu.showBy(elx, 'tr-br?');
		},
		
		formatSizeString: function(size) { return BackupApp.Utility.formatSizeString(size); },
		timeAgo: function(date) { return BackupApp.Utility.timeAgo(date); },
		
		formatBackupScheduleDetails: function(values)
		{	
			var metadata = '';
			
			if (values.MetadataLookup != null)
			{
				var metadataItems = [];
				var mt = values.MetadataLookup;
				if (mt['source-file-size'] != null)
					metadataItems.push('<span class="backup-schedule-detail-source-size">Source files: ' + this.formatSizeString(mt['source-file-size']) + '</span>');
					
				if (mt['total-backup-size'] != null && parseInt(mt['total-backup-size']) > 0)
					metadataItems.push('<span class="backup-schedule-detail-backup-size">Backup size: ' + this.formatSizeString(mt['total-backup-size']) + '</span>');
				
				if (mt['assigned-quota-space'] != null || mt['free-quota-space'] != null)
				{
					var spaceLeft = null;
					if (mt['assigned-quota-space'] != null && mt['free-quota-space'] != null && mt['total-backup-size'] != null)
						spaceLeft = Math.min(parseInt(mt['assigned-quota-space']) - parseInt(mt['total-backup-size']), parseInt(mt['free-quota-space']));
					else if (mt['assigned-quota-space'] != null && mt['total-backup-size'] != null)
						spaceLeft = parseInt(mt['assigned-quota-space']) - parseInt(mt['total-backup-size']);
					else if (mt['free-quota-space'] != null)
						spaceLeft = parseInt(mt['free-quota-space']);

					var extracls = '';
					if (mt['total-backup-size'] != null)
					{
						//We mark it as low if there is less than 10% free
						if (spaceLeft < (parseInt(mt['total-backup-size']) * 0.1))
							extracls = 'class="low-storage-space"';
					}

					if (spaceLeft != null)
						metadataItems.push('<span class="backup-schedule-detail-free-size">Free space: <span ' + extracls + '>' + this.formatSizeString(spaceLeft) + '</span></span>');
				}
				
				if (mt['changed-file-count'] != null)
				{
					var changedFiles = parseInt(mt['changed-file-count']);
					var str = changedFiles + ' ' + changedFiles == 1 ? "change" : "changes";
					
					if (mt['last-backup-date'] != null)
						str += ' since ' + this.timeAgo(new Date(mt['last-backup-date']));
					
					metadataItems.push('<span class="backup-schedule-detail-change-count">' + str + '</span>');
				} else if (mt['last-backup-date'] != null || mt['last-backup-completed-time'] != null) {
					if (mt['last-backup-completed-time'] != null)
						metadataItems.push('<span class="backup-schedule-detail-last-backup">Last backup: <span class="marker-time-ago" title="' + (new Date(mt['last-backup-completed-time'])).toUTCString() + '"></span>' + this.timeAgo(new Date(mt['last-backup-completed-time'])) + '</span>');
					else
						metadataItems.push('<span class="backup-schedule-detail-last-backup">Last backup: <span class="marker-time-ago" title="' + (new Date(mt['last-backup-date'])).toUTCString() + '"></span>' + this.timeAgo(new Date(mt['last-backup-date'])) + '</span>');
				}

				for(var ix = 0; ix < metadataItems.length; ix++) 
				{
					/*if (ix != 0)
						metadata += ", "; */
						
					metadata += metadataItems[ix];
				}
			}
			
			if (metadata == '')
				metadata = '<span class="important-information">No backup information was found, <a title="Click here to run the backup now" href="#" onclick="BackupApp.service.runBackup(' + values.ID + ', false)">run a backup</a> to obtain data</span>'
			
			return metadata;
		}		
	}
})