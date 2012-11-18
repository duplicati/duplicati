Ext.define('Duplicati.store.DefaultTimeRangesStore', {
    extend: 'Ext.data.Store',

    constructor: function(cfg) {
        var me = this;
        cfg = cfg || {};
        me.callParent([Ext.apply({
            storeId: 'DefaultTimeRangesStore',
            data: [
                {key: 's', value: 'Seconds'},
                {key: 'm', value: 'Minutes'},
                {key: 'h', value: 'Hours'},
                {key: 'D', value: 'Days'},
                {key: 'W', value: 'Weeks'},
                {key: 'M', value: 'Months'},
                {key: 'Y', value: 'Years'}
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