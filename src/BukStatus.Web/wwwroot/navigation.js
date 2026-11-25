const menus = () => document.querySelectorAll('.hero-menu details');
function closeMenus(focus = false) {
    for (const menu of menus()) {
        if (!menu.open) continue;
        menu.open = false;
        if (focus) menu.querySelector('summary')?.focus();
    }
}
document.addEventListener('click', event => {
    if (!(event.target instanceof Element)) return;
    if (!event.target.closest('.hero-menu') || event.target.closest('.hero-menu a')) closeMenus();
});
document.addEventListener('keydown', event => {
    if (event.key === 'Escape' && document.querySelector('.hero-menu details[open]')) {
        closeMenus(true);
        event.preventDefault();
    }
});
document.addEventListener('toggle', event => {
    if (!(event.target instanceof HTMLDetailsElement) || !event.target.matches('.hero-menu details[open]')) return;
    for (const menu of menus()) if (menu !== event.target) menu.open = false;
}, true);
