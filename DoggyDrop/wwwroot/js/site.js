// Zapri navbar na mobilniku po kliku na katerikoli link
document.querySelectorAll('.navbar-nav>li>a').forEach(link => {
    link.addEventListener('click', () => {
        const navbarCollapse = document.querySelector('.navbar-collapse');
        if (navbarCollapse.classList.contains('show')) {
            new bootstrap.Collapse(navbarCollapse).toggle();
        }
    });
});
