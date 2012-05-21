(function() {

	try { var gs = APP_SCOPE; }
	catch (e) { throw new Error('Loading sequence error, namespace not defined'); }
		
	var ls = {};
	
	//Remove this line to shield the inner of the class from the outer
	APP_SCOPE.localscope = ls;
	
	APP_SCOPE.parseBool = function(v) 
	{
		if (typeof(v) == "string")
		{
			if (v == "true" || v == "yes" || v == "on" || v == "1")
				return true;
			else
				return false;
		}
		else
			return v || false;
	};
	
	APP_SCOPE.toJsDate = function(str) {
		if (str == null)
			return null;
		
		var strtmp = str.replace(/\/Date\((-?\d+(((\+|\-)\d+)|Z|z)?)\)\//gi, "new Date($1)");
		return eval(strtmp);
	};
	
	APP_SCOPE.formatSizeString = function(size) {
		size = parseInt(size);
		if (isNaN(size))
			return size;
			
		if (size >= 1024 * 1024 * 1024 * 1024)
			return (size / (1024 * 1024 * 1024 * 1024.0)).toFixed(2) + ' TB';
		else if (size >= 1024 * 1024 * 1024)
			return (size / (1024 * 1024 * 1024.0)).toFixed(2) + ' GB';
		else if (size >= 1024 * 1024)
			return (size / (1024 * 1024.0)).toFixed(2) + ' MB';
		else if (size >= 1024)
			return (size / (1024.0)).toFixed(2) + ' KB';
		else
			return size + ' bytes';		
	}

	APP_SCOPE.createHtmlItems = function(html) {
		if (ls.creator_div == null) {
			ls.creator_div = document.createElement("div");
		}
		
		ls.creator_div.innerHTML = html;
		var childs = [];
		for(var i = 0; i < ls.creator_div.childNodes.length; i++)
			childs[i] = ls.creator_div.childNodes[i];

		ls.creator_div.innerHTML = '';
		
		return childs;			
	};
	
	APP_SCOPE.appendHtml = function(parent, html)
	{
		var itm = this.createHtmlItems(html);
		for(var i = 0; i < itm.length; i++) {
			parent.appendChild(itm[i]);
		}
		
	};
		
	APP_SCOPE.getActionUrl = function(action) {
		//During the design phase, we emulate the server with a folder
		//return './server-responses/' + encodeURIComponent(action) + '.txt';
		return APP_SCOPE.ServerURL + 'control.cgi?action=' + encodeURIComponent(action);
	}

	APP_SCOPE.getCurrentState = function(callback) {
		$.getJSON(this.getActionUrl('get-current-state'), callback);
	};

	APP_SCOPE.getApplicationSettings = function(callback) {
		$.getJSON(this.getActionUrl('list-application-settings'), callback);
	};

	APP_SCOPE.getBackupSchedules = function(callback) {
		$.getJSON(this.getActionUrl('list-schedules'), callback);
	};

	APP_SCOPE.getBackupDefaults = function(callback) {
		$.getJSON(this.getActionUrl('get-backup-defaults'), callback);
	};
	
	APP_SCOPE.getInstalledCompressionModules = function(callback) {
		if (APP_SCOPE.CompressionModules == null) {
			$.getJSON(this.getActionUrl('list-installed-compression-modules'), function(resp) {
				APP_SCOPE.CompressionModules = resp;
				callback(APP_SCOPE.CompressionModules);
			});
		} else {
			callback(APP_SCOPE.CompressionModules);
		}
	};
	
	APP_SCOPE.getInstalledGenericModules = function(callback) {
		if (APP_SCOPE.CompressionModules == null) {
			$.getJSON(this.getActionUrl('list-installed-generic-modules'), function(resp) {
				APP_SCOPE.GenericModules = resp;
				callback(APP_SCOPE.GenericModules);
			});
		} else {
			callback(APP_SCOPE.GenericModules);
		}
	};

	APP_SCOPE.getInstalledEncryptionModules = function(callback) {
		if (APP_SCOPE.EncryptionModules == null) {
			$.getJSON(this.getActionUrl('list-installed-encryption-modules'), function(resp) {
				APP_SCOPE.EncryptionModules = resp;
				callback(APP_SCOPE.EncryptionModules);
			});
		} else {
			callback(APP_SCOPE.EncryptionModules);
		}
	};

	
	APP_SCOPE.getQueryParameter = function(name) {
		name = name.replace(/[\[]/, "\\\[").replace(/[\]]/, "\\\]");
		var regexS = "[\\?&]" + name + "=([^&#]*)";
		var regex = new RegExp(regexS);
		var results = regex.exec(window.location.search);
		if(results == null)
			return "";
		else
			return decodeURIComponent(results[1].replace(/\+/g, " "));
	};
	
	$(document).ready(function(){ 
		ads = null;
		adsp = null;
		extmin = null;
	
		if (window.external)
		{
			try { ads = window.external.AddSearch; }
			catch (e) {}
			try { adsp = window.external.AddSearchProvider; }
			catch (e) {}
			try { extmin = window.external.setMinSiz; }
			catch (e) {}
		}
	
		if (window.external && ads == null && adsp == null && extmin != null) {
			APP_SCOPE.external = window.external;
			APP_SCOPE.activateFunction = function(code) {
				APP_SCOPE.external.openWindow(code); 
			}
		} else {
			APP_SCOPE.activateFunction = function(code) {
				alert("Unimplemented " + code);
			}
		}
		
		$(".ui-menu-item a").each(function(i, e) {
			var x = $(e)
			x.hover(
				function(){x.addClass("ui-state-hover")},
				function(){x.removeClass("ui-state-hover")}
			);
		});
	});
	
})();