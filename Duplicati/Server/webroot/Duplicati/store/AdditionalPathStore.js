Ext.define('Duplicati.store.AdditionalPathStore', {
    extend: 'Ext.data.Store',
    requires: [
        'Duplicati.model.TaskItems.BackupLocation'
    ],

    constructor: function(cfg) {
        var me = this;
        cfg = cfg || {};
        me.callParent([Ext.apply({
            autoLoad: true,
            storeId: 'pathStore',
            model: 'Duplicati.model.TaskItems.BackupLocation',
            proxy: {
                type: 'memory',
                reader: {
                    type: 'json',
                    root: 'root'
                }
            }
        }, cfg)]);
    }
});