/**
 * Redirect Merlin / Windows CE browsers to the handheld deploy hub.
 * Opt out: ?desktop=1 on any page URL.
 */
(function (global) {
    function shouldRedirectToHandheld() {
        if (/[?&]desktop=1(?:&|$)/i.test(global.location.search)) return false;
        var ua = String(global.navigator.userAgent || '');
        var isCe =
            /Windows CE|WindowsCE|WinCE|IEMobile|HTE00072|Merlin|Nordic/i.test(ua) ||
            /Windows NT 5\.1/.test(ua) && /ARM/i.test(ua);
        var narrow =
            (global.screen && global.screen.width > 0 && global.screen.width <= 520) ||
            (global.innerWidth > 0 && global.innerWidth <= 520);
        return isCe || (narrow && /Mobile|CE|Merlin/i.test(ua));
    }

    function handheldEntryUrl() {
        return '/deploy/';
    }

    function maybeRedirect() {
        if (!shouldRedirectToHandheld()) return false;
        var dest = handheldEntryUrl();
        if (global.location.pathname === dest || global.location.pathname.indexOf('/deploy/') === 0) {
            return false;
        }
        global.location.replace(dest);
        return true;
    }

    global.MerlinHandheldDetect = {
        shouldRedirectToHandheld: shouldRedirectToHandheld,
        handheldEntryUrl: handheldEntryUrl,
        maybeRedirect: maybeRedirect,
    };

    maybeRedirect();
})(typeof window !== 'undefined' ? window : global);
