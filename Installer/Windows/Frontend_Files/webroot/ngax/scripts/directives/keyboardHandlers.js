backupApp.directive('ngOnEnterPress', function () {
    return function (scope, element, attrs) {
        element.bind("keydown keypress search", function (event) {    
                if (event.keyCode == 13 || event.type == "search") {
                    scope.$apply(function (){
                        scope.$eval(attrs.ngOnEnterPress);
                    });
                    event.preventDefault();
                }
        });
    };
});

backupApp.directive('ngOnEscapePress', function () {
    return function (scope, element, attrs) {
        element.bind("keydown keypress reset", function (event) {    
                if (event.keyCode == 27) {
                    scope.$apply(function (){
                        scope.$eval(attrs.ngOnEscapePress);
                    });
                    event.preventDefault();
                }
        });
    };
});
