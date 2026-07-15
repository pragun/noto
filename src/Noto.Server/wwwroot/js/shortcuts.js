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

// Toggle a class on document.body. Used by pages that need page-scoped layout.
noto.setBodyClass = function(name, on) {
    if (on) document.body.classList.add(name);
    else document.body.classList.remove(name);
};

// Go back if there's session history, otherwise navigate to fallback.
// history.length > 1 means we have a prior entry in this tab.
noto.backOr = function(fallback) {
    if (window.history.length > 1) {
        window.history.back();
    } else {
        window.location.href = fallback;
    }
};
