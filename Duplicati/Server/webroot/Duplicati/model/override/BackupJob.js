Ext.define('Duplicati.model.override.BackupJob', {
    requires: 'Duplicati.model.BackupJob'
}, function() {
    Ext.override(Duplicati.model.BackupJob, {
        
        setValues: function(values, arrayField) {
            var me = this;
    
            function setVal(fieldId, val) {
                if (arrayField) {
                    fieldId = arrayField + '.' + fieldId;
                }
                var field = me.findField(fieldId);
                if (field) {
                    field.setValue(val);
                    if (me.trackResetOnLoad) {
                        field.resetOriginalValue();
                    }
                } else if(Ext.isObject(val)) {
                    me.setValues(val, fieldId);
                }
            }
        
            alert( "Servus setValues" );
    
            if (Ext.isArray(values)) {
                // array of objects
                Ext.each(values, function(val) {
                    setVal(val.id, val.value);
                });
            } else {
                // object hash
                Ext.iterate(values, setVal);
            }
            return this;
        }
    });
});