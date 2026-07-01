const handlers = new WeakMap();

export function registerSidebarResize(handle, dotNetHelper, minWidth, maxWidth) {
    if (!handle || !dotNetHelper) {
        return;
    }

    disposeSidebarResize(handle);

    const onPointerDown = (event) => {
        if (window.innerWidth <= 900) {
            return;
        }

        event.preventDefault();
        if (!handle.parentElement) {
            return;
        }

        const pointerId = event.pointerId;
        handle.setPointerCapture(pointerId);

        const onPointerMove = (moveEvent) => {
            const width = Math.min(Math.max(moveEvent.clientX, minWidth), maxWidth);
            dotNetHelper.invokeMethodAsync("UpdateSidebarWidthAsync", width);
        };

        const onPointerUp = () => {
            handle.releasePointerCapture(pointerId);
            handle.removeEventListener("pointermove", onPointerMove);
            handle.removeEventListener("pointerup", onPointerUp);
            handle.removeEventListener("pointercancel", onPointerUp);
        };

        handle.addEventListener("pointermove", onPointerMove);
        handle.addEventListener("pointerup", onPointerUp);
        handle.addEventListener("pointercancel", onPointerUp);
    };

    handle.addEventListener("pointerdown", onPointerDown);
    handlers.set(handle, onPointerDown);
}

export function disposeSidebarResize(handle) {
    const onPointerDown = handlers.get(handle);
    if (!handle || !onPointerDown) {
        return;
    }

    handle.removeEventListener("pointerdown", onPointerDown);
    handlers.delete(handle);
}
