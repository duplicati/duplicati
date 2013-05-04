Ext.define('Duplicati.controller.BackupConfig', {
    extend: 'Ext.app.Controller',

    onBtnGeneratePasswordClick: function(button, e, options) {
        var text = "";
        var possible = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-.,_:;+#?=)(/&%$§";
        for( var i=0; i < 16; i++ ) {
            text += possible.charAt(Math.floor(Math.random() * possible.length));
        }
        var t = Ext.getCmp('sEncryptionPassword');
        var t2 = Ext.getCmp('sEncryptionPasswordCheck');
        var pwdButton = Ext.getCmp('btnPassword');
        t.inputEl.dom.type = t2.inputEl.dom.type = 'text';
        pwdButton.setText( 'Hide Password' );
        t.setValue(text);
    },

    onButtonClick: function(button, e, options) {
        var t = Ext.getCmp('sEncryptionPassword');
        var t2 = Ext.getCmp('sEncryptionPasswordCheck');
        var pwdButton = Ext.getCmp('btnPassword');
        t.inputEl.dom.type = t2.inputEl.dom.type = ( t.inputEl.dom.type == 'password' )?'text':'password';
        pwdButton.setText( ( t.inputEl.dom.type == 'password' )?'Show Password':'Hide Password' );
    },

    onTAddLocationClick: function(tool, e, options) {
        Ext.create('Duplicati.view.wndAddLocation').show();
    },

    onTAddFilterClick: function(tool, e, options) {
        Ext.create('Duplicati.view.wndAddFilter').show();
    },

    onBtnBuildURLClick: function(button, e, options) {
        Ext.create('Duplicati.view.wndCreateConnection').show();
    },

    onTAddOverrideClick: function(tool, e, options) {
        Ext.create('Duplicati.view.wndAddOverride').show();
    },

    onBtnNextClick: function(button, e, options) {
        if( Ext.getCmp('btnNext').getText() == 'Finish' ) {
            // Save all the stuff and Finish work
            // Checks need to happen here, if everything is filled in proper
            alert( "DONE!!");
	        button.findParentByType('window').destroy();
        } else {
	        var wizardTabs = Ext.getCmp('panelWizard');
            wizardTabs.setActiveTab( wizardTabs.getActiveTab().activeItem + 1 );
        }
    },

    onAddLocationsClick: function(button, e, options) {
        var records = Ext.getCmp('treeLocation').getChecked();
        Ext.getCmp('Locations').getStore().removeAll();

        Ext.Array.each(records, function(rec){
            Ext.getCmp('Locations').getStore().add(
            new Duplicati.model.TaskItems.BackupLocation ( { 
                Location: rec.get('id'), 
                TotalSize:0 
            } 
            )
            );
        });

        Ext.getCmp("winAddLocation").destroy();
    },

    onBtnCloseClick: function(button, e, options) {
        button.findParentByType('window').destroy();
    },

    afterWindowLayout: function(abstractcontainer, layout, options) {        
        if (abstractcontainer.scheduleId == null || abstractcontainer.scheduleId < 0)
        {
			Duplicati.service.getBackupDefaults(function(data) {
				var entry = Duplicati.Utility.createRecordFromData('Duplicati.model.BackupJob', data.data);				
                abstractcontainer.loadRecord(entry);
			});        	
        }
        else
        {
	        var backupJobModel = Ext.ModelManager.getModel('Duplicati.model.BackupJob');
	        backupJobModel.load(abstractcontainer.scheduleId, {
	            scope: this,
	            success : function(backupJobResult, operation) {
	                abstractcontainer.loadRecord(backupJobResult);
	            }
	        });
       	}
    },

    onControllerClickStub: function() {

    },
    
    onWizardTabChange: function(tabPanel, newCard, oldCard, eOpts) {
    	if (newCard.activeItem == -1) {
            Ext.getCmp('btnNext').setText( 'Finish' );
    	} else {
            Ext.getCmp('btnNext').setText( 'Next >' );
    	}
    },

	afterEncryptionModuleChange: function() {
		var combo = Ext.getCmp('EncryptionModule');
        var t = Ext.getCmp('sEncryptionPassword');
        var t2 = Ext.getCmp('sEncryptionPasswordCheck');
        var b = Ext.getCmp('btnPassword');
        var b2 = Ext.getCmp('btnGeneratePassword');
        
        if (combo.getValue() == '') {
        	t.disable();
        	t2.disable();
        	b.disable();
        	b2.disable();
        } else {
        	t.enable();
        	t2.enable();
        	b.enable();
        	b2.enable();
        }
	},

    init: function() {
        this.control({
            "#btnGeneratePassword": {
                click: this.onBtnGeneratePasswordClick
            },
            "#btnPassword": {
                click: this.onButtonClick
            },
            "#tAddLocation": {
                click: this.onTAddLocationClick
            },
            "#tAddFilter": {
                click: this.onTAddFilterClick
            },
            "#tAddOverride": {
                click: this.onTAddOverrideClick
            },
            "#btnBuildURL": {
                click: this.onBtnBuildURLClick
            },
            "#btnNext": {
                click: this.onBtnNextClick
            },
            "#btnAddLocations": {
                click: this.onAddLocationsClick
            },
            "#btnClose": {
                click: this.onBtnCloseClick
            },
            "backupconfigpanel": {
                afterlayout: this.afterWindowLayout
            },
            "#EncryptionModule": {
                change: this.afterEncryptionModuleChange
            },
            "#panelWizard": {
            	tabchange: this.onWizardTabChange
            }
        });
    }

});
