(function() {

	try { var gs = APP_SCOPE; }
	catch (e) { throw new Error('Loading sequence error, namespace not defined'); }
		
	var ls = {};
	
	//Remove this line to shield the inner of the class from the outer
	APP_SCOPE.localscope = ls;
	
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
	
	APP_SCOPE.applyRoundedCornerClass = function() {
		$('.rounded-corner-box').each(function(index, el) {
			if (!$(el).hasClass('created-round-corner-box')) {
				$(el).removeClass('rounded-corner-box');
				var x = el.innerHTML;
				el.innerHTML = '<table class="round-corner-box created-round-corner-box"><tr class="top"><td class="left"></td><td class="middle"></td><td class="right"></td></tr><tr class="center"><td class="left"></td><td class="middle">' + x + '</div></td><td class="right"></td></tr><tr class="bottom"><td class="left"></td><td class="middle"></td><td class="right"></td></tr></table>';
			}
		});
	}

	APP_SCOPE.getActionUrl = function(action) {
		//During the design phase, we emulate the server with a folder
		//return './server-responses/' + encodeURIComponent(action) + '.txt';
		return APP_SCOPE.ServerURL + 'control.cgi?action=' + encodeURIComponent(action);
	}

	APP_SCOPE.getApplicationSettings = function(callback) {
		$.getJSON(this.getActionUrl('list-application-settings'), callback);
	};

	APP_SCOPE.getBackupSchedules = function(callback) {
		$.getJSON(this.getActionUrl('list-schedules'), callback);
	};
	
	$(document).ready(function(){ 
		if (window.external) {
			window.activateFunction = function(code) { 
				window.external.openWindow(code); 
			}
		} else {
			window.activateFunction = function(code) {
				alert("Unimplemented " + code);
			}
		}
		
		APP_SCOPE.applyRoundedCornerClass(); 
	});
	
})();