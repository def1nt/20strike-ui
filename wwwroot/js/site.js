// Page-level JS helpers for the ComputersV2 UI (scroll-to-top button, IP copy).

function cv2ScrollToTop() {
    window.scrollTo({ top: 0, behavior: 'smooth' });
}

function cv2InitScrollTracking(dotNetRef, threshold) {
    var handler = function () {
        var scrolled = (window.scrollY || document.documentElement.scrollTop) > threshold;
        dotNetRef.invokeMethodAsync('OnWindowScroll', scrolled);
    };
    window.addEventListener('scroll', handler, { passive: true });
    window.__cv2ScrollHandler = handler;
    handler();
}

function cv2DisposeScrollTracking() {
    if (window.__cv2ScrollHandler) {
        window.removeEventListener('scroll', window.__cv2ScrollHandler);
        window.__cv2ScrollHandler = null;
    }
}

function copyText(text) {
    if (navigator.clipboard && navigator.clipboard.writeText) {
        return navigator.clipboard.writeText(text);
    }
    var ta = document.createElement('textarea');
    ta.value = text;
    ta.style.position = 'fixed';
    ta.style.opacity = '0';
    document.body.appendChild(ta);
    ta.select();
    try { document.execCommand('copy'); } catch (e) { }
    document.body.removeChild(ta);
    return Promise.resolve();
}