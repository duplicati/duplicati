/*
 * Editdialog app code
 */

 EDIT_STATE = null;
 EDIT_BACKUP = null;

$(document).ready(function() {

    var dirSep = '/';

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

                var lookup = {};
                if (EDIT_STATE.orig_config && EDIT_STATE.orig_config.DisplayNames)
                    lookup = EDIT_STATE.orig_config.DisplayNames;

                $('#source-folder-paths').find('.source-folder').remove();
                for(var n in sources) {
                    var p = sources[n];
                    var rp = p;
                    addSourceFolder(rp, lookup[p] || p);
                }
            },
            'Tags': function(dict, key, val, cfgel) {
                var tags = val || [];
                //$('#backup-labels').val(tags.join(', '));
            },
            'Schedule': function(dict, key, val, cfgel) {
                $('#use-scheduled-run').attr('checked', val != null)
                $('#use-scheduled-run').trigger('change');
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
                if (!$('#use-scheduled-run').is(':checked'))
                    return;

                 if (!dict['Schedule'])
                    dict['Schedule'] = {};


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
                    var p = $(el).data('id');
                    sources.push(p);
                });

                dict['Backup']['Sources'] = sources;
            },
            'use-scheduled-run': function(dict, key, el, cfgel) {
                if (!$(el).is(':checked')) {
                    dict['Schedule'] = null;
                } else if(!dict['Schedule']) {
                    dict['Schedule'] = {};
                }
            },
            'backup-uri': function(dict, key, el, cfgel) {
                dict['Backup']['TargetURL'] = $(el).val();
            },
            'backup-name':  function(dict, key, el, cfgel) {
                dict['Backup']['Name'] = $(el).val();
            },
            'backup-labels': function(dict, key, el, cfgel) {
//                dict['Backup']['Tags'] = $(el).val().split(',');
            },
            'encryption-method': function(dict, key, el, cfgel) {
                dict['Backup']['Settings']['encryption-module'] = $(el).val();
                if ($(el).val().trim() == '')
                   dict['Backup']['Settings']['--no-encryption'] = true;
               else
                    delete dict['Backup']['Settings']['--no-encryption'];
            },
            'next-run-time': function(dict, key, el, cfgel) {
                if (!$('#use-scheduled-run').is(':checked'))
                    return;

                 if (!dict['Schedule'])
                    dict['Schedule'] = {};

                var t = Date.parse($('#next-run-date').val());
                if (t != NaN) {
                    var tp = $('#next-run-time').val().split(':');
                    while(tp.length < 3)
                        tp.push('00');

                    var d = new Date(t);
                    d = new Date(d.getFullYear(), d.getMonth(), d.getDate(), parseInt(tp[0]), parseInt(tp[1]), parseInt(tp[2]))

                    dict['Schedule']['Time'] = d.toISOString();
                }
            },
            'dblock-size-number': function(dict, key, el, cfgel) {
                dict['Backup']['Settings']['dblock-size'] = $(el).val() + $('#dblock-size-multiplier').val();
            },
            'repeat-run-number': function(dict, key, el, cfgel) {
                if (!$('#use-scheduled-run').is(':checked'))
                    return;

                 if (!dict['Schedule'])
                    dict['Schedule'] = {};

                var m = $('#repeat-run-multiplier').val();
                if (m == 'custom')
                    dict['Schedule']['Repeat'] = $(el).val();
                else
                    dict['Schedule']['Repeat'] = $(el).val() + m;
            }
        }
    };

    $('#backup-name').watermark('Photos 2014');
//    $('#backup-labels').watermark('work, docs, s3, movies, other');
    $('#backup-uri').watermark('webdavs://user:pass@example.com:995/backup?option=true');
    $('#encryption-password').watermark('Long and secret passphrase');
    $('#repeat-password').watermark('Long and secret passphrase');
    $('#backup-options').watermark('Enter one option pr. line in commandline format, eg. --dblock-size=100MB');
    $('#source-folder-path-text').watermark('Enter a path to back up');
    $('#source-filters').watermark('One filter per line, e.g. "+*.txt" or "-[.*\\.txt]"');

    var updateState = function() { if (EDIT_STATE != null) EDIT_STATE.dataModified = true; };

    $('#backup-name').change(updateState);
