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
            type: 'boolean',
            convert: parseBool
        },
        {
            name: 'IgnoreTimestamps',
            mapping: 'Ignore Timestamps',
            type: 'boolean',
            convert: parseBool
        },
        {
            name: 'FileSizeLimit',
            mapping: 'File Size Limit'
        },
        {
            name: 'DisableAESfallbackencryption',
            mapping: 'Disable AES fallback encryption',
            type: 'boolean',
            convert: parseBool
        },
        {
            name: 'SelectFilesVersion',
            mapping: 'Select Files - Version',
            type: 'int',
            convert: parseInt
        },
        {
            name: 'SelectFilesUseSimpleMode',
            mapping: 'Select Files - Use Simple Mode',
            type: 'boolean',
            convert: parseBool
        },
        {
            name: 'SelectFilesIncludeDocuments',
            mapping: 'Select Files - Include Documents',
            type: 'boolean',
            convert: parseBool
        },
        {
            name: 'SelectFilesIncludeDesktop',
            mapping: 'Select Files - Include Desktop',
            type: 'boolean',
            convert: parseBool
        },
        {
            name: 'SelectFilesIncludeMusic',
            mapping: 'Select Files - Include Music',
            type: 'boolean',
            convert: parseBool
        },
        {
            name: 'SelectFilesIncludeImages',
            mapping: 'Select Files - Include Images',
            type: 'boolean',
            convert: parseBool
        },
        {
            name: 'SelectFilesIncludeAppData',
            mapping: 'Select Files - Include AppData',
            type: 'boolean',
            convert: parseBool
        },
        {
            name: 'SelectWhenWarnedNoSchedule',
            mapping: 'Select When - Warned No Schedule',
            type: 'boolean',
            convert: parseBool
        },
        {
            name: 'SelectWhenWarnedTooManyIncrementals',
            mapping: 'Select When - Warned Too Many Incrementals',
            type: 'boolean',
            convert: parseBool
        },
        {
            name: 'SelectWhenWarnedNoIncrementals',
            mapping: 'Select When - Warned No Incrementals',
            type: 'boolean',
            convert: parseBool
        },
        {
            name: 'PasswordSettingsWarnedNoPassword',
            mapping: 'Password Settings - Warned No Password',
            type: 'boolean',
            convert: parseBool
        },
        {
            name: 'CleanupSettingsWarnedNoCleanup',
            mapping: 'Cleanup Settings - Warned No Cleanup',
            type: 'boolean',
            convert: parseBool
        }
    ]
});