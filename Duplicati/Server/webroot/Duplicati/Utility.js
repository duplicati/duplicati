Ext.define('Duplicati.Utility', {
	statics: {
	
		strings: {
			terabyteShort: 'TB',
			gigabyteShort: 'GB',
			megabyteShort: 'MB',
			kilobyteShort: 'KB',
			bytesShort: 'bytes',

			prefixAgo: null,
			prefixFromNow: "in",
			suffixAgo: "ago",
			suffixFromNow: null,
			seconds: "less than a minute",
			minute: "about a minute",
			minutes: "%d minutes",
			hour: "about an hour",
			hours: "about %d hours",
			day: "a day",
			days: "%d days",
			month: "about a month",
			months: "%d months",
			year: "about a year",
			years: "%d years",
			wordSeparator: " "
        },
        
        timeAgoTimer: window.setInterval('Duplicati.Utility.updateTimeAgoFields()', 1000 * 150),
	
		//Simplified version of jquery.timeago
		timeAgo: function(date) {
			var distanceMillis = (new Date().getTime() - date.getTime());
			var strings = this.strings;

			var prefix = strings.prefixAgo;
			var suffix = strings.suffixAgo;
			if (distanceMillis < 0) {
				prefix = strings.prefixFromNow;
				suffix = strings.suffixFromNow;
			}
			
			var seconds = Math.abs(distanceMillis) / 1000;
			var minutes = seconds / 60;
			var hours = minutes / 60;
			var days = hours / 24;
			var years = days / 365;
						
			var words = seconds < 45 && strings.seconds.replace(/%d/i, Math.round(seconds)) ||
			seconds < 90 && strings.minute ||
			minutes < 45 && strings.minutes.replace(/%d/i, Math.round(minutes)) ||
			minutes < 90 && strings.hour ||
			hours < 24 && strings.hours.replace(/%d/i, Math.round(hours)) ||
			hours < 42 && strings.day ||
			days < 30 && strings.days.replace(/%d/i, Math.round(days)) ||
			days < 45 && strings.month ||
			days < 365 && strings.months.replace(/%d/i, Math.round(days / 30)) ||
			years < 1.5 && strings.year ||
			strings.years.replace(/%d/i, Math.round(years));
			
			var separator = strings.wordSeparator === undefined ?  " " : strings.wordSeparator;
			return Ext.String.trim([prefix, words, suffix].join(separator));
		},
	
		updateTimeAgoFields: function() {
			var parent = Ext.get(document);
			var els = parent.query('.marker-time-ago');
			for(var i = 0; i < els.length; i++) {
				var time = this.parseJsonDate(els[i].title);
				if (els[i].nextSibling.nodeType == 3)
					els[i].nextSibling.nodeValue = this.timeAgo(time);
				else if (els[i].nextSibling.innerText != null)
					els[i].nextSibling.innerText = this.timeAgo(time);
			}
		},
		
	
		parseJsonDate: function(str) {
			if (str == null)
				return null;
				
			if (Ext.String.trim(str).length == 0)
				return null;
				
			if (str.indexOf("\/") == 0)
			{
				var strtmp = str.replace(/\/Date\((-?\d+(((\+|\-)\d+)|Z|z)?)\)\//gi, "new Date($1)");
				return eval(strtmp);
			}
			else
			{
				return new Date(str);
			}
		},

		formatSecondsAsTime: function(seconds) {
			seconds = parseInt(seconds);
			var hours = parseInt(Math.floor(seconds / (60 * 60)));
			seconds = seconds - (hours * 60 * 60);
			var minutes = parseInt(Math.floor(seconds / 60));
			seconds = seconds - (minutes * 60);
			var res = '';
			if (hours > 0) {
				res += hours + ':';
				if (minutes < 10)
					res += '0';
			}
			
			res += minutes + ':' ;
			if (seconds < 10)
				res += '0';
			res += seconds;

			return res;
		},

		formatSizeString: function(size) {
			size = parseInt(size);
			if (isNaN(size))
				return size;

			var strings = this.strings;
				
			if (size >= 1024 * 1024 * 1024 * 1024)
				return (size / (1024 * 1024 * 1024 * 1024.0)).toFixed(2) + ' ' + strings.terabyteShort;
			else if (size >= 1024 * 1024 * 1024)
				return (size / (1024 * 1024 * 1024.0)).toFixed(2) + ' ' + strings.gigabyteShort;
			else if (size >= 1024 * 1024)
				return (size / (1024 * 1024.0)).toFixed(2) + ' ' + strings.megabyteShort;
			else if (size >= 1024)
				return (size / (1024.0)).toFixed(2) + ' ' + strings.kilobyteShort;
			else
				return size + ' ' + strings.bytesShort;		
		},
		
		estimatedFreeSpace: function(assignedQuota, freeQuota, backupSize) {
			if (assignedQuota != null && freeQuota != null && backupSize != null)
				return Math.min(parseInt(assignedQuota) - parseInt(backupSize), parseInt(freeQuota));
			else if (assignedQuota != null && backupSize != null)
				return parseInt(assignedQuota) - parseInt(backupSize);
			else if (freeQuota != null)
				return parseInt(freeQuota);
			else
				return null;
		}
	}
});