$(document).ready(function(){
	$( "#edit-window-tabs" ).tabs();
	
	if (APP_SCOPE.external)
		APP_SCOPE.external.setMinSize(600, 550);

	APP_SCOPE.loadsNeeded = 3;

	APP_SCOPE.getInstalledEncryptionModules(function(resp) {
		APP_SCOPE.afterElementLoaded();
	});

	APP_SCOPE.getInstalledCompressionModules(function(resp) {
		APP_SCOPE.afterElementLoaded();
	});
		
	var action = APP_SCOPE.getQueryParameter("action");
	if (action == "add-backup") {
		//Get backup defaults
		APP_SCOPE.getBackupDefaults(function(resp) {
			window.editObj = resp;
			APP_SCOPE.afterElementLoaded();
		});
	} else if (action == "edit-backup") {
		//Get the backup in question
		var id = APP_SCOPE.getQueryParameter("id");
		
		//Get backup defaults
		APP_SCOPE.getScheduleSettings(id, function(resp) {
			window.editObj = resp;
			APP_SCOPE.afterElementLoaded();
		});
	} else {
		alert("Unexpected call, no action was given?");
	}
	
	//Hook up some events
	$("#edit-window-encryption-method").each(function(index, el) {
		el.addEventListener("change", function() {
			var sel = $("#edit-window-encryption-method")[0];
			var el = sel.options.item(sel.selectedIndex);
			if (el.value == "") {
				$("#edit-window-encryption-passphrase-box").hide();
				$("#edit-window-encryption-passphrase-repeat-box").hide();
				$("#edit-window-encryption-passphrase-generate").hide();
			} else {
				$("#edit-window-encryption-passphrase-box").show();
				$("#edit-window-encryption-passphrase-repeat-box").show();
				$("#edit-window-encryption-passphrase-generate").show();
			}
		}, false);
	});
	
});

(function(){

	//Local function that updates the UI to reflect the current editObj
	APP_SCOPE.updateUI = function(o)
	{
		//Display all available encryption modules
		$("#edit-window-encryption-method").each(function(index, el) { 
			while(el.options.length > 0)
				el.options.remove(0);
			
			for(var i = 0; i < APP_SCOPE.EncryptionModules.length; i++)
			{
				var opt = document.createElement("option");
				opt.value = APP_SCOPE.EncryptionModules[i].FilenameExtension;
				opt.text = APP_SCOPE.EncryptionModules[i].DisplayName;
				
				el.options.add(opt);
			}
		
			//Append the "No encryption" choice
			var opt = document.createElement("option");
			opt.value = "";
			opt.text = "No encryption"; //TODO: Translate
			el.options.add(opt);
			
		});
	
	
		//Now display all field values
		$("#edit-window-backup-name").each(function(index, el) { el.value = o.Name; });
		var labelString = null;
		if (o.Labels != null)
		{
			for(var i = 0; i < o.Labels.length; i++)
				if (labelString == null)
					labelString = o.Labels[i];
				else
					labelString = labelString + ", " + o.Labels[i];
		}
		$("#edit-window-backup-labels").each(function(index, el) { el.value = labelString; });
		$("#edit-window-encryption-method").each(function(index, el) { 
			var ix = -1;
			var none = -1;
			for(var i = 0; i < el.options.length; i++) {
				var opt = el.options.item(i);
				if (opt.value.toLowerCase() == o.EncryptionModule.toLowerCase())
					ix = i;
				if (opt.value == "")
					none = i;
			}
			
			if (ix >= 0)
				el.selectedIndex = ix;
			else if (none >= 0)
				el.selectedIndex = none;
			
		});
		
		if (o.EncryptionSettings != null && o.EncryptionSettings["--passphrase"] !== undefined)
			$("#edit-window-encryption-passphrase").each(function(index, el) { el.value = o.EncryptionSettings["--passphrase"]; });
	
		$("#edit-window-standard-locations-my-documents").each(function(index, el) { el.checked = APP_SCOPE.parseBool(o.Settings["Files:IncludeDocuments"]); });
		$("#edit-window-standard-locations-my-music").each(function(index, el) { el.checked = APP_SCOPE.parseBool(o.Settings["Files:IncludeMusic"]); });
		$("#edit-window-standard-locations-my-videos").each(function(index, el) { el.checked = APP_SCOPE.parseBool(o.Settings["Files:IncludeVideos"]); });
		$("#edit-window-standard-locations-my-pictures").each(function(index, el) { el.checked = APP_SCOPE.parseBool(o.Settings["Files:IncludeImages"]); });
		$("#edit-window-standard-locations-my-appdata").each(function(index, el) { el.checked = APP_SCOPE.parseBool(o.Settings["Files:IncludeAppData"]); });
		
		//Clean up 
		$("#edit-window-additional-paths-container").empty();
		
		var additionalPathTemplate = "<div class=\"edit-window-additional-path\">%s <div class=\"edit-window-remove-additional-button\"></div></div>";
				
		//Then add all additional paths
		var additionalPaths = "";
		if (o.SourcePaths != null)
			for(var i = 0; i < o.SourcePaths.length; i++)
			{
				additionalPaths += additionalPathTemplate.replace("%s", o.SourcePaths[i]);
			}
			
		APP_SCOPE.appendHtml($("#edit-window-additional-paths-container")[0], additionalPaths);

		//Clean up 
		$("#edit-window-filters-container").empty();

		var filterSetTemplate = "<div class=\"edit-window-additional-filter\">%s <div class=\"edit-window-remove-filter-button\"></div></div>";
		
		var filterSets = "";
		if (o.FilterSets != null)
			for(var i = 0; i < o.FilterSets.length; i++)
			{
				filterSets += additionalPathTemplate.replace("%s", o.FilterSets[i].Name);
			}

		APP_SCOPE.appendHtml($("#edit-window-filters-container")[0], filterSets);
	
	}

	APP_SCOPE.loadsNeeded = 0;
	APP_SCOPE.afterElementLoaded = function()
	{
		APP_SCOPE.loadsNeeded--;
		if (APP_SCOPE.loadsNeeded <= 0)
		{
			$('#modal-cover').hide();
			$('#progress-loader').hide();
			APP_SCOPE.updateUI(window.editObj);
		}
	}

}());