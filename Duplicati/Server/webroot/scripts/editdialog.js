/*
 * Editdialog app code
 */

 EDIT_STATE = null;
 EDIT_BACKUP = null;

$(document).ready(function() {

    EDIT_BACKUP = {
        validate_tab: function(tab) {
            var tabs = $('#edit-dialog').parent().find('[role=tablist] > li');
            tab += '';

            if (tab.length > 0 && tab[0] == '#')
                tab = tab.substr(1);

            if (parseInt(tab) + '' == tab) {
                tab = tabs[parseInt(tab)]
            } else {
                var tt = tab;
                tab = null;
                for(var n in tabs) {
                    var href = $(n).find('a').attr('href');
                    if (href && href[0] == '#') {
                        href = href.substr(1);
                    }

                    if (tabs[n] == tt || href == tab) {
                        tab = tabs[n];
                        break;
                    }
                }
            }

            if (tab) {
                var href = $(tab).find('a').attr('href');
                if (href && href[0] == '#')
                    href = href.substr(1);

                var index;
                for(index in tabs)
                    if (tab == tabs[index])
                        break;

                if (EDIT_BACKUP.validate_form_map[href]) {
                    if (!EDIT_BACKUP.validate_form_map[href](index)) {
                        return false;
                    }
                }

                return true;
            } else {
                return false;
            }

        },

        validate_form_map: {
            'edit-tab-general': function(tabindex) {
                if ($('#backup-name').val().trim() == '') {
                    $('#edit-dialog').tabs( "option", "active", tabindex);                
                    $('#backup-name').focus();
                    alert('You must enter a name for the backup');
                    return false;
                }

                if ($('#encryption-method').val() != '') {
                    if ($('#encryption-password').val().trim() == '') {
                        $('#edit-dialog').tabs( "option", "active", tabindex);                
                        $('#encryption-password').focus();
                        alert('You must enter a passphrase');
                        return false;
                    }

                    if (!EDIT_STATE.passwordShown && $('#repeat-password').hasClass('password-mismatch')) {
                        $('#edit-dialog').tabs( "option", "active", tabindex);                
                        $('#repeat-password').focus();
                        alert('The passwords do not match');
                        return false;
                    }
                }

                return true;
            },

            'edit-tab-sourcedata': function(tabindex) {

                if ($('#source-folder-paths').find('.source-folder').length == 0) {
                    $('#edit-dialog').tabs( "option", "active", tabindex);                
                    $('#source-folder-paths').focus();
                    alert('You must select at least one source folder to back up');
                    return false;
                }

                return true;
            },

            'edit-tab-target': function(tabindex) {
                if ($('#backup-uri').val().trim() == '') {
                    $('#edit-dialog').tabs( "option", "active", tabindex);                
                    $('#backup-uri').focus();
                    alert('You must enter a connection url for the backup');
                    return false;
                }

                return true;
            },

            'edit-tab-schedule': function(tabindex) {
                if ($('#use-scheduled-run').is(':checked')) {
                    var t = Date.parse($('#next-run-date').val() + 'T' + $('#next-run-time').val());
                    if (isNaN(t)) {
                        $('#edit-dialog').tabs( "option", "active", tabindex);                
                        $('#next-run-time').focus();
                        alert('You must enter a valid time');
                        return false;
                    }
                }

                return true;
            }
        },

        fill_form_map: {
            'encryption-module': 'encryption-method',
            'Name': 'backup-name',
            'TargetURL': 'backup-uri',
            'passphrase': function(dict, key, val, cfgel) {
                $('#encryption-password').val(val);
                $('#repeat-password').val(val);
            },
            'Sources': function(dict, key, val, cfgel) {
                var sources = val || [];

                $('#source-folder-paths').find('.source-folder').empty();
                for(var n in sources)
                    addSourceFolder(sources[n]);
            },
            'Tags': function(dict, key, val, cfgel) {
                var tags = val || [];
                $('#backup-labels').val(tags.join(', '));
            },
            'Schedule': function(dict, key, val, cfgel) {
                $('#use-scheduled-run').attr('checked', val != null)
            },
            'Repeat': function(dict, key, val, cfgel) {
                $('#use-scheduled-run').attr('checked', val != '');
                $('#use-scheduled-run').change();

                if (!$('#use-scheduled-run').is(':checked'))
                    return;

                var m = (/(\d*)(\w)/mg).exec(val);
                var mul = null;
                if (m) {
                    switch(m[2]) {
                        case 'D':
                            mul = 'days';
                            break;
                        case 'h':
                            mul = 'hours';
                            break;
                        case 'M':
                            mul = 'months';
                            break;
                        case 'Y':
                            mul = 'years';
                            break;
                        case 'W':
                            mul = 'weeks';
                            break;
                    }
                }

                if (!mul) {
                    // Strange stuff, just use it as-is
                    $('#repeat-run-number').val(val + '');
                    $('#repeat-run-multiplier').val('custom');
                } else {
                    $('#repeat-run-number').val(m[1]);
                    $('#repeat-run-multiplier').val(m[2]);
                }
            },
            'Time': function(dict, key, val, cfgel) {
                var parts = val.split(':');
                var now = new Date();
                var msec = Date.parse(val);
                if (isNaN(msec))
                    msec = Date.parse("1970-01-01T" + val);

                var d = null;
                if (isNaN(msec)) {
                    d = new Date(now.getFullYear(), now.getMonth(), now.getDate(), 13, 0, 0);

                    $('#next-run-time').val('13:00');
                    $('#next-run-date').val(now);
                } else {
                    d = new Date(msec);
                    if (d.getFullYear() <= 1970) {
                        msec += new Date(now.getFullYear(), now.getMonth(), now.getDate(), 0, 0, 0).getTime();
                        d = new Date(msec);
                    }
                }

                var y = d.getFullYear() + '';
                var m = (d.getMonth() + 1) + '';
                var n = d.getDate() + '';

                var h = d.getHours() + '';
                var l = d.getMinutes() + '';
                var s = d.getSeconds() + '';

                if (m.length == 1)
                    m = '0' + m;
                if (n.length == 1)
                    n = '0' + n;
                if (h.length == 1)
                    h = '0' + h;
                if (l.length == 1)
                    l = '0' + l;
                if (s.length == 1)
                    s = '0' + s;

                $('#next-run-date').val(y + '-' + m + '-' + n);
                $('#next-run-time').val(h + ':' + l + ':' + s);

            },
            'AllowedDays': function(dict, key, val, cfgel) {

                var d = false;
                if (val == null || val.length == 0)
                    d = true;

                var n = {
                    'mon': d,
                    'tue': d,
                    'wed': d,
                    'thu': d,
                    'fri': d,
                    'sat': d,
                    'sun': d
                };

                for(var i in val)
                    if (n[val[i]] !== undefined)
                        n[val[i]] = true;

                for(var k in n) {
                    $('#allow-day-' + k).attr('checked', n[k]);
                }
            },
            'dblock-size': function(dict, key, val, cfgel) {
                var m = (/(\d*)(\w*)/mg).exec(val);
                var mul = null;
                if (m) {
                    $('#dblock-size-number').val(m[1]);
                    $('#dblock-size-multiplier').val(m[2]);
                }
            }
        },

        fill_dict_map: {
            'source-folder-path-text': false,
            'repeat-password': false,
            'dblock-size-multiplier': false,
            'repeat-run-multiplier': false,
            'next-run-date': false,
            'allow-day-mon': false,
            'allow-day-tue': false,
            'allow-day-wed': false,
            'allow-day-thu': false,
            'allow-day-fri': false,
            'allow-day-sat': false,

            'allow-day-sun': function(dict, key, el, cfgel) { 
                if (!dict['Schedule'])
                    return;

                // Collect all days 
                var days = [];
                var r = ['mon', 'tue', 'wed', 'thu','fri', 'sat', 'sun'];
                for(var k in r)
                    if ($('#allow-day-' + r[k]).is(':checked'))
                        days.push(r[k]);

                dict['Schedule']['AllowedDays']= days;
            },
            'encryption-password': function(dict, key, el, cfgel) { 
                dict['Backup']['Settings']['passphrase'] = $(el).val();
            }, 
            'source-folder-list': function(dict, key, el, cfgel) { 
                var sources = [];
                $('#source-folder-paths').find('.source-folder').each(function(i,el) {
                    sources.push($(el).data('id'));
                });

                dict['Backup']['Sources'] = sources;
            },
            'use-scheduled-run': function(dict, key, el, cfgel) { 
                if (!$(el).is(':checked')) {
                    dict['Schedule'] = null;
                }
            },
            'backup-uri': function(dict, key, el, cfgel) { 
                dict['Backup']['TargetURL'] = $(el).val();
            },
            'backup-name':  function(dict, key, el, cfgel) {
                dict['Backup']['Name'] = $(el).val();
            },
            'backup-labels': function(dict, key, el, cfgel) {
                dict['Backup']['Tags'] = $(el).val().split(',');
            },
            'encryption-method': function(dict, key, el, cfgel) {
                dict['Backup']['Settings']['encryption-module'] = $(el).val();
            },
            'next-run-time': function(dict, key, el, cfgel) {
                if (!dict['Schedule'])
                    return;

                var t = Date.parse($('#next-run-date').val() + 'T' + $('#next-run-time').val());
                if (t != NaN) {
                    var d = new Date(t);

                    //TODO: Not correct UTC, returns GMT instead
                    dict['Schedule']['Time'] = d.toUTCString();
                }
            },
            'dblock-size-number': function(dict, key, el, cfgel) {
                dict['Backup']['Settings']['dblock-size'] = $(el).val() + $('#dblock-size-multiplier').val();
            },
            'repeat-run-number': function(dict, key, el, cfgel) {
                if (!dict['Schedule'])
                    return;

                var m = $('#repeat-run-multiplier').val();
                if (m == 'custom')
                    dict['Schedule']['Repeat'] = $(el).val();
                else
                    dict['Schedule']['Repeat'] = $(el).val() + m;
            }
        }
    };

    $('#backup-name').watermark('Enter a name for your backup');
    $('#backup-labels').watermark('work, docs, s3, movies, other');
    $('#backup-uri').watermark('webdavs://example.com/mybackup?');
    $('#encryption-password').watermark('Enter a secure passphrase');
    $('#repeat-password').watermark('Repeat the passphrase');
    $('#backup-options').watermark('Enter one option pr. line in commandline format, eg. --dblock-size=100MB');
    $('#source-folder-path-text').watermark('Enter a path to back up');

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
                        o.data = { id: o.id, display: o.text };
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

        $('#edit-dialog').tabs('option', 'active', 0);
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
        var tabs = $('#edit-dialog').parent().find('[role=tablist] > li');
        if (event.curPage == tabs.size() - 1) {
            // Saving, validate first 

            for(var n in tabs) {
                if (!EDIT_BACKUP.validate_tab(n)) {
                    return;
                }
            }

            var obj = {
                'Schedule': {},
                'Backup': {
                    'Settings': {},
                    'Filters': [],
                    'Sources': [],
                    'Tags': []
                }
            };

            // To protect against data loss, we use the orig config and update it
            if (EDIT_STATE.orig_config != null)
                $.extend(true, obj, EDIT_STATE.orig_config);

            APP_UTIL.read_form($('#edit-dialog-form'), EDIT_BACKUP.fill_dict_map, obj);
            if (!APP_UTIL.parseOptionStrings($('#backup-options').val(), obj.Backup.Settings, function() {
                //TODO: Add validation
                return true;
            })) {
                //TODO: Add validation
                return;
            }

            //Fixup, change settings dict into array

            var set = obj.Backup.Settings;
            obj.Backup.Settings = [];
            for(var k in set)
                obj.Backup.Settings.push({Name: k, Value: set[k]});

            var method = EDIT_STATE.newBackup ? APP_DATA.addBackup : APP_DATA.updateBackup;
            method(obj, function() {
                EDIT_STATE.dataModified = false;
                $('#edit-dialog').dialog('close');
            },
            function(data, succes, status) {
                alert('Could not save: ' + status);
            });


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
        addSourceFolder(node.data('id'), node.data('display'));
    });

    // Register a drop target for folder nodes
    var inActualMove = false;
    $('#source-folder-droptarget').jstree({ 
        'core': {
            'check_callback': function(method, item, parent, position) {
                if (inActualMove)
                    addSourceFolder(item.data('id'), item.data('display'));

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
        if (data.Backup && data.Backup.ID && parseInt(data.Backup.ID) > 0) {
            EDIT_STATE.orig_config = data;
            EDIT_STATE.newBackup = false;
        } else {
            EDIT_STATE.orig_config = null;
            EDIT_STATE.newBackup = true;
        }

        if (data['Schedule'] == null || data['Schedule']['Repeat'] == null || data['Schedule']['Repeat'] == '')
            APP_UTIL.fill_form($('#edit-dialog-form'), { 'Schedule': null }, EDIT_BACKUP.fill_form_map, 'Schedule');
        else
            APP_UTIL.fill_form($('#edit-dialog-form'), data['Schedule'], EDIT_BACKUP.fill_form_map, 'Schedule');

        // Convert from list-form to key/value map
        var orig = data['Backup'].Settings;
        var settings = {};
        data['Backup'].Settings = settings;
        for(var n in orig)
            settings[orig[n].Name] = orig[n].Value;

        APP_UTIL.fill_form($('#edit-dialog-form'), data['Backup'], EDIT_BACKUP.fill_form_map, d);

        for(var d in data['Backup'])
            if (typeof(data['Backup'][d]) == typeof({}))
                APP_UTIL.fill_form($('#edit-dialog-form'), data['Backup'][d], EDIT_BACKUP.fill_form_map, d);
    });

    /*

    Too bad, we can drop files and folders, 
    but not read the paths, only the contents.
    That seems to make sense for web apps, 
    but not cross-local like this

    if(window.FileReader) { 
        var dragHandler = function(e) {
            e = e || window.event;

            if (e.preventDefault) { e.preventDefault(); }

            var dt = e.dataTransfer;
            if (!dt && e.originalEvent)
                dt = e.originalEvent.dataTransfer;

            if (dt != null)
            {
                var allFiles = true;
                for (var i=0; i<dt.types.length; i++)
                    allFiles &= dt.types[i] == 'Files';

                if (allFiles) {
                    console.log('Setting drag target');
                    dt.dropEffect = 'move';
                    $('#source-folder-paths').addClass('file-drag-target');                    
                    return true;
                }
            }

            return false;

            var files = dt.items;
            for (var i=0; i<files.length; i++) {
                var file = files[i];
                var reader = new FileReader();
                alert(reader.readAsDataURL(file));

            }            
            return false;
        };

        var dragEndHandler = function() { 
            console.log('Removing drag target');
            $('#source-folder-paths').removeClass('file-drag-target'); 
        };

        $('#source-folder-paths').on('dragover', dragHandler);
        $('#source-folder-paths').on('dragenter', dragHandler);
        $('#source-folder-paths').on('dragleave', dragEndHandler);
        $('#source-folder-paths').on('dragend', function(e) { 
            e = e || window.event;
            if (e.preventDefault) { e.preventDefault(); }
            if (e.stopPropagation) { e.stopPropagation(); }
            dragEndHandler();
            return false;
        });

        $('#source-folder-paths').on('drop', function(e) { 
            e = e || window.event;

            if (e.preventDefault) { e.preventDefault(); }
            if (e.stopPropagation) { e.stopPropagation(); }
            dragEndHandler();

            var dt = e.dataTransfer;
            if (!dt && e.originalEvent)
                dt = e.originalEvent.dataTransfer;

            if (dt != null)
            {
                var allFiles = true;
                for (var i=0; i<dt.types.length; i++)
                    allFiles &= dt.types[i] == 'Files';

                if (allFiles) {
                    $('#source-folder-paths').addClass('file-drag-target');                    
                }
            }

            return false;

        });

    }*/

});