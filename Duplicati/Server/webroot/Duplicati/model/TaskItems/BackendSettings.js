Ext.define('Duplicati.model.TaskItems.BackendSettings', {
    extend: 'Ext.data.Model',

    fields: [
        {
            name: 'UI_Checked_empty'
        },
        {
            name: 'Destination'
        },
        {
            name: 'Username'
        },
        {
            name: 'Password'
        }
    ]
});