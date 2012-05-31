Ext.define('BackupApp.model.Schedule', {
    extend: 'Ext.data.Model',
    fields: ['ID', 'Name', 'Path', 'MetadataLookup', 'NextScheduledTime', 'When', 'ExistsInDb', 'AllowedWeekdays', 'Repeat', 'Weekdays']
});