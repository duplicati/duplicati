//TODO: These can be fetched from the server data, but requires some string parsing to get right

$(document).ready(function() {

    APP_DATA.plugins.backend['file'] = {
        hasssl: false,
        hideserverandport: true,
        optionalauth: true,
        serverpathlabel: 'Path or UNC',

        btnel: null,

        fill_form_map: {
            'server-path': false,
            'server-name': function(dict, key, el, cfgel) {
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
            'server-name': false,
            'server-path': function(dict, key, el, cfgel) {
                var p =  $(el).val();
                if (p.indexOf('file://') == 0)
                    p = p.substr('file://'.length);
                dict['server-name'] = p;
                dict['server-path'] = '';
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

            this.btnel = $('<input type="button" value="..." class="browse-button" />').css('width', 'auto').button();
            this.btnel.insertAfter($('#server-path'));
            this.btnel.click(function() {
                $.browseForFolder({
                    title: 'Select target folder',
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

    APP_DATA.plugins.backend['skydrive'] = {
        hideserverandport: true
    }

    APP_DATA.plugins.backend['googledocs'] = {
        hideserverandport: true
    }


    APP_DATA.plugins.backend['s3'] = {
        PLUGIN_S3_HOSTS: {
            'Amazon S3': 's3.amazonaws.com',
            'Hosteurope': 'cs.hosteurope.de',
            'Dunkel': 'dcs.dunkel.de',
            'DreamHost': 'objects.dreamhost.com'
        },
        //Updated list: http://docs.amazonwebservices.com/general/latest/gr/rande.html#s3_region
        PLUGIN_S3_LOCATIONS: {
            '(default)': '',
            'Europe (EU, Ireland)': 'EU',
            'US East (Northern Virginia)': 'us-east-1',
            'US West (Northen California)': 'us-west-1',
            'US West (Oregon)': 'us-west-2',
            'Asia Pacific (Singapore)': 'ap-southeast-1',
            'Asia Pacific (Sydney)': 'ap-southeast-2',
            'Asia Pacific (Tokyo)': 'ap-northeast-1',
            'South America (Sao Paulo)': 'sa-east-1'
        },
        PLUGIN_S3_SERVER_LOCATIONS: {
            'EU': 's3-eu-west-1.amazonaws.com',
            'eu-west-1': 's3-eu-west-1.amazonaws.com',
            'us-east-1': 's3.amazonaws.com',
            'us-west-1': 's3-us-west-1.amazonaws.com',
            'us-west-2': 's3-us-west-2.amazonaws.com',
            'ap-southeast-1': 's3-ap-southeast-1.amazonaws.com',
            'ap-southeast-2': 's3-ap-southeast-2.amazonaws.com',
            'ap-northeast-1': 's3-ap-northeast-1.amazonaws.com',
            'sa-east-1': 's3-sa-east-1.amazonaws.com'
        },
        PLUGIN_S3_LINK: 'https://portal.aws.amazon.com/gp/aws/developer/registration/index.html',

        hasssl: true,
        hideserverandport: true,
        usernamelabel: 'AWS Access ID',
        passwordlabel: 'AWS Secret Key',
        usernamewatermark: 'AWS Access ID',
        passwordwatermark: 'AWS Secret Key',
        serverdrop_field: null,
        bucket_field: null,

        setup: function(dlg, div) {
            $('#server-path-label').hide();
            $('#server-path').hide();

            var serverdrop = EDIT_URI.createFieldset({label: 'S3 servername', name: 's3-server', after: $('#server-path'), watermark: 'Click for a list of providers'});
            var bucketfield = EDIT_URI.createFieldset({label: 'S3 Bucket name', name: 's3-bucket', after: $('#server-username-and-password'), title: 'Use / to access subfolders in the bucket', watermark: 'Enter bucket name'});
            var regiondrop = EDIT_URI.createFieldset({label: 'Bucket create region', name: 's3-region', before: $('#server-options-label'), watermark: 'Click for a list of regions', title: 'Note that region is only used when creating buckets'});
            var rrscheck = EDIT_URI.createFieldset({'label': 'Use RRS', name: 's3-rrs', type: 'checkbox', before: $('#server-options-label'), title: 'Reduced Redundancy Storage is cheaper, but less reliable'});
            var signuplink = EDIT_URI.createFieldset({'label': '&nbsp;', href: this.PLUGIN_S3_LINK, type: 'link', before: bucketfield.outer, 'title': 'Click here for the sign up page'});

            signuplink.outer.css('margin-bottom', '10px');

            var servers = [];
            for (var k in this.PLUGIN_S3_HOSTS)
                servers.push({label: k + ' (' + this.PLUGIN_S3_HOSTS[k] + ')', value: this.PLUGIN_S3_HOSTS[k]});

            var buckets = [];
            for (var k in this.PLUGIN_S3_LOCATIONS)
                buckets.push({label: k, value: this.PLUGIN_S3_LOCATIONS[k]});

            regiondrop.field.autocomplete({
                minLength: 0,
                source: buckets, 
            });

            serverdrop.field.autocomplete({
                minLength: 0,
                source: servers, 
            });

            serverdrop.field.click(function() {  
                serverdrop.field.autocomplete('search', '');
            });
            regiondrop.field.click(function() {  
                regiondrop.field.autocomplete('search', '');
            });

            this.serverdrop_field = serverdrop.field;
            this.bucket_field = bucketfield.field;
        },

        cleanup: function(dlg, div) {
            $('#server-path-label').show();
            $('#server-path').show();
            this.serverdrop_field = null;
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

});