//    $('#backup-labels').change(updateState);
    $('#backup-uri').change(updateState);
    $('#encryption-password').change(updateState);
    $('#repeat-password').change(updateState);
    $('#backup-options').change(updateState);
    $('#source-filters').change(updateState);

    function split(val) {
        return val.split(/,\s*/);
    }
    function extractLast(val) {
        return split(val).pop();
    }

    // $('#backup-labels').autocomplete({
    //     minLength: 0,

    //     source: function(request, response) {
    //         if (EDIT_STATE != null && EDIT_STATE.tags != null)
    //             response( $.ui.autocomplete.filter(EDIT_STATE.tags, extractLast(request.term)));
    //     },

    //     focus: function() {
    //         return false;
    //     },

    //     select: function( event, ui ) {
    //         var terms = split( this.value );
    //         terms.pop(); //remove current
    //         terms.push(ui.item.value);
    //         terms.push(''); //prepare for new
    //         this.value = terms.join(', ');
    //         return false;
    //     }
    // });

    var updatePasswordIndicator = function() {

        var strengthMap = {
            0: "Useless",
            1: "Very weak",
            2: "Weak",
            3: "Strong",
            4: "Very strong"
        };

        $.passwordStrength($('#encryption-password')[0].value, function(r) {
            var f = $('#backup-password-strength');
            if (r == null) {
                f.text('Strength: Unknown');
                r = {score: -1}
            } else {
                console.log(r)
                f.text('Strength: ' +  strengthMap[r.score]);
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
        $('#toggle-show-password').text('Hide')
        $('#repeat-password').showPassword();
        EDIT_STATE.passwordShown = true;
        //$('#repeat-password').hide();
        //$('#repeat-password-label').hide();
    }).on('passwordHidden', function () {
        $('#toggle-show-password').text('Show')
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

    $('#backup-uri-label').click(function() {
        $('#connection-uri-dialog').dialog('open');
    });

    var dlg_buttons = $('#edit-dialog').parent().find('.ui-dialog-buttonpane').find('.ui-button');

    $('#edit-dialog').on( "tabsactivate", function( event, ui ) {

        var tabs = $('#edit-dialog').parent().find('[role=tablist] > li');

        if (ui.newPanel.attr('id') == tabs.first().attr('aria-controls'))
            dlg_buttons.first().button('option', 'disabled', true);
        else if (ui.oldPanel.attr('id') == tabs.first().attr('aria-controls'))
            dlg_buttons.first().button('option', 'disabled', false);

        if (ui.newPanel.attr('id') == tabs.last().attr('aria-controls'))
            dlg_buttons.last().button('option', 'label', 'Save');
        else if (ui.oldPanel.attr('id') == tabs.last().attr('aria-controls'))
            dlg_buttons.last().button('option', 'label', 'Next >');

    });

    $('#edit-dialog').on( "dialogopen", function( event, ui ) {
        $('#edit-dialog-form').each(function(i, e) { e.reset(); });
        $('#source-folder-paths').find('.source-folder').remove();
        removeSourceFolder();

        EDIT_STATE = {
            passwordShown: false,
            dataModified: false,
            passwordModified: false,
            newBackup: true
        };

        APP_DATA.getServerConfig(function(serverdata) {
            dirSep = serverdata.DirectorySeparator;

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
        //TODO: Actually set this flag
        if (EDIT_STATE.dataModified) {
            if (!confirm('Close without saving?'))
                return false;
        }
    });

    var readFormData = function(validateOptions) {

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

        for(var k in obj.Backup.Settings)
            if (k.substr(0, 2) == '--')
                delete obj.Backup.Settings[k];

        APP_UTIL.read_form($('#edit-dialog-form'), EDIT_BACKUP.fill_dict_map, obj);

        if (!APP_UTIL.parseOptionStrings($('#backup-options').val(), obj.Backup.Settings, function() {
            //TODO: Add validation
            return true;
        })) {
            return null;
        }

        var filters = $('#source-filters').val();
        var filterlist = [];
        if (filters != '') {
            var lines = filters.split('\n');

            for(var i in lines) {
                var ld = lines[i].trim();
                if (ld == '')
                    continue;

                if (ld[0] != '-' && ld[0] != '+') {
                    EDIT_URI.validation_error($('#source-filters'), 'Each filter line must start with either a "+" or a "-" character');
                    return null;
                }

                filterlist.push({'Expression': ld.substr(1), 'Include': ld[0] == '+', 'Order': i});
            }
        }

        obj.Backup.Filters = filterlist;

        return obj
    };

    dlg_buttons.last().click(function(event, ui) {
        var tabs = $('#edit-dialog').parent().find('[role=tablist] > li');
        if (event.curPage == tabs.size() - 1) {
            // Saving, validate first

            for(var n in tabs) {
                if (!EDIT_BACKUP.validate_tab(n)) {
                    return;
                }
            }

            var obj = readFormData(true);
            if (obj == null)
                return;
            //Fixup, change settings dict into array

            var set = obj.Backup.Settings;
            obj.Backup.Settings = [];
            for(var k in set)
                obj.Backup.Settings.push({Name: k, Value: set[k]});

            if (EDIT_STATE.newBackup) {

                APP_DATA.locateUriDb(obj.Backup.TargetURL, function(res) {
                    var existing_db = (res.Exists && confirm("An existing local database for the storage has been found.\nRe-using the database will allow the commandline and server instances to work on the same remote storage.\n\n Do you wish to use the existing database?")) ? true : false;

                    APP_DATA.addBackup(obj, function() {
                        EDIT_STATE.dataModified = false;
                        $('#edit-dialog').dialog('close');
                    },
                    function(data, succes, status) {
                        alert('Could not save: ' + status);
                    }, 
                    {
                        existing_db: existing_db
                    });
                },
                function(data, succes, status) {
                    alert('Could not save: ' + status);
                });

            } else {
                APP_DATA.updateBackup(obj, function() {
                    EDIT_STATE.dataModified = false;
                    $('#edit-dialog').dialog('close');
                },
                function(data, succes, status) {
                    alert('Could not save: ' + status);
                });
            }

        }
    });

    var removeSourceFolder = function(el) {
        var container = $('#source-folder-paths');
        if (el)
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

    var browsePath =  function() {
        $.browseForFolder({
            title: 'Select folder to back up',
            callback: function(path, disp) {
                disp = (disp || path).split(dirSep);
                addSourceFolder(path, disp[disp.length - 1]);
            }
        });
    };

    $('#source-folder-path-browse').click(browsePath);

    $('#source-folder-path-add').click(function() {
        if ($('#source-folder-path-text').val() == '') {
            browsePath();
        } else {
            var path = $('#source-folder-path-text').val();
            var disp = path.split(dirSep);
            if (addSourceFolder(path, disp[disp.length - 1])) {
                $('#source-folder-path-text').val('');
                $('#source-folder-path-text').trigger('change');
                $('#source-folder-path-text').focus();
            }
        }

    });

    $('#source-folder-path-text').keypress(function(e) {
        if (e.which == 13)
            $('#source-folder-path-add').click();
        else
            $('#source-folder-path-text').trigger('change');

    });

    $('#source-folder-path-text').keyup(function(e) {
        $('#source-folder-path-text').trigger('change');

    });

    $('#source-folder-path-text').change(function(e, data) {
        if ($('#source-folder-path-text').val().trim() == '')
            $('#source-folder-path-add').button('option', 'label', 'Browse');
        else
            $('#source-folder-path-add').button('option', 'label', 'Add');
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

        var opttext = '';
        for(var k in data['Backup']['Settings'])
            if (EDIT_BACKUP.fill_form_map[k] === undefined && k.indexOf('--') == 0)
                opttext += k + '=' + (data['Backup']['Settings'][k] || '') + '\n';

        $('#backup-options').val(opttext);

        var filtertext = '';
        for(var k in data['Backup']['Filters'])
            filtertext += (data['Backup']['Filters'][k].Include ? '+' : '-') + data['Backup']['Filters'][k].Expression + '\n';

        $('#source-filters').val(filtertext);


        EDIT_STATE.dataModified = false;
    });

    $('#backup-options-dialog').dialog({
        minWidth: 320,
        width: $('body').width > 600 ? 320 : 600,
        minHeight: 480,
        height: 500,
        modal: true,
        autoOpen: false,
        closeOnEscape: true,
        buttons: [
            { text: 'Close', disabled: false, click: function(event, ui) {
                $('#backup-options-dialog').dialog('close');
            }}
        ]
    });

    $('#backup-options-link').click(function() {
        APP_DATA.getServerConfig(function(data) {
            $('#backup-options-dialog').dialog('open');

            var baseOpts = data.Options;

            var obj = readFormData(false);
            var compressionModule = '';
            var encryptionModule = '';

            for (var i in data.Options)
                if (data.Options[i].Name == 'compression-module')
                    compressionModule = data.Options[i].DefaultValue;
                else if (data.Options[i].Name == 'encryption-module')
                    encryptionModule = data.Options[i].DefaultValue;

            if (obj != null && obj.Backup != null && obj.Backup.Settings != null) {
                if (obj.Backup.Settings['compression-module'] != null && obj.Backup.Settings['compression-module'] != '')
                    compressionModule = obj.Backup.Settings['compression-module'];
                if (obj.Backup.Settings['encryption-module'] != null && obj.Backup.Settings['encryption-module'] != '')
                    encryptionModule = obj.Backup.Settings['encryption-module'];
            }

            for(var n in data.CompressionModules)
                if (data.CompressionModules[n].Key == compressionModule) {
                    baseOpts = baseOpts.concat(data.CompressionModules[n].Options);
                    break;
                }

            for(var n in data.EncryptionModules)
                if (data.EncryptionModules[n].Key == encryptionModule) {
                    baseOpts = baseOpts.concat(data.EncryptionModules[n].Options);
                    break;
                }

            for(var n in data.GenericModules)
                baseOpts = baseOpts.concat(data.GenericModules[n].Options);


            $('#backup-options-dialog').trigger('configure', { Options: baseOpts, callback: function(id) {
                $('#backup-options-dialog').dialog('close');

                var txt = $('#backup-options').val().trim();
                if (txt.length > 0)
                    txt += '\n';

                var defaultvalue = '';
                for(var o in data.Options)
                    if (data.Options[o].Name == id) {
                        defaultvalue = data.Options[o].DefaultValue;
                        break;
                    }


                txt += '--' + id + '=' + defaultvalue;
                $('#backup-options').val('').val(txt);
                $('#backup-options').focus();

            }});
        }, function() {
        });
    });

    $('#backup-options-dialog').on('configure', function(e, data) {
        $('#backup-options-dialog').empty();

        var s = data.Options.sort(function(a, b){
            if (a == null)
                return -1;
            if (b == null)
                return 1;
            if (a == null && b == null)
                return 0;

            if(a.Name < b.Name) return -1;
            if(a.Name > b.Name) return 1;
            return 0;
        });

        //Fill with jQuery template
        $.tmpl($('#backup-option-template'), s).prependTo($('#backup-options-dialog'));
        $('#backup-options-dialog').find('.backup-option-link').click(function(e) {
            data.callback(e.target.id);
        });

        $('#backup-options-dialog').find('.backup-option-long').each(function(i, e) {
            $(e).html(nl2br($(e).html()));
        });
    });

    var setStateModified = function() {
        if (EDIT_STATE != null)
            EDIT_STATE.dataModified = true;
    };

    $('#edit-dialog-form').find('input').change(setStateModified);
    $('#edit-dialog-form').find('select').change(setStateModified);
    $('#edit-dialog-form').find('textarea').change(setStateModified);

    $('#use-scheduled-run').change(function() {
        if ($('#use-scheduled-run').is(':checked')) {
            $('#use-scheduled-run-details').show();

                var dt = new Date();
                dt.setHours(dt.getHours() + 1);
                dt.setMinutes(0);
                dt.setSeconds(0);

                if ($('#next-run-time').val() == '') {
                    var h = dt.getHours() + '';
                    var m = dt.getMinutes() + '';
                    if (h.length == 1)
                        h = '0' + h;
                    if (m.length == 1)
                        m = '0' + m;
                    $('#next-run-time').val(h + ':' + m);
                }

                if ($('#next-run-date').val() == '') {
                    var y = dt.getFullYear();
                    var d = dt.getDate() + '';
                    var m = (dt.getMonth() + 1) + '';
                    if (d.length == 1)
                        d = '0' + d;
                    if (m.length == 1)
                        m = '0' + m;
                    $('#next-run-date').val(y + '-' + m + '-' + d);
                }

                if ($('#repeat-run-number').val() == '' || $('#repeat-run-number').val() == undefined) {
                    $('#repeat-run-number').val('1');
                    $('#repeat-run-multiplier').val('D');

                    $('#allow-day-mon').attr('checked', true);
                    $('#allow-day-tue').attr('checked', true);
                    $('#allow-day-wed').attr('checked', true);
                    $('#allow-day-thu').attr('checked', true);
                    $('#allow-day-fri').attr('checked', true);
                    $('#allow-day-sat').attr('checked', true);
                    $('#allow-day-sun').attr('checked', true);
                }

        } else {
            $('#use-scheduled-run-details').hide();
        }
    });
});