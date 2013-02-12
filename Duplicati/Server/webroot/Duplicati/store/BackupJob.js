Ext.define('Duplicati.store.BackupJob', {
    extend: 'Ext.data.Store',
    requires: [
        'Duplicati.model.BackupJob'
    ],

    constructor: function(cfg) {
        var me = this;
        cfg = cfg || {};
        me.callParent([Ext.apply({
            storeId: 'MyJsonStore',
            model: 'Duplicati.model.BackupJob',
            proxy: {
                type: 'ajax',
                url: '/control.cgi?action=get-schedule-details',
                reader: {
                    type: 'json',
                    idProperty: 'ID',
                    root: 'data'
                }
            }
        }, cfg)]);
    }
});