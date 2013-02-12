Ext.define('Duplicati.store.MyComputerTreeStore', {
    extend: 'Ext.data.TreeStore',

    constructor: function(cfg) {
        cfg = cfg || {};
        this.callParent([Ext.apply({
            autoLoad: true,
            filterOnLoad: false,
            sortOnLoad: false,
            storeId: 'MyComputerTreeStore',
            folderSort: false,
            nodeParam: 'path',
            root: {
                text: 'My Computer',
                id: '/',
                expanded: false
            },
            proxy: {
                type: 'ajax',
                url: '/control.cgi?action=get-folder-contents&onlyfolders=true',
                reader: {
                    type: 'json',
                    root: '/'
                }
            },
            fields: [
                {
                    mapping: 'check',
                    name: 'checked'
                },
                {
                    name: 'text'
                }
            ]
        }, cfg)]);
    }
});