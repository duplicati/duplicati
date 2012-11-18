Ext.define('Duplicati.view.wndAddLocation', {
    extend: 'Ext.window.Window',

    height: 539,
    id: 'winAddLocation',
    width: 349,
    title: 'Add Location',
    modal: true,

    initComponent: function() {
        var me = this;

        Ext.applyIf(me, {
            items: [
                {
                    xtype: 'treepanel',
                    height: 467,
                    id: 'treeLocation',
                    iconCls: '',
                    title: 'My Computer',
                    hideHeaders: true,
                    store: 'MyComputerTreeStore',
                    folderSort: true,
                    rootVisible: false,
                    singleExpand: true,
                    useArrows: true,
                    viewConfig: {
                        autoScroll: true,
                        blockRefresh: true,
                        loadingText: 'Retrieving File Structure',
                        multiSelect: true,
                        simpleSelect: true
                    }
                }
            ],
            dockedItems: [
                {
                    xtype: 'toolbar',
                    dock: 'bottom',
                    layout: {
                        pack: 'end',
                        type: 'hbox'
                    },
                    items: [
                        {
                            xtype: 'buttongroup',
                            columns: 2,
                            layout: {
                                columns: 2,
                                type: 'table'
                            },
                            items: [
                                {
                                    xtype: 'button',
                                    id: 'btnAddLocations',
                                    maintainFlex: false,
                                    scale: 'medium',
                                    text: 'Add selected Nodes'
                                }
                            ]
                        }
                    ]
                }
            ]
        });

        me.callParent(arguments);
    }

});