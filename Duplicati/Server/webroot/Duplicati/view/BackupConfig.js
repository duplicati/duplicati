Ext.define('Duplicati.view.BackupConfig', {
    extend: 'Ext.form.Panel',
	
	xtype: 'backupconfigpanel',
    draggable: false,
    floating: false,
    frame: false,
    height: 550,
    width: 698,
    autoScroll: false,
    resizable: false,
    layout: {
        type: 'absolute'
    },
    bodyBorder: false,
    closable: false,
    collapsed: false,
    collapsible: false,
    frameHeader: false,
    hideCollapseTool: false,
    overlapHeader: false,
    preventHeader: true,
    title: 'Backup Job',
    titleCollapse: true,
    waitMsgTarget: true,

    initComponent: function(params) {
        var me = this;
		
		me.scheduleId = me.initialConfig.scheduleId;
		
        me.initialConfig = Ext.apply({
            waitMsgTarget: true
        }, me.initialConfig);

        Ext.applyIf(me, {
            items: [
                {
                    xtype: 'tabpanel',
                    height: 484,
                    id: 'panelWizard',
                    activeTab: 0,
                    plain: true,
                    layout: {
                        deferredRender: true,
                        type: 'card'
                    },
                    items: [
                        {
                            xtype: 'panel',
                            height: 409,
                            activeItem: 0,
                            layout: {
                                type: 'anchor'
                            },
                            hideCollapseTool: false,
                            title: 'General',
                            items: [
                                {
                                    xtype: 'fieldset',
                                    margin: 10,
                                    padding: 10,
                                    collapsed: false,
                                    collapsible: false,
                                    title: 'Name & Label',
                                    formBind: false,
                                    items: [
                                        {
                                            xtype: 'textfield',
                                            id: 'Name',
                                            name: 'Schedule.Name',
                                            fieldLabel: 'Name of Backup',
                                            labelAlign: 'right',
                                            labelWidth: 140,
                                            msgTarget: 'side',
                                            allowBlank: false,
                                            blankText: 'Required: Short Description to identify your Backup',
                                            disableKeyFilter: false,
                                            emptyText: 'Short Description to identify your Backup',
                                            enableKeyEvents: false,
                                            minLength: 6,
                                            anchor: '100%',
                                            formBind: false
                                        },
                                        {
                                            xtype: 'combobox',
                                            id: 'sLabel',
                                            name: 'Schedule.Path',
                                            fieldLabel: 'Labels/Groups',
                                            labelAlign: 'right',
                                            labelWidth: 140,
                                            msgTarget: 'side',
                                            blankText: 'Required: Select a Label or Create a new one',
                                            emptyText: 'Select a Label or Create a new one',
                                            displayField: 'name',
                                            multiSelect: true,
                                            store: 'LabelStore',
                                            anchor: '100%',
                                            formBind: false,
                                            listeners: {
                                                change: {
                                                    fn: me.onSLabelChange,
                                                    scope: me
                                                }
                                            }
                                        },
                                        {
                                            xtype: 'container',
                                            height: 27,
                                            hidden: true,
                                            layout: {
                                                align: 'stretchmax',
                                                pack: 'end',
                                                type: 'hbox'
                                            },
                                            items: [
                                                {
                                                    xtype: 'button',
                                                    text: 'Manage Labels',
                                                    flex: 1
                                                }
                                            ]
                                        }
                                    ]
                                },
                                {
                                    xtype: 'fieldset',
                                    height: 220,
                                    hidden: false,
                                    margin: 10,
                                    padding: 10,
                                    styleHtmlContent: false,
                                    activeItem: 0,
                                    title: 'Security',
                                    formBind: false,
                                    items: [
                                        {
                                            xtype: 'component',
                                            floating: false,
                                            html: 'It is recommended to encrypt all backups which are stored on remote Servers',
                                            margin: 10,
                                            padding: 10,
                                            tpl: [
                                                '<h1>sepp</h1>'
                                            ],
                                            autoScroll: false
                                        },
                                        {
                                            xtype: 'combobox',
                                            id: 'EncryptionModule',
                                            name: 'Task.EncryptionModule',
                                            fieldLabel: 'Encryption method',
                                            labelAlign: 'right',
                                            labelWidth: 140,
                                            editable: false,
                                            forceSelection: true,
                                            queryParam: 'encryptionMethodName',
                                            store: 'EncryptionMethodStore',
                                            displayField: 'encryptionMethodName',
                                            valueField: 'encryptionMethodIdentifier',
                                            anchor: '100%',
                                            formBind: false,
                                            queryMode: 'local'
                                        },
                                        {
                                            xtype: 'textfield',
                                            disabled: false,
                                            id: 'sEncryptionPassword',
                                            stateful: false,
                                            inputType: 'password',
                                            name: 'sEncryptionPassword',
                                            fieldLabel: 'Encryption Password',
                                            labelAlign: 'right',
                                            labelWidth: 140,
                                            msgTarget: 'side',
                                            disableKeyFilter: false,
                                            minLength: 6,
                                            anchor: '100%',
                                            formBind: false
                                        },
                                        {
                                            xtype: 'textfield',
                                            validator: function(value) {
                                                var password1 = Ext.getCmp('sEncryptionPassword');
                                                return (value === password1.getValue() || value.length < 6) ? true : 'Passwords do not match.';
                                            },
                                            id: 'sEncryptionPasswordCheck',
                                            stateful: false,
                                            maintainFlex: false,
                                            inputType: 'password',
                                            name: 'sEncryptionPasswordCheck',
                                            readOnly: false,
                                            fieldLabel: 'Repeat Password',
                                            labelAlign: 'right',
                                            labelWidth: 140,
                                            minLength: 6,
                                            anchor: '100%',
                                            formBind: false
                                        },
                                        {
                                            xtype: 'button',
                                            id: 'btnGeneratePassword',
                                            style: 'float:right',
                                            autoWidth: true,
                                            scale: 'medium',
                                            text: 'Generate Password',
                                            formBind: false
                                        },
                                        {
                                            xtype: 'button',
                                            id: 'btnPassword',
                                            style: 'float:right',
                                            autoWidth: true,
                                            repeat: false,
                                            scale: 'medium',
                                            text: 'Show Password',
                                            formBind: false
                                        }
                                    ]
                                }
                            ]
                        },
                        {
                            xtype: 'panel',
                            height: 367,
                            activeItem: 1,
                            title: 'Source Data',
                            items: [
                                {
                                    xtype: 'fieldset',
                                    margin: 10,
                                    padding: 10,
                                    title: 'Standard Locations',
                                    items: [
                                        {
                                            xtype: 'checkboxgroup',
                                            id: 'chkgStandardLocations',
                                            margin: '',
                                            width: 400,
                                            fieldLabel: '',
                                            columns: 1,
                                            vertical: true,
                                            name: 'Files',
                                            items: [
                                                {
                                                    xtype: 'checkboxfield',
                                                    id: 'chkMyDocuments',
                                                    name: 'Task.Extensions.SelectFilesIncludeDocuments',
                                                    hideLabel: false,
                                                    labelPad: 10,
                                                    boxLabel: 'My Documents'
                                                },
                                                {
                                                    xtype: 'checkboxfield',
                                                    id: 'chkMyMusic',
                                                    name: 'Task.Extensions.SelectFilesIncludeMusic',
                                                    boxLabel: 'My Music'
                                                },
                                                {
                                                    xtype: 'checkboxfield',
                                                    id: 'chkMyVideos',
                                                    name: 'Task.Extensions.SelectFilesIncludeMyVideos',
                                                    boxLabel: 'My Videos'
                                                },
                                                {
                                                    xtype: 'checkboxfield',
                                                    id: 'chkMyPictures',
                                                    name: 'Task.Extensions.SelectFilesIncludeImages',
                                                    boxLabel: 'My Pictures'
                                                },
                                                {
                                                    xtype: 'checkboxfield',
                                                    id: 'chkAppSettings',
                                                    name: 'Task.Extensions.SelectFilesIncludeAppData',
                                                    boxLabel: 'Application Settings'
                                                }
                                            ]
                                        }
                                    ]
                                },
                                {
                                    xtype: 'gridpanel',
                                    id: 'Locations',
                                    margin: 10,
                                    bodyBorder: false,
                                    title: 'Additional Locations',
                                    sortableColumns: false,
                                    store: 'AdditionalPathStore',
                                    columnLines: false,
                                    tools: [
                                        {
                                            xtype: 'tool',
                                            id: 'tAddLocation',
                                            type: 'plus'
                                        }
                                    ],
                                    listeners: {
                                        added: {
                                            fn: me.onLocationsAdded,
                                            scope: me
                                        }
                                    },
                                    columns: [
                                        {
                                            xtype: 'gridcolumn',
                                            renderer: function(value, metaData, record, rowIndex, colIndex, store, view) {
                                                // this works only for Windows ...
                                                // ...need to identify OS before
                                                if( value.indexOf("/") == 0 && value.length > 2 && value[2] == ':') {
                                                    value = value.substr( 1, value.length);
                                                    value = value.replace(/\//g,"\\");
                                                }
                                                return value;
                                            },
                                            dataIndex: 'Location',
                                            text: 'Location'
                                        },
                                        {
                                            xtype: 'numbercolumn',
                                            dataIndex: 'TotalSize',
                                            text: 'Size'
                                        }
                                    ]
                                },
                                {
                                    xtype: 'gridpanel',
                                    margin: 10,
                                    bodyBorder: false,
                                    title: 'Filter Rules',
                                    forceFit: true,
                                    hideHeaders: true,
                                    scroll: 'none',
                                    sortableColumns: false,
                                    store: 'LabelStore',
                                    columnLines: false,
                                    columns: [
                                        {
                                            xtype: 'gridcolumn',
                                            id: 'fldFilter',
                                            margin: '',
                                            dataIndex: 'string',
                                            menuDisabled: false,
                                            text: 'Filter'
                                        }
                                    ],
                                    tools: [
                                        {
                                            xtype: 'tool',
                                            id: 'tAddFilter',
                                            type: 'plus'
                                        }
                                    ]
                                }
                            ]
                        },
                        {
                            xtype: 'panel',
                            activeItem: 2,
                            closable: false,
                            title: 'Target',
                            items: [
                                {
                                    xtype: 'fieldset',
                                    frame: false,
                                    id: 'pConnection',
                                    margin: 10,
                                    padding: 10,
                                    title: 'Connection',
                                    items: [
                                        {
                                            xtype: 'textfield',
                                            id: 'sNameOfConnection',
                                            name: 'NameOfConnection',
                                            fieldLabel: 'Name of Connection',
                                            labelAlign: 'right',
                                            labelWidth: 140,
                                            anchor: '100%'
                                        },
                                        {
                                            xtype: 'fieldcontainer',
                                            height: 27,
                                            layout: {
                                                align: 'stretch',
                                                type: 'hbox'
                                            },
                                            combineErrors: true,
                                            combineLabels: false,
                                            fieldLabel: 'Label',
                                            hideLabel: true,
                                            msgTarget: 'side',
                                            anchor: '100%',
                                            formBind: false,
                                            items: [
                                                {
                                                    xtype: 'textfield',
                                                    floating: false,
                                                    id: 'sConnectionURL',
                                                    name: 'ConnectionURL',
                                                    fieldLabel: 'Connection URL',
                                                    labelAlign: 'right',
                                                    labelWidth: 140,
                                                    flex: 10
                                                },
                                                {
                                                    xtype: 'button',
                                                    id: 'btnBuildURL',
                                                    text: '...',
                                                    flex: 1,
                                                    margins: '0 0 5 5'
                                                }
                                            ]
                                        },
                                        {
                                            xtype: 'container',
                                            padding: '0 0 0 145',
                                            items: [
                                                {
                                                    xtype: 'fieldset',
                                                    height: 110,
                                                    id: 'pCredentials',
                                                    margin: '',
                                                    padding: 10,
                                                    layout: {
                                                        align: 'stretchmax',
                                                        pack: 'center',
                                                        padding: 10,
                                                        type: 'vbox'
                                                    },
                                                    collapsible: false,
                                                    title: 'Credentials',
                                                    items: [
                                                        {
                                                            xtype: 'textfield',
                                                            id: 'sUsername',
                                                            maxWidth: 330,
                                                            minWidth: 330,
                                                            width: 330,
                                                            name: 'sUsername',
                                                            fieldLabel: 'Username',
                                                            labelAlign: 'right',
                                                            labelWidth: 70
                                                        },
                                                        {
                                                            xtype: 'textfield',
                                                            id: 'sPassword',
                                                            maxWidth: 330,
                                                            minWidth: 330,
                                                            width: 330,
                                                            inputType: 'password',
                                                            name: 'sPassword',
                                                            fieldLabel: 'Password',
                                                            labelAlign: 'right',
                                                            labelWidth: 70
                                                        }
                                                    ]
                                                },
                                                {
                                                    xtype: 'button',
                                                    margin: '5 0 0 0',
                                                    text: 'Test Connection'
                                                }
                                            ]
                                        }
                                    ]
                                }
                            ]
                        },
                        {
                            xtype: 'panel',
                            activeItem: 3,
                            title: 'Run & Delete',
                            items: [
                                {
                                    xtype: 'fieldset',
                                    id: 'fsSchedule',
                                    margin: 10,
                                    padding: 10,
                                    title: 'Schedule',
                                    items: [
                                        {
                                            xtype: 'checkboxfield',
                                            id: 'chkRunRegular',
                                            margin: '0 0 20 0',
                                            name: 'chkRunRegular',
                                            fieldLabel: 'Label',
                                            hideLabel: true,
                                            boxLabel: 'Run regulary at the specified times',
                                            checked: true,
                                            anchor: '100%',
                                            listeners: {
                                                change: {
                                                    fn: me.onChkRunRegularChange,
                                                    scope: me
                                                }
                                            }
                                        },
                                        {
                                            xtype: 'fieldcontainer',
                                            layout: {
                                                align: 'stretchmax',
                                                type: 'hbox'
                                            },
                                            fieldLabel: 'Label',
                                            hideLabel: true,
                                            items: [
                                                {
                                                    xtype: 'timefield',
                                                    margin: '0 5 0 0',
                                                    fieldLabel: 'Next Time',
                                                    name: 'Schedule.When.Time',
                                                    labelSeparator: ' ',
                                                    flex: 5
                                                },
                                                {
                                                    xtype: 'datefield',
                                                    maintainFlex: false,
                                                    name: 'Schedule.When.Date',
                                                    fieldLabel: 'Label',
                                                    hideLabel: true,
                                                    flex: 3
                                                },
                                                {
                                                    xtype: 'container',
                                                    flex: 10
                                                }
                                            ]
                                        },
                                        {
                                            xtype: 'fieldcontainer',
                                            layout: {
                                                align: 'stretchmax',
                                                type: 'hbox'
                                            },
                                            fieldLabel: '',
                                            hideLabel: true,
                                            items: [
                                                {
                                                    xtype: 'numberfield',
                                                    margin: '0 5 0 0',
                                                    fieldLabel: 'Run again every',
                                                    name: 'Schedule.Repeat.Number',
                                                    minValue: 0,
                                                    labelSeparator: ' ',
                                                    flex: 5
                                                },
                                                {
                                                    xtype: 'combobox',
                                                    fieldLabel: 'Label',
                                                    hideLabel: true,
                                                    flex: 3,
                                                    name: 'Schedule.Repeat.Suffix',
                                                    store: 'DefaultTimeRangesStore',
		                                            editable: false,
		                                            forceSelection: true,
		                                            displayField: 'value',
		                                            valueField: 'key'
                                                },
                                                {
                                                    xtype: 'container',
                                                    flex: 10
                                                }
                                            ]
                                        },
                                        {
                                            xtype: 'fieldcontainer',
                                            layout: {
                                                align: 'stretchmax',
                                                type: 'hbox'
                                            },
                                            fieldLabel: '',
                                            hideLabel: true,
                                            items: [
                                                {
                                                    xtype: 'checkboxgroup',
                                                    width: 400,
                                                    fieldLabel: 'Allowed Days',
                                                    flex: 5,
                                                    name: 'Schedule.AllowedWeekdays',
                                                    items: [
                                                        {
                                                            xtype: 'checkboxfield',
                                                            boxLabel: 'Mon'
                                                        },
                                                        {
                                                            xtype: 'checkboxfield',
                                                            boxLabel: 'Tue'
                                                        },
                                                        {
                                                            xtype: 'checkboxfield',
                                                            boxLabel: 'Wed'
                                                        },
                                                        {
                                                            xtype: 'checkboxfield',
                                                            boxLabel: 'Thu'
                                                        },
                                                        {
                                                            xtype: 'checkboxfield',
                                                            boxLabel: 'Fri'
                                                        },
                                                        {
                                                            xtype: 'checkboxfield',
                                                            boxLabel: 'Sat'
                                                        },
                                                        {
                                                            xtype: 'checkboxfield',
                                                            boxLabel: 'Sun'
                                                        }
                                                    ]
                                                },
                                                {
                                                    xtype: 'container',
                                                    flex: 2
                                                }
                                            ]
                                        }
                                    ]
                                },
                                {
                                    xtype: 'fieldset',
                                    margin: 10,
                                    padding: 10,
                                    title: 'Full or incremental',
                                    items: [
                                        {
                                            xtype: 'combobox',
                                            fieldLabel: 'Make a full backup',
                                            labelWidth: 120,
                                            store: 'NewBackupChainWhenStore',
                                            displayField: 'value',
                                            valueField: 'key',
                                            editable: false,
                                            forceSelection: true,
                                            anchor: '100%'
                                        },
                                        {
                                            xtype: 'fieldcontainer',
                                            height: 27,
                                            layout: {
                                                align: 'stretch',
                                                type: 'hbox'
                                            },
                                            fieldLabel: 'Label',
                                            hideLabel: true,
                                            items: [
                                                {
                                                    xtype: 'numberfield',
                                                    hideEmptyLabel: false,
                                                    labelWidth: 120,
                                                    minValue: 0,
                                                    flex: 5,
                                                    margins: '0 5 0 0'
                                                },
                                                {
                                                    xtype: 'combobox',
                                                    fieldLabel: 'Label',
                                                    store: 'DefaultTimeRangesStore',
                                                    displayField: 'value',
                                                    valueField: 'key',
		                                            editable: false,
		                                            forceSelection: true,
                                                    hideLabel: true,
                                                    flex: 2
                                                },
                                                {
                                                    xtype: 'container',
                                                    flex: 8
                                                }
                                            ]
                                        }
                                    ]
                                },
                                {
                                    xtype: 'fieldset',
                                    margin: 10,
                                    padding: 10,
                                    title: 'Delete old backups',
                                    items: [
                                        {
                                            xtype: 'displayfield',
                                            margin: '0 0 10 0',
                                            value: 'Please note: A full and its incremental backups are called a chain. Duplicati deletes entire backup chains only. A chain is deleted, when all its backups match the specified rule.',
                                            hideEmptyLabel: false,
                                            labelWidth: 120,
                                            anchor: '100%'
                                        },
                                        {
                                            xtype: 'combobox',
                                            fieldLabel: 'Delete backups',
                                            labelWidth: 120,
                                            store: 'DeleteOldChainsWhenStore',
                                            editable: false,
                                            forceSelection: true,
                                            displayField: 'value',
                                            valueField: 'key',
                                            anchor: '100%'
                                        },
                                        {
                                            xtype: 'fieldcontainer',
                                            height: 27,
                                            layout: {
                                                align: 'stretch',
                                                type: 'hbox'
                                            },
                                            fieldLabel: 'Label',
                                            hideLabel: true,
                                            items: [
                                                {
                                                    xtype: 'numberfield',
                                                    hideEmptyLabel: false,
                                                    labelWidth: 120,
                                                    minValue: 0,
                                                    flex: 5,
                                                    margins: '0 5 0 0'
                                                },
                                                {
                                                    xtype: 'combobox',
                                                    fieldLabel: 'Label',
                                                    store: 'DefaultTimeRangesStore',
                                                    displayField: 'value',
                                                    valueField: 'key',
		                                            editable: false,
		                                            forceSelection: true,
                                                    hideLabel: true,
                                                    flex: 2
                                                },
                                                {
                                                    xtype: 'container',
                                                    flex: 8
                                                }
                                            ]
                                        }
                                    ]
                                }
                            ]
                        },
                        {
                            xtype: 'panel',
                            activeItem: -1,
                            title: 'Options',
                            items: [
                                {
                                    xtype: 'fieldset',
                                    margin: 10,
                                    padding: 10,
                                    title: 'Options',
                                    items: [
                                        {
                                            xtype: 'combobox',
                                            fieldLabel: 'Missed Backup',
                                            anchor: '100%'
                                        },
                                        {
                                            xtype: 'displayfield',
                                            margin: '0 0 30 0 0',
                                            value: 'When the computer or Duplicati turned off when a backup should have been made, this defines how Duplicati will handle this situation',
                                            hideEmptyLabel: false,
                                            hideLabel: false,
                                            anchor: '100%'
                                        },
                                        {
                                            xtype: 'fieldcontainer',
                                            height: 27,
                                            layout: {
                                                align: 'stretch',
                                                type: 'hbox'
                                            },
                                            fieldLabel: 'Label',
                                            hideLabel: true,
                                            items: [
                                                {
                                                    xtype: 'numberfield',
                                                    fieldLabel: 'Max archive size',
                                                    name: 'Task.Extensions.VolumeSize.Number',
                                                    minValue: 0,
                                                    flex: 2,
                                                    margins: '0 5 0 0'
                                                },
                                                {
                                                    xtype: 'combobox',
                                                    name: 'Task.Extensions.VolumeSize.Suffix',
                                                    fieldLabel: 'Label',
                                                    hideLabel: true,
                                                    store: 'DefaultSizeRangeStore',
                                                    valueField: 'key',
                                                    displayField: 'value',
		                                            editable: false,
		                                            forceSelection: true,
                                                    flex: 1
                                                },
                                                {
                                                    xtype: 'container',
                                                    flex: 4
                                                }
                                            ]
                                        },
                                        {
                                            xtype: 'displayfield',
                                            margin: '0 0 30 0',
                                            value: 'Duplicati splitts his backup archives into smaller chunks. This setting defines what the maximum filesize is for this chunks.',
                                            hideEmptyLabel: false,
                                            hideLabel: false,
                                            anchor: '100%'
                                        },
                                        {
                                            xtype: 'textfield',
                                            fieldLabel: 'Backup file prefix',
                                            anchor: '100%'
                                        },
                                        {
                                            xtype: 'displayfield',
                                            value: 'This prefix is used for all backup file names. This must be set to unique values if multiple backups are stored in the same target path.',
                                            hideEmptyLabel: false,
                                            hideLabel: false,
                                            anchor: '100%'
                                        }
                                    ]
                                },
                                {
                                    xtype: 'propertygrid',
                                    height: 115,
                                    margin: 10,
                                    autoScroll: true,
                                    title: 'Advanced',
                                    scroll: 'vertical',
                                    columnLines: true,
                                    nameColumnWidth: 180,
                                    tools: [
                                        {
                                            xtype: 'tool',
                                            id: 'tAddOverride',
                                            type: 'plus'
                                        }
                                    ],
                                    source: {
                                        'Property 1': 'String',
                                        'Property 2': true,
                                        'Property 3': '2012-07-28T23:31:46',
                                        'Property 4': 123
                                    }
                                }
                            ]
                        }
                    ]
                }
            ],
            dockedItems: [
                {
                    xtype: 'toolbar',
                    frame: false,
                    height: 40,
                    width: 40,
                    vertical: true,
                    dock: 'bottom',
                    layout: {
                        align: 'middle',
                        pack: 'end',
                        padding: 0,
                        type: 'hbox'
                    },
                    items: [
                        {
                            xtype: 'buttongroup',
                            border: '',
                            minButtonWidth: 100,
                            titleCollapse: false,
                            columns: 2,
                            layout: {
                                columns: 2,
                                type: 'table'
                            },
                            items: [
                                {
                                    xtype: 'button',
                                    frame: false,
                                    id: 'btnClose',
                                    scale: 'medium',
                                    text: 'Close'
                                }
                            ]
                        },
                        {
                            xtype: 'buttongroup',
                            border: '',
                            minButtonWidth: 100,
                            titleCollapse: false,
                            columns: 2,
                            layout: {
                                columns: 2,
                                type: 'table'
                            },
                            items: [
                                {
                                    xtype: 'button',
                                    id: 'btnNext',
                                    scale: 'medium',
                                    text: 'Next >'
                                }
                            ]
                        }
                    ]
                }
            ],
            listeners: {
            }
        });

        me.callParent(arguments);
    },

    onSLabelChange: function(field, newValue, oldValue, options) {
        //Add new Label, by capturing the typing of the user

    },

    onLocationsAdded: function(abstractcomponent, container, pos, options) {
        // calculate consumed space of entry
    },

    onChkRunRegularChange: function(field, newValue, oldValue, options) {
        Ext.Array.forEach(Ext.getCmp("fsSchedule").query("combobox,datefield,timefield,numberfield,checkboxgroup"), function(field) {
            field.setDisabled(!newValue);
            if (!Ext.isIE6 && field.el != null) {
                field.el.animate({opacity: !newValue ? .5 : 1});
            }
        });
    }
});