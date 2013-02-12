Ext.define('Duplicati.model.TaskItems.Extensions', {
    extend: 'Ext.data.Model',

    fields: [
        {
            name: 'UploadBandwidth',
            mapping: "Upload Bandwidth"
        },
        {
            name: 'DownloadBandwidth',
            mapping: 'Download Bandwidth'
        },
        {
            name: 'MaxUploadSize',
            mapping: 'Max Upload Size'
        },
        {
            name: 'VolumeSize',
            mapping: 'Volume Size'
        },
        {
            name: 'ThreadPriority',
            mapping: 'Thread Priority'
        },
        {
            name: 'AsyncTransfer',
            mapping: 'Async Transfer',
            type: 'boolean'
        },
        {
            name: 'IgnoreTimestamps',
            mapping: 'Ignore Timestamps',
            type: 'boolean'
        },
        {
            name: 'FileSizeLimit',
            mapping: 'File Size Limit'
        },
        {
            name: 'DisableAESfallbackencryption',
            mapping: 'Disable AES fallback encryption',
            type: 'boolean'
        },
        {
            name: 'SelectFilesVersion',
            mapping: 'Select Files - Version',
            type: 'int'
        },
        {
            name: 'SelectFilesUseSimpleMode',
            mapping: 'Select Files - Use Simple Mode',
            type: 'boolean'
        },
        {
            name: 'SelectFilesIncludeDocuments',
            mapping: 'Select Files - Include Documents',
            type: 'boolean'
        },
        {
            name: 'SelectFilesIncludeDesktop',
            mapping: 'Select Files - Include Desktop',
            type: 'boolean'
        },
        {
            name: 'SelectFilesIncludeMusic',
            mapping: 'Select Files - Include Music',
            type: 'boolean'
        },
        {
            name: 'SelectFilesIncludeImages',
            mapping: 'Select Files - Include Images',
            type: 'boolean'
        },
        {
            name: 'SelectFilesIncludeAppData',
            mapping: 'Select Files - Include AppData',
            type: 'boolean'
        },
        {
            name: 'SelectWhenWarnedNoSchedule',
            mapping: 'Select When - Warned No Schedule',
            type: 'boolean'
        },
        {
            name: 'SelectWhenWarnedTooManyIncrementals',
            mapping: 'Select When - Warned Too Many Incrementals',
            type: 'boolean'
        },
        {
            name: 'SelectWhenWarnedNoIncrementals',
            mapping: 'Select When - Warned No Incrementals',
            type: 'boolean'
        },
        {
            name: 'PasswordSettingsWarnedNoPassword',
            mapping: 'Password Settings - Warned No Password',
            type: 'boolean'
        },
        {
            name: 'CleanupSettingsWarnedNoCleanup',
            mapping: 'Cleanup Settings - Warned No Cleanup',
            type: 'boolean'
        }
    ]
});