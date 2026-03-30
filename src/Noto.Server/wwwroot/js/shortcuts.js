window.noto = window.noto || {};

noto.registerSearchShortcut = function(dotnetRef) {
    document.addEventListener('keydown', function(e) {
        if ((e.metaKey || e.ctrlKey) && e.key === 'k') {
            e.preventDefault();
            dotnetRef.invokeMethodAsync('OnSearchShortcut');
        }
    });
};

noto.registerShiftWatch = function(dotnetRef) {
    document.addEventListener('keydown', function(e) {
        if (e.key === 'Shift') dotnetRef.invokeMethodAsync('OnShiftChanged', true);
    });
    document.addEventListener('keyup', function(e) {
        if (e.key === 'Shift') dotnetRef.invokeMethodAsync('OnShiftChanged', false);
    });
    window.addEventListener('blur', function() {
        dotnetRef.invokeMethodAsync('OnShiftChanged', false);
    });
};
