Ext.define('BackupApp.controller.StatuswindowFooter', {
    extend: 'Ext.app.Controller',
	views: [ 'StatuswindowFooter' ],

    init: function() {
        this.control({
            'viewport > panel': {
                render: this.onPanelRendered
            }
        });
    },

    onPanelRendered: function() {
        //console.log('The footer panel was rendered');
    }
});