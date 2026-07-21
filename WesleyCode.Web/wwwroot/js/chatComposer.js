const handlers = new WeakMap();
const autoScrollStates = new WeakMap();

export function registerSubmitOnEnter(element, dotNetHelper) {
    if (!element) {
        return;
    }

    const handler = async (event) => {
        if (event.key !== 'Enter' || event.shiftKey || event.isComposing) {
            return;
        }

        event.preventDefault();
        await dotNetHelper.invokeMethodAsync('SubmitFromKeyboardAsync');
    };

    handlers.set(element, handler);
    element.addEventListener('keydown', handler);
}

export function disposeSubmitOnEnter(element) {
    const handler = handlers.get(element);
    if (!handler) {
        return;
    }

    element.removeEventListener('keydown', handler);
    handlers.delete(element);
}

export function registerAutoScroll(anchor, composerShell) {
    if (!anchor) {
        return;
    }

    const state = {
        composerShell,
        shouldStick: true,
        update: () => {
            state.shouldStick = isNearBottom(anchor, state.composerShell);
        }
    };

    state.update();
    window.addEventListener('scroll', state.update, { passive: true });
    window.addEventListener('resize', state.update);
    autoScrollStates.set(anchor, state);
}

export function disposeAutoScroll(anchor) {
    const state = autoScrollStates.get(anchor);
    if (!state) {
        return;
    }

    window.removeEventListener('scroll', state.update);
    window.removeEventListener('resize', state.update);
    autoScrollStates.delete(anchor);
}

export function scrollToBottom(anchor, composerShell, force = false) {
    if (!anchor) {
        return;
    }

    const state = autoScrollStates.get(anchor);
    if (state) {
        state.composerShell = composerShell;
        if (!force && !state.shouldStick) {
            return;
        }
    }

    const prefersReducedMotion = window.matchMedia('(prefers-reduced-motion: reduce)').matches;
    scrollViewportToTarget(anchor, composerShell, force || prefersReducedMotion ? 'auto' : 'smooth');

    if (state) {
        requestAnimationFrame(() => {
            scrollViewportToTarget(anchor, composerShell, 'auto');
            state.update();
        });
        setTimeout(() => {
            scrollViewportToTarget(anchor, composerShell, 'auto');
            state.update();
        }, 120);
    }
}

function isNearBottom(anchor, composerShell) {
    const targetTop = getTargetScrollTop(anchor, composerShell);
    return Math.abs(getScrollTop() - targetTop) <= 48;
}

function getTargetScrollTop(anchor, composerShell) {
    const composerHeight = composerShell?.getBoundingClientRect().height ?? 0;
    const anchorTop = anchor.getBoundingClientRect().top + getScrollTop();
    const viewportHeight = window.innerHeight;
    const padding = 24;
    return Math.max(0, anchorTop - viewportHeight + composerHeight + padding);
}

function scrollViewportToTarget(anchor, composerShell, behavior) {
    const scrollingElement = document.scrollingElement;
    if (!scrollingElement) {
        return;
    }

    const targetTop = getTargetScrollTop(anchor, composerShell);
    scrollingElement.scrollTo({ top: targetTop, behavior });
}

function getScrollTop() {
    return document.scrollingElement?.scrollTop ?? window.scrollY ?? 0;
}
