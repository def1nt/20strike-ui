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

// ---------------------------------------------------------------------------
// Location map (Pages/Map/LocationMap.razor): a pannable/zoomable viewport with
// draggable markers. The layer (image + markers) lives at 0..0 inside the fixed
// viewport and is moved via translate()/scale(). Percentages are always relative
// to the untransformed layer, so they survive any pan/zoom.
// ---------------------------------------------------------------------------

var mapState = null;

function mapInit(viewportId, dotNetRef) {
    mapDispose();

    var viewport = document.getElementById(viewportId);
    if (!viewport) {
        return;
    }

    var layer = viewport.querySelector('.map-layer');
    if (!layer) {
        return;
    }

    var state = {
        viewport: viewport,
        layer: layer,
        img: layer.querySelector('img'),
        dotNetRef: dotNetRef,
        tx: 0, ty: 0, scale: 1,
        minScale: 0.15, maxScale: 8,
        fitted: false,
        dragging: false,
        dragMode: null, // 'pan' | 'marker'
        pointerId: null,
        startClientX: 0, startClientY: 0,
        startTx: 0, startTy: 0,
        markerEl: null,
        markerName: null,
        markerStartX: 0, markerStartY: 0,
        moved: false,
    };
    mapState = state;

    state.pointerDownHandler = function (e) {
        if (e.pointerType === 'mouse' && e.button !== 0) {
            return;
        }
        var marker = e.target.closest('.map-marker');
        state.dragging = true;
        state.moved = false;
        state.pointerId = e.pointerId;
        state.startClientX = e.clientX;
        state.startClientY = e.clientY;
        state.markerEl = null;
        viewport.setPointerCapture(e.pointerId);
        if (marker) {
            e.preventDefault();
            state.dragMode = 'marker';
            state.markerEl = marker;
            state.markerName = marker.getAttribute('data-name');
            var layerRect = layer.getBoundingClientRect();
            state.markerStartX = (e.clientX - layerRect.left) / layerRect.width * 100;
            state.markerStartY = (e.clientY - layerRect.top) / layerRect.height * 100;
        }
        else {
            state.dragMode = 'pan';
            state.startTx = state.tx;
            state.startTy = state.ty;
            viewport.style.cursor = 'grabbing';
        }
    };

    state.pointerMoveHandler = function (e) {
        if (!state.dragging || e.pointerId !== state.pointerId) {
            return;
        }
        var dx = e.clientX - state.startClientX;
        var dy = e.clientY - state.startClientY;
        if (Math.abs(dx) + Math.abs(dy) > 4) {
            state.moved = true;
        }
        if (state.dragMode === 'pan') {
            state.tx = state.startTx + dx;
            state.ty = state.startTy + dy;
            clampTranslate(state);
            applyTransform(state);
        }
        else if (state.dragMode === 'marker' && state.markerEl) {
            var r = layer.getBoundingClientRect();
            var x = clampPct(state.markerStartX + dx / r.width * 100);
            var y = clampPct(state.markerStartY + dy / r.height * 100);
            state.markerEl.style.left = x + '%';
            state.markerEl.style.top = y + '%';
        }
    };

    state.pointerEndHandler = function (e) {
        if (!state.dragging || e.pointerId !== state.pointerId) {
            return;
        }
        state.dragging = false;
        viewport.style.cursor = '';
        var wasMove = state.moved;
        var mode = state.dragMode;
        var markerName = state.markerName;
        var isCancel = e.type === 'pointercancel';

        if (mode === 'marker' && wasMove && !isCancel && markerName) {
            var r = layer.getBoundingClientRect();
            var xPct = clampPct((e.clientX - r.left) / r.width * 100);
            var yPct = clampPct((e.clientY - r.top) / r.height * 100);
            dotNetRef.invokeMethodAsync('OnMarkerDragEnd', markerName, xPct, yPct);
        }
        else if (!wasMove && !isCancel) {
            if (mode === 'marker' && markerName) {
                dotNetRef.invokeMethodAsync('OnMarkerClick', markerName);
            }
            else {
                var r2 = layer.getBoundingClientRect();
                dotNetRef.invokeMethodAsync(
                    'OnMapClick',
                    clampPct((e.clientX - r2.left) / r2.width * 100),
                    clampPct((e.clientY - r2.top) / r2.height * 100));
            }
        }

        state.dragMode = null;
        state.markerEl = null;
        state.markerName = null;
    };

    state.wheelHandler = function (e) {
        e.preventDefault();
        var rect = viewport.getBoundingClientRect();
        zoomAt(state, e.clientX - rect.left, e.clientY - rect.top, Math.pow(1.0015, -e.deltaY));
        applyTransform(state);
    };

    state.imgLoadHandler = function () {
        if (!state.fitted) {
            fitToViewport(state);
            applyTransform(state);
        }
    };

    // Escape undoes the last drag-saved marker movement (handled in .NET). Ignored while typing
    // in a form field so Escape keeps its usual meaning there (e.g. clearing the search box).
    state.keyDownHandler = function (e) {
        if (e.key !== 'Escape') {
            return;
        }
        var t = e.target;
        if (t && (t.tagName === 'INPUT' || t.tagName === 'TEXTAREA' || t.tagName === 'SELECT' || t.isContentEditable)) {
            return;
        }
        dotNetRef.invokeMethodAsync('OnMapEscape');
    };

    viewport.addEventListener('pointerdown', state.pointerDownHandler);
    viewport.addEventListener('pointermove', state.pointerMoveHandler);
    viewport.addEventListener('pointerup', state.pointerEndHandler);
    viewport.addEventListener('pointercancel', state.pointerEndHandler);
    viewport.addEventListener('wheel', state.wheelHandler, { passive: false });
    document.addEventListener('keydown', state.keyDownHandler);
    if (state.img) {
        state.img.addEventListener('load', state.imgLoadHandler);
        if (state.img.complete) {
            state.imgLoadHandler();
        }
    }

    fitToViewport(state);
    applyTransform(state);
}

