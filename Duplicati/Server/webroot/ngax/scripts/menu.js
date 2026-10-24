document.addEventListener('DOMContentLoaded', function() {
    // Each button opens and closes its menu, and a click anywhere else closes all of them
    var menus = [
        { button: '.menubutton', id: 'mainmenu', className: 'mobile-open' },
        { button: '#threedotmenubutton_add_destination', id: 'threedotmenu_add_destination', className: 'open' },
        { button: '#threedotmenubutton_add_destination_adv', id: 'threedotmenu_add_destination_adv', className: 'open' },
        { button: '#threedotmenubutton_add_source_folders', id: 'threedotmenu_add_source_folders', className: 'open' },
        { button: '#threedotmenubutton_add_source_filters', id: 'threedotmenu_add_source_filters', className: 'open' },
        { button: '#threedotmenubutton_add_options_adv', id: 'threedotmenu_add_options_adv', className: 'open' }
    ];

    document.documentElement.addEventListener('click', function(e) {
        menus.forEach(function(menu) {
            var element = document.getElementById(menu.id);
            if (element)
                element.classList.remove(menu.className);
        });
    });

    document.body.addEventListener('click', function(e) {
        menus.forEach(function(menu) {
            if (!e.target.closest(menu.button))
                return;

            e.stopPropagation();
            e.preventDefault();
            var element = document.getElementById(menu.id);
            if (element)
                element.classList.toggle(menu.className);
        });
    });
});
