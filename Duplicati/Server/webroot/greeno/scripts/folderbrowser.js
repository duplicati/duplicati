$(document).ready(function() {
    $.browseForFolder = function(config) {
        var self = {};

        var select_node = function(node) {
            if (node != null && node.original != null && node.original.filepath != null) {
                var path = node.original.filepath;
                if (self.resolvePath && node.original.resolvedpath)
                    path = node.original.resolvedpath;

                config.callback(path, node.text);
                self.rootel.dialog('close');
            }
        }

        self.rootel = $('<div class="modal-dialog folder-browser-dialog"></div>')
        self.treeel = $('<div></div>');

        self.rootel.append(self.treeel);

        self.rootel.attr('title', config.title);
        self.resolvePath = config.resolvePath;

        self.loadData = function(path, callback) {
            $.ajax({
                'url': APP_CONFIG.server_url,
                'data': { 
                    'action': 'get-folder-contents',
                    'onlyfolders': true,
                    'path': path
                },
                'dataType': 'json'
            }).done(function(data, status, xhr) {
                for(var i = 0; i < data.length; i++) {
                    var o = data[i];
                    o.children = !o.leaf;
                    o.filepath = o.id;

                    //o.icon = o.iconCls;
                    delete o.id;
                    delete o.iconCls;
                    delete o.leaf;
                }

                if (path == "/") {
                    var usernode = {
                        text: 'User data',
                        leaf: false,
                        state: {opened: true},
                        children: []
                    };

                    var systemnode = {
                        text: 'Computer',
                        leaf: false,
                        state: {opened: true},
                        children: []
                    };

                    var roots = [
                        usernode,
                        systemnode
                    ];

                    for(var i = 0; i < data.length; i++) {
                        if (data[i].filepath.indexOf('%') == 0)
                            usernode.children.push(data[i]);
                        else
                            systemnode.children.push(data[i]);
                    }

                    data = roots;             
                }


                callback(data)
            }).fail(function() {
                callback(null);
            });
        };

        self.treeel.jstree({
            'core': { 
                'data': function(node, callback){
                    if (node.id === '#') {
                        self.loadData('/', callback);
                    } else {
                        self.loadData(node.original.filepath, callback);
                    }
                }
            },
            'dnd': { copy: false },
        });

        self.rootel.dialog({
            minWidth: 320, 
            width: $('body').width > 600 ? 320 : 600, 
            minHeight: 480, 
            height: 500, 
            modal: true,
            autoOpen: true,
            closeOnEscape: true,
            buttons: [
                { text: 'Close', disabled: false, click: function(event, ui) {
                    self.rootel.dialog('close');
                }},
                { text: 'OK', disabled: true, click: function(event, ui) {
                    select_node(self.selected_node);
                }}
            ]
        });

        self.dlg_buttons = self.rootel.parent().find('.ui-dialog-buttonpane').find('.ui-button');

        self.rootel.on('dialogclose', function() {
            self.rootel.remove();
        });

        self.treeel.bind("dblclick.jstree", function (event) {
            var node = self.treeel.jstree().get_node($(event.target).closest("li"));
            select_node(node);
        });

        self.treeel.bind('select_node.jstree', function (event, data) {
            if (data.node.original.filepath)
                self.selected_node = data.node;
            else
                self.selected_node = null;
            self.dlg_buttons.last().button('option', 'disabled', self.selected_node == null);
        });


    };
});