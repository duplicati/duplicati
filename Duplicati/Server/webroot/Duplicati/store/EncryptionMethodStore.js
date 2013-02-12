Ext.define('Duplicati.store.EncryptionMethodStore', {
    extend: 'Ext.data.Store',

    constructor: function(cfg) {
        var me = this;
        cfg = cfg || {};
        me.callParent([Ext.apply({
            autoLoad: true,
            filterOnLoad: false,
            sortOnLoad: false,
            storeId: 'EncryptionMethodStore',
            
            /*listeners: {
            	load: function(records, success, eOpts) {
            		alert('WS');
            	}
            	
            	//TODO: Should we add the empty row here instad?
            	
            },*/
            
            fields: [
                {
                    mapping: 'DisplayName',
                    name: 'encryptionMethodName',
                    type: 'string'
                },
                {
                    mapping: 'FilenameExtension',
                    name: 'encryptionMethodIdentifier',
                    type: 'string'
                }
            ],
            proxy: {
                type: 'ajax',
                url: '/control.cgi?action=list-installed-encryption-modules',
                reader: {
                    type: 'array',
                    idProperty: 'id'
                }
            }
        }, cfg)]);
    }    
});