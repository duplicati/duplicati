Ext.define('Duplicati.view.override.BackupConfig', {
    requires: 'Duplicati.view.BackupConfig'
}, function() {
    Ext.override(Duplicati.view.BackupConfig, {
    setFieldValue: function(fieldId, val) {
        var field = this.getForm().findField(fieldId);
        if (field) {
            
            field.setValue(val);
            if (this.trackResetOnLoad) {
                field.resetOriginalValue();
            }
            return true;
            
        } else {
        	
			Ext.log('No mapping for field ' + fieldId);
			return false;            
        }
    },
    
    setValues: function(record, parentFieldName) {
        var me = this;
        var form = me.getForm();

        function setVal(fieldId, val) {
            if (parentFieldName) {
                fieldId = parentFieldName + '.' + fieldId;
            }
            var field = form.findField(fieldId);
            if (field) {
                field.setValue(val);
                if (me.trackResetOnLoad) {
                    field.resetOriginalValue();
                }
            } else if(Ext.isObject(val)) {
                me.setValues(val, fieldId);
            } else {
				Ext.log('No mapping for field ' + fieldId);            
            }
        }
	
		if (record.getData) {
	        Ext.iterate(record.getData(), setVal);
	        
	        if (record.getAssociatedData && record.getAssociatedData()) {
				for(k in record.getAssociatedData()) {
					//The property name has a lower first character, but the method format is getCamelCase()
					var propName = k[0].toUpperCase() + k.substring(1);
					if (record['get' + propName] != null)
						me.setValues(record['get' + propName](), parentFieldName + '.' + propName);
				}
	        }
        }
        
        return this;
    },
        
    updateRecord: function(record) {
        var values = this.getFieldValues(),
        name,
        obj = {};

		alert( "Servus updateRecord" );

        function populateObj(record, values) {
            var obj = {},
            name;

            record.fields.each(function(field) {
                name = field.name;
                if (field.model) {
                    var nestedValues = {};
                    var hasValues = false;
                    for(var v in values) {
                        if (v.indexOf('.') > 0) {
                            var parent = v.substr(0, v.indexOf('.'));
                            if (parent == field.name) {
                                var key = v.substr(v.indexOf('.') + 1);
                                nestedValues[key] = values[v];
                                hasValues = true;
                            }
                        }
                    }
                    if (hasValues) {
                        obj[name] = populateObj(Ext.create(field.model), nestedValues);
                    }
                } else if (name in values) {
                    obj[name] = values[name];
                }
            });
            return obj;
        }

        obj = populateObj(record, values);

        record.beginEdit();
        record.set(obj);
        record.endEdit();

        return this;
    },
    
    setEncryptionModule: function(value) {
    	value = value || '';
    	var form = this.getForm();
    	var combo = form.findField('Task.EncryptionModule');
    	var store = combo.getStore();
    	
    	//Make sure we have the empty element
    	if (store.findRecord("id", -1) == null) {
    		store.add({encryptionMethodName: 'No encryption', encryptionMethodIdentifier: '', id: -1});
    	}
    		
    	this.setFieldValue('Task.EncryptionModule', value);
    },
    
    setEncryptionPassword: function(value) {
    	value = value || '';
    	
		this.setFieldValue('sEncryptionPassword', value);
		this.setFieldValue('sEncryptionPasswordCheck', value);
    },
    
    setBackupRepeatAndWhen: function(valueRepeat, valueWhen) {
    	valueWhen = valueWhen || '';
    	valueRepeat = valueRepeat || '';

    	var form = this.getForm();
    	var chk = form.findField('chkRunRegular');

		if (valueWhen == '' || valueRepeat == '') {
			chk.setValue(false);
		} else {
			chk.setValue(true);

			var date = Duplicati.Utility.parseJsonDate(valueWhen);

			this.setFieldValue('Schedule.Repeat.Number', parseInt(valueRepeat.substr(0, valueRepeat.length - 1)));   	
			this.setFieldValue('Schedule.Repeat.Suffix', valueRepeat.substr(valueRepeat.length - 1, 1));

			this.setFieldValue('Schedule.When.Time', date);
			this.setFieldValue('Schedule.When.Date', date);
		}
    },
    
    setAllowedWeekdays: function(value) {
    	var form = this.getForm();

		var t = form.findField('Schedule.AllowedWeekdays');    	
    },

    setVolumeSize: function(value) {
    	value = value || '';

		var number = parseInt(value);
		var suffix = 'b';

		function isNumber(n) {
			switch(n) {
				case '0':
				case '1':
				case '2':
				case '3':
				case '4':
				case '5':
				case '6':
				case '7':
				case '8':
				case '9':
					return true;
				default:
					return false;
			}
		}

		if (value != '' && value.length >= 2) {
			
			var i = value.length - 1;
			while(i >= 0 && !isNumber(value[i]))
				i--;

			i++;
			
			suffix = value.substr(i, value.length - i).toLowerCase();			
			number = parseInt(value.substr(0, i));
		}
		
		this.setFieldValue('Task.Extensions.VolumeSize.Number', number);   	
		this.setFieldValue('Task.Extensions.VolumeSize.Suffix', suffix);
    },
    
    setSourcePaths: function(value) {
        var store = Ext.getCmp('Locations').getStore();
        store.removeAll();

		if (value != null && value != '') {
			var pathSep = Duplicati.service.serverdata['system-info'].PathSeparator;
			
	        Ext.Array.each(value.split(pathSep), function(rec){
	            Ext.getCmp('Locations').getStore().add(
		            new Duplicati.model.TaskItems.BackupLocation ( { 
		                Location: rec, 
		                TotalSize:0 
		            })
	            );
	        });
		}    	
    },
    
    loadRecord: function(loaderObject) {
    	var schedule = loaderObject.getSchedule();
    	var task = loaderObject.getTask();
    	
    	this.setValues(schedule, "Schedule")
    	this.setValues(task, "Task")
    	
    	this.setEncryptionModule(task.EncryptionModule);
    	this.setEncryptionPassword(task.Encryptionkey);
    	
    	this.setBackupRepeatAndWhen(schedule.getData().Repeat, schedule.getData().When);
    	this.setAllowedWeekdays(schedule.AllowedWeekdays);
    	this.setVolumeSize(task.getExtensions().getData().VolumeSize);
    	this.setSourcePaths(task.getData().SourcePath);
    }
        
        
    });
});
