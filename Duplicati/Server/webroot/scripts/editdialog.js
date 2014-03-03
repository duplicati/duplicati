/*
 * Editdialog app code
 */

 EDIT_STATE = null;
 EDIT_BACKUP = null;

$(document).ready(function() {

    EDIT_BACKUP = {
        fill_form_map: {
            'encryption-module': 'encryption-method',
            'Repeat': function(dict, key, val, cfgel) {},
            'Time': function(dict, key, val, cfgel) {}
        },

        fill_dict_map: {
            'encryption-method': 'encryption-module',
            'Repeat': function(dict, key, val, cfgel) {},
            'Time': function(dict, key, val, cfgel) {}
        }
    };

    $('#backup-name').watermark('Enter a name for your backup');
    $('#backup-labels').watermark('work, docs, s3, movies, other');
    $('#backup-uri').watermark('webdavs://example.com/mybackup?');
    $('#encryption-password').watermark('Enter a secure passphrase');
    $('#repeat-password').watermark('Repeat the passphrase');
    $('#backup-options').watermark('Enter one option pr. line in commandline format, eg. --dblock-size=100MB');

    var updateState = function() { if (EDIT_STATE != null) EDIT_STATE.dataModified = true; };

    $('#backup-name').change(updateState);
    $('#backup-labels').change(updateState);
    $('#backup-uri').change(updateState);
    $('#encryption-password').change(updateState);
    $('#repeat-password').change(updateState);
    $('#backup-options').change(updateState);

    function split(val) {
        return val.split(/,\s*/);
    }
    function extractLast(val) {
        return split(val).pop();
    }

    $('#backup-labels').autocomplete({
        minLength: 0,

        source: function(request, response) {
            if (EDIT_STATE != null && EDIT_STATE.tags != null)
                response( $.ui.autocomplete.filter(EDIT_STATE.tags, extractLast(request.term)));
        },

        focus: function() {
            return false;
        },

        select: function( event, ui ) {
            var terms = split( this.value );
            terms.pop(); //remove current
            terms.push(ui.item.value);
            terms.push(''); //prepare for new
            this.value = terms.join(', ');
            return false;
        }
    });

    var updatePasswordIndicator = function() {
        $.passwordStrength($('#encryption-password')[0].value, function(r) {
            var f = $('#backup-password-strength');
            if (r == null) {
                f.text('Strength: Unknown');
                r = {score: -1}
            } else {
                f.text('Time to break password: ' +  r.crack_time_display);
            }

            f.removeClass('password-strength-0');
            f.removeClass('password-strength-1');
            f.removeClass('password-strength-2');
            f.removeClass('password-strength-3');
            f.removeClass('password-strength-4');
            f.removeClass('password-strength-unknown');

            if (r.score == 0)
                f.addClass('password-strength-0');
            else if (r.score == 1)
                f.addClass('password-strength-1');
            else if (r.score == 2)
                f.addClass('password-strength-2');
            else if (r.score == 3)
                f.addClass('password-strength-3');
            else if (r.score == 4)
                f.addClass('password-strength-4');
            else
                f.addClass('password-strength-unknown');

        });

        if ($('#encryption-password').val() != $('#repeat-password').val()) {
            $('#repeat-password').addClass('password-mismatch');
            //$('#encryption-password').addClass('password-mismatch');
        } else {
            $('#repeat-password').removeClass('password-mismatch');
            //$('#encryption-password').removeClass('password-mismatch');
        }
    }

    $('#encryption-password').change(updatePasswordIndicator);
    $('#repeat-password').change(updatePasswordIndicator);
    $('#encryption-password').keyup(updatePasswordIndicator);
    $('#repeat-password').keyup(updatePasswordIndicator);

    $('#toggle-show-password').click(function() {
        $('#encryption-password').togglePassword();    
    });

    $('#encryption-password').on('passwordShown', function () {
        $('#toggle-show-password').text('Hide passwords')
        $('#repeat-password').showPassword();
        EDIT_STATE.passwordShown = true;
        //$('#repeat-password').hide();
        //$('#repeat-password-label').hide();
    }).on('passwordHidden', function () {
        $('#toggle-show-password').text('Show passwords')        
        $('#repeat-password').hidePassword();
        EDIT_STATE.passwordShown = false;
        //$('#repeat-password').show();
        //$('#repeat-password-label').show();
    });

    $('#generate-password').click(function() {
        var specials = '!@#$%^&*()_+{}:"<>?[];\',./';
        var lowercase = 'abcdefghijklmnopqrstuvwxyz';
        var uppercase = lowercase.toUpperCase();
        var numbers = '0123456789';
        var all = specials + lowercase + uppercase + numbers;

        function choose(str, n) {
            var res = '';
            for (var i = 0; i < n; i++) {
                res += str.charAt(Math.floor(Math.random() * str.length));
            }

            return res;
        };

        var pwd = (
            choose(specials, 2) + 
            choose(lowercase, 2) + 
            choose(uppercase, 2) + 
            choose(numbers, 2) + 
            choose(all, (Math.random()*5) + 5)
        ).split('');

        for(var i = 0; i < pwd.length; i++) {
            var pos = parseInt(Math.random() * pwd.length);
            var t = pwd[i]
            pwd[i] = pwd[pos];
            pwd[pos] = t;
        }

        pwd = pwd.join('');

        $('#encryption-password')[0].value = pwd;
        $('#repeat-password')[0].value = pwd;

        $('#encryption-password').showPassword(); 
        updatePasswordIndicator();
    });

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
        'plugins' : [ 'themes', 'json', 'ui', 'dnd', 'wholerow' ],
        'core': { 
            'check_callback': function(method, item, parent, position) { 
                // We never allow drops in the tree itself
                return false; 
            }
        },
        'dnd': { copy: false },
    });

    $('#edit-connection-uri-link').click(function() {
        $('#connection-uri-dialog').dialog('open');
    });

    $('#edit-dialog').on( "tabsbeforeactivate", function( event, ui ) {
    });

    var dlg_buttons = $('#edit-dialog').parent().find('.ui-dialog-buttonpane').find('.ui-button');

    $('#edit-dialog').on( "tabsactivate", function( event, ui ) {

        if (ui.newPanel[0].id == 'edit-tab-general')
            $(dlg_buttons[0]).button('option', 'disabled', true);
        else if (ui.oldPanel[0].id == 'edit-tab-general')
            $(dlg_buttons[0]).button('option', 'disabled', false);

        if (ui.newPanel[0].id == 'edit-tab-options')
            $(dlg_buttons[1]).find('span').each(function(ix, el) {el.innerText = 'Save'});
        else if (ui.oldPanel[0].id == 'edit-tab-options')
            $(dlg_buttons[1]).find('span').each(function(ix, el) {el.innerText = 'Next'});

    });

    $('#edit-dialog').on( "dialogopen", function( event, ui ) {
        
        EDIT_STATE = {
            passwordShown: false,
            dataModified: false,
            passwordModified: false,
            newBackup: true
        };

        APP_DATA.getServerConfig(function(serverdata) {
            if (serverdata['EncryptionModules'] == null || serverdata['EncryptionModules'].length == 0) {
                $('#encryption-area').hide();
            } else {
                $('#encryption-area').show();

                var drop = $('#encryption-method');
                drop.empty();

                drop.append($("<option></option>").attr("value", '').text('No encryption'));

                var encmodules = serverdata['EncryptionModules'];

                for (var i = 0; i < encmodules.length; i++)
                  drop.append($("<option></option>").attr("value", encmodules[i].Key).text(encmodules[i].DisplayName));
            }

            $('#encryption-method').change();            
        });

        APP_DATA.getLabels(function(labels) {
            EDIT_STATE.tags = labels;
        });
    });

    $('#encryption-method').change(function() {
        if ($('#encryption-method').val() == '')
            $('#encryption-password-area').hide();
        else
            $('#encryption-password-area').show();
    });


    $('#edit-dialog').on( "dialogbeforeclose", function( event, ui ) {
        if (EDIT_STATE.dataModified) {
            return false;
        }
    });

    $(dlg_buttons[1]).click(function(event, ui) {
        if (event.curPage == 4) {
            // Saving, validate first 

            if ($('#backup-name').val().trim() == '') {
                $('#edit-dialog').tabs( "option", "active", 0);                
                $('#backup-name').focus();
                return false;
            }

            if ($('#encryption-method').val() != '') {
                if (!EDIT_STATE.passwordShown && $('#repeat-password').hasClass('password-mismatch')) {
                    $('#edit-dialog').tabs( "option", "active", 0);                
                    $('#repeat-password').focus();
                }
            }

            EDIT_STATE.dataModified = false;
            $('#edit-dialog').dialog('close');

        }
    });

    var removeSourceFolder = function(el) {
        var container = $('#source-folder-paths');
        container.each(function(i, e) { e.removeChild(el) });

        if (container.find('.source-folder').length == 0) {
            container.addClass('empty');
            $('#source-folder-paths-hint').show();
        }
    };

    var addSourceFolder = function(path, display) {
        var container = $('#source-folder-paths');
        container.removeClass('empty');
        $('#source-folder-paths-hint').hide();

        if (path == null || path.trim() == '')
            return false;

        var exists = false;
        container.find('.source-folder').each(function(i,el) {
            exists |= $(el).data('id') == path;
        });

        if (exists)
            return false;

        var div = $('<div>').addClass('source-folder').text(display).each(function(i, e) { if (path[0] != '%') { e.title = path; }});
        var closer = $('<div></div>').addClass('source-folder-close-icon');
        div.append(closer);

        closer.click(function() {
            removeSourceFolder(div[0]);
        });

        container.append(div);

        $(div).data('id', path);

        APP_DATA.validatePath(path, function(path, success) {
            if (success) 
                div.addClass('path-valid');
            else
                div.addClass('path-invalid');
        });

        return true;
    };

    $('#source-folder-path-add').click(function() {
        var txt = $('#source-folder-path-text').val();
        var disp = txt.split('/');
        if (addSourceFolder(txt, disp[disp.length - 1])) {
            $('#source-folder-path-text').val('');
            $('#source-folder-path-text').focus();
        }
    });

    $('#source-folder-path-text').keypress(function(e) {
        if (e.which == 13)
            $('#source-folder-path-add').click();
    });

    $('#source-folder-browser').bind("dblclick.jstree", function (event) {
       var node = $(event.target).closest("li");
       var id = node.data('id');
        addSourceFolder(id, node.text());
    });

    // Register a drop target for folder nodes
    var inActualMove = false;
    $('#source-folder-droptarget').jstree({ 
        'core': {
            'check_callback': function(method, item, parent, position) {
                if (inActualMove)
                    addSourceFolder(item.data('id'), item.text());

                return !inActualMove;
            },
        },
        'dnd': { copy: false }
    });

    // We need to know if the check callback happens on drop or on drag
    // but jstree only sends "move_node"
    var tree = $('#source-folder-droptarget').data('jstree');
    tree.tree_move_orig = tree.move_node;
    tree.move_node = function(obj, par, pos, callback, is_loaded) {
        try { 
            inActualMove = true;
            this.tree_move_orig(obj, par, pos, callback, is_loaded); 
        } finally { 
            inActualMove = false; 
        }
    }    

    $("#edit-dialog").on('setup-dialog', function(e, data) {

        for (var d in data) {
            APP_UTIL.fill_form($('#edit-dialog-form'), data[d], EDIT_BACKUP.fill_form_map, d);
        }
    });

});