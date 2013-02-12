Ext.define('Duplicati.model.Task', {
    extend: 'Ext.data.Model',
    uses: [
        'Duplicati.model.TaskItems.BackendSettings',
        'Duplicati.model.TaskItems.Extensions',
        'Duplicati.model.TaskItems.Overrides',
        'Duplicati.model.TaskItems.EncryptionSettings',
        'Duplicati.model.TaskItems.CompressionSettings'
    ],

    associations: [{ 
    	name: 'backendSettings',
    	associationKey: 'BackendSettings',
    	type: 'hasOne', 
    	model: 'Duplicati.model.TaskItems.BackendSettings', 
    	getterName: 'getBackendSettings',
    	setterName: 'setBackendSettings'
    },{ 
    	name: 'extensions',
    	associationKey: 'Extensions',
    	type: 'hasOne', 
    	model: 'Duplicati.model.TaskItems.Extensions', 
    	getterName: 'getExtensions',
    	setterName: 'setExtensions'
    },{ 
    	name: 'overrides',
    	associationKey: 'Overrides',
    	type: 'hasOne', 
    	model: 'Duplicati.model.TaskItems.Overrides', 
    	getterName: 'getOverrides',
    	setterName: 'setOverrides'
    },{ 
    	name: 'encryptionSettings',
    	associationKey: 'EncryptionSettings',
    	type: 'hasOne', 
    	model: 'Duplicati.model.TaskItems.EncryptionSettings', 
    	getterName: 'getEncryptionSettings',
    	setterName: 'setEncryptionSettings'
    },{ 
    	name: 'compressionSettings',
    	associationKey: 'CompressionSettings',
    	type: 'hasOne', 
    	model: 'Duplicati.model.TaskItems.CompressionSettings', 
    	getterName: 'getCompressionSettings',
    	setterName: 'setCompressionSettings'
    }],


    fields: [
        {
            name: 'Filter'
        },
        {
            name: 'ID'
        },
        {
            name: 'Service'
        },
        {
            name: 'Encryptionkey'
        },
        {
            name: 'SourcePath'
        },
        {
            name: 'ScheduleID'
        },
        {
            name: 'KeepFull'
        },
        {
            name: 'KeepTime'
        },
        {
            name: 'FullAfter'
        },
        {
            name: 'IncludeSetup',
            type: 'boolean'
        },
        {
            name: 'EncryptionModule'
        },
        {
            name: 'CompressionModule'
        }
    ]
});