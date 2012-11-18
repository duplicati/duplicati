Ext.define('Duplicati.model.TaskItems.BackupLocation', {
    extend: 'Ext.data.Model',

    fields: [
        {
            name: 'Location',
            type: 'string'
        },
        {
            name: 'TotalSize',
            type: 'float'
        }
    ]
});