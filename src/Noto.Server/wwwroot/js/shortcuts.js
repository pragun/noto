window.noto = window.noto || {};

noto.registerSearchShortcut = function(dotnetRef) {
    document.addEventListener('keydown', function(e) {
        if ((e.metaKey || e.ctrlKey) && e.key === 'k') {
            e.preventDefault();
            dotnetRef.invokeMethodAsync('OnSearchShortcut');
        }
    });
};
