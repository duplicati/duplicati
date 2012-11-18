Ext.define('Duplicati.store.NewBackupChainWhenStore', {
    extend: 'Ext.data.Store',

    constructor: function(cfg) {
        var me = this;
        cfg = cfg || {};
        me.callParent([Ext.apply({
            storeId: 'NewBackupChainWhenStore',
            data: [
                {key: '0', value: 'When there are more backups in the chain than ...'},
                {key: '1', value: 'When the chain is older than ...'},
                {key: '2', value: 'Once, and then backup changes only'},
                {key: '3', value: 'Every time'}
            ],
            fields: [
                {
                    name: 'key'
                },
                {
                	name: 'value'
                }
            ]
        }, cfg)]);
    }
});