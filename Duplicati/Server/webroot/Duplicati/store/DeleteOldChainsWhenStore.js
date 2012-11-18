Ext.define('Duplicati.store.DeleteOldChainsWhenStore', {
    extend: 'Ext.data.Store',

    constructor: function(cfg) {
        var me = this;
        cfg = cfg || {};
        me.callParent([Ext.apply({
            storeId: 'DeleteOldChainsWhenStore',
            data: [
                {key: '0', value: 'When there are more chains than ...'},
                {key: '1', value: 'When the last backup in the chain is older than ...'},
                {key: '2', value: 'Never'}
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