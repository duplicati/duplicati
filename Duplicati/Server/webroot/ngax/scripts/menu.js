$(document).ready(function() {
    $('html').on('click', function(e) {
        $('#mainmenu').removeClass('mobile-open');
    });
    
    $('body').on('click', '.menubutton', function(e) {
        e.stopPropagation();
        e.preventDefault();
        $('#mainmenu').toggleClass('mobile-open');
    });
});