function clampTranslate(state) {
    var layerW = state.layer.offsetWidth || 0;
    var layerH = state.layer.offsetHeight || 0;
    var vw = state.viewport.clientWidth;
    var vh = state.viewport.clientHeight;
    var scaledW = layerW * state.scale;
    var scaledH = layerH * state.scale;
    if (scaledW <= vw) {
        state.tx = (vw - scaledW) / 2;
    }
    else {
        state.tx = Math.min(0, Math.max(vw - scaledW, state.tx));
    }
    if (scaledH <= vh) {
        state.ty = (vh - scaledH) / 2;
    }
    else {
        state.ty = Math.min(0, Math.max(vh - scaledH, state.ty));
    }
    state.tx = Math.round(state.tx);
    state.ty = Math.round(state.ty);
    state.fitted = true;
}

function fitToViewport(state) {
    var layerW = state.layer.offsetWidth || 0;
    var layerH = state.layer.offsetHeight || 0;
    var vw = state.viewport.clientWidth;
    var vh = state.viewport.clientHeight;
    if (!layerW || !layerH || !vw || !vh) {
        return;
    }
    state.scale = Math.min(state.maxScale, Math.max(state.minScale, Math.min(vw / layerW, vh / layerH)));
    clampTranslate(state);
}

function zoomAt(state, cx, cy, factor) {
    var oldScale = state.scale || 1;
    var s = Math.min(state.maxScale, Math.max(state.minScale, oldScale * factor));
    var ix = (cx - state.tx) / oldScale;
    var iy = (cy - state.ty) / oldScale;
    state.tx = cx - ix * s;
    state.ty = cy - iy * s;
    state.scale = s;
    clampTranslate(state);
}

function applyTransform(state) {
    state.layer.style.transformOrigin = '0 0';
    state.layer.style.transform = 'translate(' + state.tx + 'px,' + state.ty + 'px) scale(' + state.scale + ')';
    // Counter-scale markers when zoomed out so they never get smaller than their base
    // (scale == 1) size on screen. Zooming in still enlarges them together with the map.
    var markerScale = state.scale < 1 ? 1 / state.scale : 1;
    state.layer.style.setProperty('--marker-scale', markerScale);
}

function clampPct(value) {
    return Math.min(100, Math.max(0, value));
}

function mapZoomBy(delta) {
    if (!mapState) {
        return;
    }
    var rect = mapState.viewport.getBoundingClientRect();
    zoomAt(mapState, rect.width / 2, rect.height / 2, Math.pow(1.3, delta));
    applyTransform(mapState);
}

function mapFit() {
    if (mapState) {
        fitToViewport(mapState);
        applyTransform(mapState);
    }
}

function mapCenterOn(xPct, yPct, animate) {
    if (!mapState) {
        return;
    }
    var state = mapState;
    var layerW = state.layer.offsetWidth || 0;
    var layerH = state.layer.offsetHeight || 0;
    var vw = state.viewport.clientWidth;
    var vh = state.viewport.clientHeight;
    if (!layerW || !layerH) {
        return;
    }
    // Zoom in a little beyond the fit level so the target is comfortably framed.
    state.scale = Math.max(state.scale, Math.min(state.maxScale, Math.min(vw / layerW, vh / layerH) * 1.6));
    state.tx = vw / 2 - (xPct / 100 * layerW) * state.scale;
    state.ty = vh / 2 - (yPct / 100 * layerH) * state.scale;
    clampTranslate(state);
    if (animate !== false) {
        state.layer.style.transition = 'transform 0.3s ease';
    }
    applyTransform(state);
    if (animate !== false) {
        window.setTimeout(function () { state.layer.style.transition = ''; }, 350);
    }
}

// Repositions a marker element from .NET (used to revert a drag when the save fails:
// Blazor diffs against its own last render, so it never undoes the style set by the drag).
function mapSetMarkerPosition(name, xPct, yPct) {
    if (!mapState) {
        return;
    }
    var markers = mapState.layer.querySelectorAll('.map-marker');
    for (var i = 0; i < markers.length; i++) {
        if (markers[i].getAttribute('data-name') === name) {
            markers[i].style.left = clampPct(xPct) + '%';
            markers[i].style.top = clampPct(yPct) + '%';
            return;
        }
    }
}

function mapDispose() {
    if (!mapState) {
        return;
    }
    var state = mapState;
    state.viewport.removeEventListener('pointerdown', state.pointerDownHandler);
    state.viewport.removeEventListener('pointermove', state.pointerMoveHandler);
    state.viewport.removeEventListener('pointerup', state.pointerEndHandler);
    state.viewport.removeEventListener('pointercancel', state.pointerEndHandler);
    state.viewport.removeEventListener('wheel', state.wheelHandler);
    document.removeEventListener('keydown', state.keyDownHandler);
    if (state.img) {
        state.img.removeEventListener('load', state.imgLoadHandler);
    }
    mapState = null;
}

function mapConfirm(message) {
    return window.confirm(message);
}

// Resolves when the map image is loaded (or immediately if it already is). Called before centering
// so the viewport math has real layer dimensions to work with.
function mapWaitForImage(viewportId) {
    var viewport = document.getElementById(viewportId);
    if (!viewport) {
        return Promise.resolve();
    }
    var img = viewport.querySelector('.map-layer img');
    if (!img || img.complete) {
        return Promise.resolve();
    }
    return new Promise(function (resolve) {
        img.addEventListener('load', function () { resolve(); });
        img.addEventListener('error', function () { resolve(); });
    });
}