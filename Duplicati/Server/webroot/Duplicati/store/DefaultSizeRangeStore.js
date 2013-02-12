Ext.define('Duplicati.store.DefaultSizeRangeStore', {
    extend: 'Ext.data.Store',

    constructor: function(cfg) {
        var me = this;
        cfg = cfg || {};
        me.callParent([Ext.apply({
            storeId: 'DefaultSizeRangeStore',
            data: [
                {key: 'b', value: 'bytes'},
                {key: 'kb', value: 'Kilobytes'},
                {key: 'mb', value: 'Megabytes'},
                {key: 'gb', value: 'Gigabytes'},
                {key: 'tb', value: 'Terabyts'}
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