$(document).ready(function() {

    APP_DATA.plugins.backend['file'] = {
        hasssl: false,
        hideserverandport: true,
        optionalauth: true,

        btnel: null,

        fill_form_map: {
            'server-path': function(dict, key, el, cfgel) {
                var p = [];
                if (dict['server-name'] && dict['server-name'] != '')
                    p.push(dict['server-name']);
                if (dict['server-path'] && dict['server-path'] != '')
                    p.push(dict['server-path']);

                var sep = '/';
                if (this.serverconfig)
                    sep = this.serverconfig.DirectorySeparator;

                p = p.join(sep);
                if (p.indexOf('file://') == 0)
                    p = p.substr('file://'.length);

                $('#server-path').val(p);
            }
        },

        fill_dict_map: {
            'server-path': function(dict, key, el, cfgel) {
                var p =  $(el).val();
                if (p.indexOf('file://') == 0)
                    p = p.substr('file://'.length);
                dict['server-path'] = p;
                dict['server-name'] = '';
            }
        },

        decode_uri: function(url) {
            var dict = EDIT_URI.decode_uri(url);
            if (dict == null || dict['backend-type'] == null) {
                
                dict = dict || {};

                var p = url;
                if (p.indexOf('file://') == 0)
                    p = p.substr('file://'.length);

                if (p.indexOf('?') > 0) {
                    var q = p.substr(p.indexOf('?') + 1);
                    p = p.substr(0, p.length - q.length - 1);
                    q.replace(EDIT_URI.QUERY_REGEXP, function(str, key, val) {
                        if (key)
                            dict['--' + key] = decodeURIComponent(val);
                    });
                }

                $.extend(true, dict, {
                    'source_uri': url,
                    'backend-type': 'file',
                    'server-path': p
                });
            }

            return dict;
        },

        setup: function(dlg, div) {
            $('#server-path').addClass('server-path-file');
            $('#server-path').watermark('mybackup');

            this.btnel = $('<input type="button" value="..." class="browse-button" />').css('width', 'auto');
            this.btnel.insertAfter($('#server-path'));
            this.btnel.click(function() {
                $.browseForFolder({
                    title: 'Select target folder',
                    resolvePath: true,            
                    callback: function(path, display) {
                        $('#server-path').val(path);
                    }
                });
            });

            if (this.serverconfig == null) {
                APP_DATA.getServerConfig(function(data) {
                    this.serverconfig = data;
                });
            }
        },
        cleanup: function(dlg, div) {
            $('#server-path').removeClass('server-path-file');
            if (this.btnel != null) {
                this.btnel.remove();
                this.btnel = null;
            }

        }
    }

    APP_DATA.plugins.backend['webdav'] = {
        defaultport: 80,
        defaultportssl: 443
    }

    APP_DATA.plugins.backend['cloudfiles'] = {
        defaultport: 80,
        defaultportssl: 443
    }

    APP_DATA.plugins.backend['ftp'] = {
        defaultport: 21,
        defaultportssl: 443,
        optionalpassword: true
    }

    APP_DATA.plugins.backend['ssh'] = {
        defaultport: 22,
        optionalauth: true,
        hasssl: false
    }

    APP_DATA.plugins.backend['onedrive'] = {
        PLUGIN_LOGIN_LINK: 'https://duplicati-oauth-handler.appspot.com/',

        hideserverandport: true,
        hideusernameandpassword: true,
        optionalauth: true,
        hasssl: false,
        fetchtoken: null,

        passwordlabel: 'AuthID',

        fill_form_map: {
            '--authid': 'authid'
        },

        fill_dict_map: {
            'authid': '--authid'
        },

        validate: function(form, values) {
            delete values['auth-username'];
            delete values['auth-password'];
            delete values['--auth-username'];
            delete values['--auth-password'];

            if (values['--authid'] == '')
                return EDIT_URI.validation_error($('#server-password'), 'You must fill in an AuthID');


            return EDIT_URI.validate_input(values, true);
        },

        setup: function(dlg, div) {


            var authid = EDIT_URI.createFieldset({label: 'AuthID', name: 'authid', after: $('#server-path'), watermark: 'Enter the AuthID value'});

            authid.label.addClass('action-link');


            var self = this;
            authid.label.click(function() {

                self.fetchtoken = Math.random().toString(36).substr(2) + Math.random().toString(36).substr(2);
                var ft = self.fetchtoken;

                var countDown = 100;
                var url = self.PLUGIN_LOGIN_LINK + '?token=' + self.fetchtoken;
                var real_link = $('<a />').text(authid.label.text())
                    .addClass('edit-dialog-label')
                    .addClass('action-link')
                    .attr('href', url)
                    .attr('target', '_blank');

                authid.label.attr('href', url).addClass('action-link');
                real_link.insertAfter(authid.label);

                setTimeout(function() { authid.label.hide(); }, 500);

                var w = 400;
                var h = 550;

                var left = (screen.width/2)-(w/2);
                var top = (screen.height/2)-(h/2);                
                var wnd = window.open(url, '_blank', 'height=' + h +',width=' + w + ',menubar=0,status=0,titlebar=0,toolbar=0,left=' + left + ',top=' + top)

                var recheck = function() {
                    countDown--;
                    if (countDown > 0 && ft == self.fetchtoken) {
                        $.ajax({
                            url: self.PLUGIN_LOGIN_LINK + 'fetch',
                            dataType: 'jsonp',
                            data: {'token': ft}
                        })
                        .done(function(data) {
                            if (data.authid) {
                                authid.field.val(data.authid);
                                wnd.close();
                            } else {
                                setTimeout(recheck, 3000);
                            }
                        })
                        .fail(function() {
                            setTimeout(recheck, 3000);
                        });
                    } else {
                        if (wnd != null)
                            wnd.close();
                    }                  
                };

                setTimeout(recheck, 6000);

                return false;
            });
        },

        cleanup: function(dlg, div) {
            this.fetchtoken = null;
        }
    }

    APP_DATA.plugins.backend['googledocs'] = {
        hideserverandport: true
    }


    APP_DATA.plugins.backend['s3'] = {

        PLUGIN_S3_LINK: 'https://portal.aws.amazon.com/gp/aws/developer/registration/index.html',

        hasssl: true,
        hideserverandport: true,
        usernamelabel: 'AWS Access ID',
        passwordlabel: 'AWS Secret Key',
        usernamewatermark: 'AWS Access ID',
        passwordwatermark: 'AWS Secret Key',
        serverdrop_field: null,
        regiondrop_field: null,
        bucket_field: null,
        known_hosts: null,
        known_regions: null,

        setup_hosts_after_config: function() {
            if (this.known_hosts != null && this.serverdrop_field != null)
            {
                var servers = [];
                for (var k in this.known_hosts)
                    servers.push({label: k + ' (' + this.known_hosts[k] + ')', value: this.known_hosts[k]});

                this.serverdrop_field.autocomplete({
                    minLength: 0,
                    source: servers, 
                });
                var self = this;
                this.serverdrop_field.click(function() {  
                    self.serverdrop_field.autocomplete('search', '');
                });
            }
        },

        setup_regions_after_config: function() {
            if (this.known_regions != null && this.regiondrop_field != null) {
                var buckets = [];
                for (var k in this.known_regions)
                    buckets.push({label: k + ' (' + this.known_regions[k] + ')', value: this.known_regions[k]});
                
                this.regiondrop_field.autocomplete({
                    minLength: 0,
                    source: buckets, 
                });
                var self = this;
                this.regiondrop_field.click(function() {  
                    self.regiondrop_field.autocomplete('search', '');
                });
            }
        },

        setup: function(dlg, div) {
            var self = this;

            if (self.known_hosts == null) {
                APP_DATA.callServer({action: 'send-command', command: 's3-getconfig', 's3-config': 'Providers'}, function(data) {
                    self.known_hosts = data.Result;
                    self.setup_hosts_after_config();
                },
                function(data, success, message) {
                    alert('Failed to get S3 config: ' + message);
                });
            } else {
                this.setup_hosts_after_config();
            }

            if (self.known_regions == null) {
                APP_DATA.callServer({action: 'send-command', command: 's3-getconfig', 's3-config': 'Regions'}, function(data) {
                    self.known_regions = data.Result;
                    self.setup_regions_after_config();
                },
                function(data, success, message) {
                    alert('Failed to get S3 config: ' + message);
                });
            } else {
                this.setup_regions_after_config();
            }

            $('#server-path-label').hide();
            $('#server-path').hide();

            var serverdrop = EDIT_URI.createFieldset({label: 'S3 servername', name: 's3-server', after: $('#server-path'), watermark: 'Click for a list of providers'});
            var bucketfield = EDIT_URI.createFieldset({label: 'S3 Bucket name', name: 's3-bucket', after: $('#server-username-and-password'), title: 'Use / to access subfolders in the bucket', watermark: 'Enter bucket name'});
            var regiondrop = EDIT_URI.createFieldset({label: 'Bucket create region', name: 's3-region', before: $('#server-options-label'), watermark: 'Click for a list of regions', title: 'Note that region is only used when creating buckets'});
            var rrscheck = EDIT_URI.createFieldset({'label': 'Use RRS', name: 's3-rrs', type: 'checkbox', before: $('#server-options-label'), title: 'Reduced Redundancy Storage is cheaper, but less reliable'});
            var signuplink = EDIT_URI.createFieldset({'label': '&nbsp;', href: this.PLUGIN_S3_LINK, type: 'link', before: bucketfield.outer, 'title': 'Click here for the sign up page'});

            signuplink.outer.css('margin-bottom', '10px');

            this.serverdrop_field = serverdrop.field;
            this.regiondrop_field = regiondrop.field;
            this.bucket_field = bucketfield.field;
        },

        cleanup: function(dlg, div) {
            $('#server-path-label').show();
            $('#server-path').show();
            this.serverdrop_field = null;
            this.regiondrop_field = null;
            this.bucket_field = null;
        },

        validate: function(dlg, values) {
            if (!EDIT_URI.validate_input(values, true))
                return;
            if (values['--s3-server-name'] == '')
                return EDIT_URI.validation_error(this.serverdrop_field, 'You must fill in or select the S3 server to use');                
            if (values['s3-bucket'] == '')
                return EDIT_URI.validation_error(this.bucket_field, 'You must enter a S3 bucket name');
            if (values['server-name'].toLowerCase() != values['server-name']) {
                if (!confirm('The bucket name must be all lower case, convert automatically?')) {
                    this.bucket_field.focus();
                    return false;
                }
                values['server-name'] = values['server-name'].toLowerCase();
                this.bucket_field.val(values['server-name']);
            }
            if (values['server-name'].indexOf(values['--auth-username'].toLowerCase()) != 0) {
                if (confirm('The bucket name should start with your username, append automatically?')) {
                    values['server-name'] = values['--auth-username'].toLowerCase() + '-' + values['server-name'];
                    this.bucket_field.val(values['server-name']);
                }
            }

            return true;
        },

        fill_form_map: {
            'server-name': 's3-bucket',
            '--s3-server-name': 's3-server',
            '--s3-use-rrs': 's3-rrs',
            '--s3-location-constraint': 's3-region'
        },

        fill_dict_map: {
            's3-bucket': 'server-name',
            's3-server': '--s3-server-name',
            's3-rrs': '--s3-use-rrs',
            's3-region': '--s3-location-constraint'
        }
    }

    APP_DATA.plugins.backend['azure'] = {

        PLUGIN_AZURE_LINK: 'https://account.windowsazure.com/Home/Index',

        hasssl: false,
        hideserverandport: true,
        usernamelabel: 'Account Name',
        passwordlabel: 'Access Key',
        usernamewatermark: 'Account Name',
        passwordwatermark: 'Access Key',
        container_field: null,

        setup: function (dlg, div) {
            $('#server-path-label').hide();
            $('#server-path').hide();

            $('#server-username').attr('placeholder', this.usernamewatermark);
            $('#server-password').attr('placeholder', this.passwordwatermark);

            var containerfield = EDIT_URI.createFieldset({ label: 'Container Name', name: 'azure-container', after: $('#server-username-and-password'), title: 'Container name', watermark: 'Enter container name' });
            this.container_field = containerfield.field;

            var signuplink = EDIT_URI.createFieldset({ 'label': '&nbsp;', href: this.PLUGIN_AZURE_LINK, type: 'link', before: $('#server-options-label'), 'title': 'Click here to sign in or register' });
            signuplink.outer.css('margin-bottom', '10px');

        },

        cleanup: function (dlg, div) {
            $('#server-path-label').show();
            $('#server-path').show();
            this.container_field = null;
        },

        validate: function (dlg, values) {
            if (!EDIT_URI.validate_input(values, true))
                return false;
            if (values['azure-container'] == '')
                return EDIT_URI.validation_error(this.container_field, 'You must enter an Azure container name');

            values['server-name'] = values["azure-container"].toLowerCase();
            this.container_field.val(values['server-name']);

            return true;
        },

        fill_form_map: {
            'server-name': 'azure-container'
        },

        fill_dict_map: {
            'azure-container': 'server-name'
        }
    }
});