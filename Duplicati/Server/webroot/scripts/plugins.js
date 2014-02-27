//TODO: These can be fetched from the server data, but requires some string parsing to get right

PLUGIN_S3_HOSTS = {
    'Amazon S3': 's3.amazonaws.com',
    'Hosteurope': 'cs.hosteurope.de',
    'Dunkel': 'dcs.dunkel.de',
    'DreamHost': 'objects.dreamhost.com'
}

//Updated list: http://docs.amazonwebservices.com/general/latest/gr/rande.html#s3_region
PLUGIN_S3_LOCATIONS = {
    '(default)': '',
    'Europe (EU, Ireland)': 'EU',
    'US East (Northern Virginia)': 'us-east-1',
    'US West (Northen California)': 'us-west-1',
    'US West (Oregon)': 'us-west-2',
    'Asia Pacific (Singapore)': 'ap-southeast-1',
    'Asia Pacific (Sydney)': 'ap-southeast-2',
    'Asia Pacific (Tokyo)': 'ap-northeast-1',
    'South America (Sao Paulo)': 'sa-east-1'
};

PLUGIN_S3_SERVER_LOCATIONS = {
    'EU': 's3-eu-west-1.amazonaws.com',
    'eu-west-1': 's3-eu-west-1.amazonaws.com',
    'us-east-1': 's3.amazonaws.com',
    'us-west-1': 's3-us-west-1.amazonaws.com',
    'us-west-2': 's3-us-west-2.amazonaws.com',
    'ap-southeast-1': 's3-ap-southeast-1.amazonaws.com',
    'ap-southeast-2': 's3-ap-southeast-2.amazonaws.com',
    'ap-northeast-1': 's3-ap-northeast-1.amazonaws.com',
    'sa-east-1': 's3-sa-east-1.amazonaws.com'
};

PLUGIN_S3_LINK = 'https://portal.aws.amazon.com/gp/aws/developer/registration/index.html';


$(document).ready(function() {

    APP_DATA.plugins.backend['file'] = {
        hasssl: false,
        hideserverandport: true,
        serverpathlabel: 'Path or UNC',
        custom_callback: function(dlg, div) {
            //$('#server-path').watermark('/my/data');
            //div.text('Awesome plugin stuff');
        },
        custom_cleanup: function(dlg, div) {
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
        defaultportssl: 443
    }

    APP_DATA.plugins.backend['ssh'] = {
        defaultport: 22,
        hasssl: false
    }

    APP_DATA.plugins.backend['skydrive'] = {
        hideserverandport: true
    }

    APP_DATA.plugins.backend['googledocs'] = {
        hideserverandport: true
    }


    APP_DATA.plugins.backend['s3'] = {
        hasssl: true,
        hideserverandport: true,
        usernamelabel: 'AWS Access ID',
        passwordlabel: 'AWS Secret Key',
        usernamewatermark: 'AWS Access ID',
        passwordwatermark: 'AWS Secret Key',

        custom_callback: function(dlg, div) {
            $('#server-path-label').hide();
            $('#server-path').hide();

            var serverdrop = EDIT_URI.createFieldset({label: 'S3 servername', after: $('#server-path'), watermark: 'Click for a list of providers'});
            var bucketfield = EDIT_URI.createFieldset({label: 'S3 Bucket name', after: $('#server-username-and-password'), title: 'Use / to access subfolders in the bucket', watermark: 'Enter bucket name'});
            var regiondrop = EDIT_URI.createFieldset({label: 'Bucket create region', before: $('#server-options-label'), watermark: 'Click for a list of regions', title: 'Note that region is only used when creating buckets'});
            var rrscheck = EDIT_URI.createFieldset({'label': 'Use RRS', type: 'checkbox', before: $('#server-options-label'), title: 'Reduced Redundancy Storage is cheaper, but less reliable'});
            var signuplink = EDIT_URI.createFieldset({'label': '&nbsp;', href: PLUGIN_S3_LINK, type: 'link', before: bucketfield.outer, 'title': 'Click here for the sign up page'});

            signuplink.outer.css('margin-bottom', '10px');

            var servers = [];
            for (var k in PLUGIN_S3_HOSTS)
                servers.push({label: k + ' (' + PLUGIN_S3_HOSTS[k] + ')', value: PLUGIN_S3_HOSTS[k]});

            var buckets = [];
            for (var k in PLUGIN_S3_LOCATIONS)
                buckets.push({label: k, value: PLUGIN_S3_LOCATIONS[k]});

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
        },
        custom_cleanup: function(dlg, div) {
            $('#server-path-label').show();
            $('#server-path').show();
        }
    }

});