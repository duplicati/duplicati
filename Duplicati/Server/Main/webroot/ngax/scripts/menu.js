$(document).ready(function() {
    $('html').on('click', function(e) {
        $('#mainmenu').removeClass('mobile-open');
        $('#threedotmenu_add_destination').removeClass('open');
        $('#threedotmenu_add_destination_adv').removeClass('open');
        $('#threedotmenu_add_source_folders').removeClass('open');
        $('#threedotmenu_add_source_filters').removeClass('open');
        $('#threedotmenu_add_options_adv').removeClass('open');
    });
    
    $('body').on('click', '.menubutton', function(e) {
        e.stopPropagation();
        e.preventDefault();
        $('#mainmenu').toggleClass('mobile-open');
    });

    $('body').on('click', '#threedotmenubutton_add_destination', function(e) {
        e.stopPropagation();
        e.preventDefault();
        $('#threedotmenu_add_destination').toggleClass('open');
    });

    $('body').on('click', '#threedotmenubutton_add_destination_adv', function(e) {
        e.stopPropagation();
        e.preventDefault();
        $('#threedotmenu_add_destination_adv').toggleClass('open');
    });

    $('body').on('click', '#threedotmenubutton_add_source_folders', function(e) {
        e.stopPropagation();
        e.preventDefault();
        $('#threedotmenu_add_source_folders').toggleClass('open');
    });

    $('body').on('click', '#threedotmenubutton_add_source_filters', function(e) {
        e.stopPropagation();
        e.preventDefault();
        $('#threedotmenu_add_source_filters').toggleClass('open');
    });

    $('body').on('click', '#threedotmenubutton_add_options_adv', function(e) {
        e.stopPropagation();
        e.preventDefault();
        $('#threedotmenu_add_options_adv').toggleClass('open');
    });

});
