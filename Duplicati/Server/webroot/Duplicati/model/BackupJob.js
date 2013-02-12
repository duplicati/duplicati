Ext.define('Duplicati.model.BackupJob', {
    extend: 'Ext.data.Model',
    uses: [
        'Duplicati.model.Task',
        'Duplicati.model.Schedule'
    ],

    proxy: {
        type: 'ajax',
        url: '/control.cgi?action=get-schedule-details',
        reader: {
            type: 'json',
            idProperty: 'ID',
            root: 'data'
        }
    },

    associations: [{ 
    	name: 'task',
    	type: 'hasOne',
    	associationKey: 'Task',
    	primaryKey: 'ID',
    	getterName: 'getTask',
    	setterName: 'setTask',
    	model: 'Duplicati.model.Task' 
    },{
    	name: 'schedule',
    	type: 'hasOne',
    	associationKey: 'Schedule',
    	primaryKey: 'ID',
    	getterName: 'getSchedule',
    	setterName: 'setSchedule',
    	model: 'Duplicati.model.Schedule' 
    }]
});
