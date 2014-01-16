/*
 * Editdialog app code
 */
$(document).ready(function() {
    $('#source-folder-browser').jstree({
        'json': {
            'ajax': {
                'url': APP_CONFIG.server_url,
                'data': function(n) {
                    return {
                        'action': 'get-folder-contents',
                        'onlyfolders': true,
                        'path': n === -1 ? "/" : n.data('id')
                    };
                },
                'success': function(data, status, xhr) {
                    for(var i = 0; i < data.length; i++) {
                        var o = data[i];
                        o.title = o.text;
                        o.children = !o.leaf;
                        o.data = { id: o.id };
                        delete o.text;
                        delete o.leaf;
                    }
                    return data;
                }
            },
            'progressive_render' : true,
        },
        'plugins' : [ 'themes', 'json', 'ui' ],
        'core': {
        }
    });
});