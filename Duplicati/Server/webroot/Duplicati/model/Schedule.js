Ext.define('Duplicati.model.Schedule', {
    extend: 'Ext.data.Model',

    associations: [{ 
    	name: 'task',
    	type: 'hasOne', 
    	model: 'Duplicati.model.Task', 
    	primaryKey: 'ID', 
    	foreignKey: 'ScheduleID' 
    }],

    fields: [
        {
            name: 'NextScheduledTime'
        },
        {
            name: 'AllowedWeekdays'
        },
        {
            name: 'ID'
        },
        {
            name: 'Name'
        },
        {
            name: 'Path'
        },
        {
            name: 'When'
        },
        {
            name: 'Repeat'
        },
        {
            name: 'Weekdays'
        },
        {
            name: 'Metadata'
        }
    ]
});