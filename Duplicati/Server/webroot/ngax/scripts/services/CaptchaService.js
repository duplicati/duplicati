backupApp.service('CaptchaService', function(DialogService, AppService, AppUtils, gettextCatalog) {
	this.active = null;
	var self = this;

    this.Authorize = function(title, message, target, callback) {

    	var cb = self.active = {
    		'message': message,
    		'target': target,
    		'callback': callback,
    		'attempts': 0,
    		'hasfailed': false,
    		'verifying': false
    	};

    	self.attemptSolve = function() {
    		if (cb.attempts >= 3) {
    			cb.attempts = 0;
    			cb.token = null;
    		}

	    	DialogService.htmlDialog(title, 'templates/captcha.html', [gettextCatalog.getString('Cancel'), gettextCatalog.getString('OK')], function(btn) {
	    		if (btn != 1) {
	    			self.active = null;
	    			return;
	    		}

				cb.attempts += 1;
				cb.verifying = true;
				

				DialogService.dialog(gettextCatalog.getString('Verifying answer'), gettextCatalog.getString('Verifying ...'), [], function() {}, function() {

		    		AppService.post('/captcha/' + encodeURIComponent(cb.token), {'answer': cb.answer, 'target': cb.target}).then(function(resp) {
		    			DialogService.dismissCurrent();
		    			self.active = null;
		    			cb.callback(cb.token, cb.answer);
		    		}, function(err) {

		    			DialogService.dismissCurrent();
						cb.verifying = false;
						cb.hasfailed = true;
						if (err.status == 400)
							self.attemptSolve();
						else
							AppUtils.connectionError(err);
		    		});

		    	});
	    	});
    	};

    	self.attemptSolve();
    };
});