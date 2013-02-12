Ext.define('Duplicati.store.LabelStore', {
    extend: 'Ext.data.Store',

    constructor: function(cfg) {
        var me = this;
        cfg = cfg || {};
        me.callParent([Ext.apply({
            storeId: 'LabelStore',
            data: [
                {name: 'private'},
                {name: 'important'},
                {name: 'weekly'}
            ],
            fields: [
                {
                    name: 'name'
                }
            ]
        }, cfg)]);
    }
});