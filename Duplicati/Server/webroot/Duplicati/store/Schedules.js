Ext.define('Duplicati.store.Schedules', {
    extend: 'Ext.data.Store',
    model: 'Duplicati.model.Schedule',
    autoLoad: true,

	/* Sort order, 
	 * first by queue position
	 * second by next schedule time,
	 * third by last backup time
	 * */
	sorters: [{
		sorterFn: function(a, b) {
			var schedulePosA = Duplicati.service.getSchedulePosition(a.get("ID"));
			var schedulePosB = Duplicati.service.getSchedulePosition(b.get("ID"));
			
			if(schedulePosA >= 0 && schedulePosB >= 0)
			{
				if (schedulePosA == schedulePosB)
					return 0;
				else
					return schedulePosA < schedulePosB ? -1 : 1;
			}
			else if (schedulePosA >= 0)
				return -1;
			else if (schedulePosB >= 0)
				return 1;
			else
				return 0;
			}
	}, {
		sorterFn: function(a, b) {
			var nextTimeA = Duplicati.Utility.parseJsonDate(a.get('NextScheduledTime'));
			var nextTimeB = Duplicati.Utility.parseJsonDate(b.get('NextScheduledTime'));
			var now = new Date().getTime();
			
			//Scheduled time in past, but not in queue, so it will not run
			if (nextTimeA != null && nextTimeA < now)
				nextTimeA = null;
			if (nextTimeB != null && nextTimeB < now)
				nextTimeB = null;
						
			if (nextTimeA != null && nextTimeB != null)
			{
				nextTimeA = nextTimeA.getTime();
				nextTimeB = nextTimeB.getTime();
				if (nextTimeA == nextTimeB)		
					return 0;
				else
					return nextTimeA < nextTimeB ? -1 : 1;
			}
			else if (nextTimeA != null)
				return -1;
			else if (nextTimeB != null)
				return 1;
			else
				return 0;
		}
	}, {
		sorterFn: function(a, b) {
			var lastTimeA = Duplicati.Utility.parseJsonDate(a.data.Metadata['last-backup-completed-time']);
			var lastTimeB = Duplicati.Utility.parseJsonDate(b.data.Metadata['last-backup-completed-time']);
			if (lastTimeA == null)
				lastTimeA = Duplicati.Utility.parseJsonDate(a.data.Metadata['last-backup-date']);
			if (lastTimeB == null)
				lastTimeB = Duplicati.Utility.parseJsonDate(b.data.Metadata['last-backup-date']);

			if ((lastTimeA != null && lastTimeB != null))
			{
				lastTimeA = lastTimeA.getTime();
				lastTimeB = lastTimeB.getTime();

				if (lastTimeA == lastTimeB)		
					return 0;
				else
					return lastTimeA > lastTimeB ? -1 : 1;
			}
			else if (lastTimeA != null)
				return -1;
			else if (lastTimeB != null)
				return 1;
			else
				return 0;
		}	
	}, {
		property: 'Name'
	}],

    proxy: {
        type: 'ajax',
        url: 'control.cgi?action=list-schedules',
        reader: {
            type: 'json'
        }
    }
});