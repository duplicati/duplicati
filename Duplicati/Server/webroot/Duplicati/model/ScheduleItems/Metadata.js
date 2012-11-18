Ext.define('Duplicati.model.ScheduleItems.Metadata', {
    extend: 'Ext.data.Model',

    fields: [
        {
            name: 'total_size',
            mapping: 'total-size'
        },
        {
            name: 'full_backup_count'
        },
        {
            name: 'total_volume_count'
        },
        {
            name: 'total_file_count'
        },
        {
            name: 'longest_chain_length'
        },
        {
            name: 'current_chain_length'
        },
        {
            name: 'current_full_date'
        },
        {
            name: 'current_chain_size'
        },
        {
            name: 'last_backup_date'
        },
        {
            name: 'last_backup_size'
        },
        {
            name: 'total_backup_size'
        },
        {
            name: 'total_quota_space'
        },
        {
            name: 'free_quota_space'
        },
        {
            name: 'source_file_size'
        },
        {
            name: 'source_file_count'
        },
        {
            name: 'source_folder_count'
        },
        {
            name: 'orphan_file_count'
        },
        {
            name: 'orphan_file_size'
        },
        {
            name: 'alien_file_count'
        },
        {
            name: 'alien_file_size'
        },
        {
            name: 'total_backup_sets'
        },
        {
            name: 'last_backup_completed_time'
        }
    ]
});