window.copyTextToClipboard = (text) => {
    if (navigator.clipboard) {
        return navigator.clipboard.writeText(text);
    } else {
        // 兼容旧浏览器
        const textarea = document.createElement("textarea");
        textarea.value = text;
        document.body.appendChild(textarea);
        textarea.select();
        document.execCommand("copy");
        document.body.removeChild(textarea);
        return Promise.resolve();
    }
};

window.downloadFileFromStream = async (fileName, contentStreamReference) => {
    const arrayBuffer = await contentStreamReference.arrayBuffer();
    const blob = new Blob([arrayBuffer]);
    const url = URL.createObjectURL(blob);
    const anchorElement = document.createElement('a');
    anchorElement.href = url;
    anchorElement.download = fileName ?? '';
    anchorElement.click();
    anchorElement.remove();
    URL.revokeObjectURL(url);
}

// 通知功能实现
window.showNotification = async (title, options) => {
    // 检查浏览器是否支持通知
    if (!("Notification" in window)) {
        console.log("此浏览器不支持桌面通知");
        return Promise.resolve(false);
    }

    // 检查通知权限是否已授予
    if (Notification.permission !== "granted") {
        console.log("通知权限未被授予");
        return Promise.resolve(false);
    }

    // 检查页面是否可见
    const isPageVisible = document.visibilityState === 'visible';
    
    // 如果页面可见，则不显示通知
    if (isPageVisible) {
        console.log("页面可见，不显示通知");
        return Promise.resolve(false);
    }

    // 权限已授予，显示通知
    try {
        const notification = new Notification(title, options);
        notification.onclick = () => { window.focus(); };
        return Promise.resolve(true);
    } catch (error) {
        console.error("显示通知时出错:", error);
        return Promise.resolve(false);
    }
};

// 请求通知权限
window.requestNotificationPermission = async () => {
    if (!("Notification" in window)) {
        console.log("此浏览器不支持桌面通知");
        return Promise.resolve(false);
    }
    
    const permission = await Notification.requestPermission();
    return Promise.resolve(permission === "granted");
};

// ─── Chat auto-scroll ─────────────────────────────

// 聊天消息区的“自动跟随”状态由 JS 维护。
// 只有真实的用户滚动输入（滚轮 / 触摸 / 方向键）才会关闭自动跟随；
// 程序化滚动（自动滚到底部、点击回到底部按钮）和内容增长都不会。
// 否则平滑滚动产生的中间 scroll 事件会被当成用户上滑，把自动跟随自己关掉，
// 导致生成时卡在某个位置永远滚不到底部 —— 该问题在非 100% 缩放下被放大。
window.chatScroll = (function () {
    // 距底部不超过该像素数即视为“已回到底部”，并恢复自动跟随
    var BOTTOM_THRESHOLD = 50;

    var states = new WeakMap();

    function getState(element) {
        var state = states.get(element);
        if (state) return state;

        state = { following: true, userScrolled: false, scheduled: false };
        states.set(element, state);

        var markUserScrolled = function () {
            state.userScrolled = true;
            scheduleRecompute(element, state);
        };

        element.addEventListener('wheel', markUserScrolled, { passive: true });
        element.addEventListener('touchstart', markUserScrolled, { passive: true });
        element.addEventListener('touchmove', markUserScrolled, { passive: true });
        element.addEventListener('keydown', function (e) {
            if (e.key === 'ArrowUp' || e.key === 'ArrowDown' ||
                e.key === 'PageUp' || e.key === 'PageDown' ||
                e.key === 'Home' || e.key === 'End') {
                markUserScrolled();
            }
        });

        return state;
    }

    function atBottom(element) {
        return element.scrollHeight - element.scrollTop - element.clientHeight <= BOTTOM_THRESHOLD;
    }

    function recompute(element, state) {
        if (atBottom(element)) {
            state.following = true;
            state.userScrolled = false;
        } else if (state.userScrolled) {
            state.following = false;
        }
    }

    function scheduleRecompute(element, state) {
        if (state.scheduled) return;
        state.scheduled = true;
        requestAnimationFrame(function () {
            state.scheduled = false;
            recompute(element, state);
        });
    }

    return {
        // 当前是否应保持自动跟随
        isFollowing: function (element) {
            var state = getState(element);
            recompute(element, state);
            return state.following;
        },
        // smooth=true 用于用户主动点击「回到底部」；自动跟随应使用即时滚动
        scrollToBottom: function (element, smooth) {
            var state = getState(element);
            state.following = true;
            state.userScrolled = false;

            if (smooth) {
                element.scrollTo({ top: element.scrollHeight, behavior: 'smooth' });
                return;
            }

            // 即时滚动，并在后续帧重新校正，以处理内容在滚动目标计算完成后
            // 继续增长，以及非整数缩放下的最大滚动偏移偏差。
            var pin = function () { element.scrollTop = element.scrollHeight; };
            pin();
            requestAnimationFrame(function () {
                pin();
                requestAnimationFrame(pin);
            });
        }
    };
})();

// 滚动到底部。smooth=true 时使用平滑滚动（用户主动回到底部）；
// 自动跟随生成应省略 smooth，使用即时滚动。
window.scrollToBottom = function (element, smooth) {
    if (!element) {
        console.error('scrollToBottom: element is null');
        return;
    }
    try {
        window.chatScroll.scrollToBottom(element, smooth === true);
    } catch (error) {
        console.error('Error scrolling to bottom:', error);
    }
};

// 聊天区是否应保持自动跟随（由 chatScroll 维护，区分用户滚动与程序化滚动）
window.isChatAutoFollowing = function (element) {
    try {
        if (!element) return true;
        return window.chatScroll.isFollowing(element);
    } catch (error) {
        console.error('Error checking scroll state:', error);
        return true;
    }
};

// 检查元素是否滚动到底部（允许一定误差）
window.isScrolledToBottom = function (element, threshold) {
    try {
        if (!element) return true;
        var diff = element.scrollHeight - element.scrollTop - element.clientHeight;
        return diff <= threshold;
    } catch (error) {
        console.error('Error checking scroll position:', error);
        return true;
    }
};

// 检测屏幕方向
window.getIsPortrait = function () {
    return window.innerHeight > window.innerWidth;
};

// 监听屏幕方向变化
window.listenOrientation = function (dotNetRef) {
    var handler = function () {
        dotNetRef.invokeMethodAsync('OnOrientationChanged', window.innerHeight > window.innerWidth);
    };
    window.addEventListener('resize', handler);
    window.__orientationHandler = handler;
};

// 移除屏幕方向监听
window.unlistenOrientation = function () {
    if (window.__orientationHandler) {
        window.removeEventListener('resize', window.__orientationHandler);
        window.__orientationHandler = null;
    }
};

// 检查元素是否滚动到顶部（允许一定误差）
window.isScrolledToTop = function (element, threshold) {
    try {
        if (!element) return true;
        return element.scrollTop <= threshold;
    } catch (error) {
        console.error('Error checking scroll position:', error);
        return true;
    }
};

// textarea 自适应高度
window.autoResizeTextarea = function (el) {
    if (!el) return;
    el.style.height = 'auto';
    el.style.height = Math.min(el.scrollHeight, 200) + 'px';
};

// ─── Node Picker scroll helpers ──────────────────

/**
 * Scrolls the .tn-add-item-active element inside the container into view.
 */
window.scrollNodePickerActiveIntoView = function (container) {
    if (!container) return;
    var active = container.querySelector('.tn-add-item-active');
    if (active) {
        active.scrollIntoView({ block: 'nearest' });
    }
};

// ─── Tree Editor Drag & Drop Helpers ──────────────────

/**
 * Returns the ratio (0..1) of the pointer's Y position within the element.
 * Used by TreeEditor/GenericNodeEditor to determine whether to insert
 * before (< 0.5) or after (>= 0.5) the target node.
 */
window.getDropRatio = function (element, clientY) {
    if (!element) return 0;
    var rect = element.getBoundingClientRect();
    return (clientY - rect.top) / rect.height;
};

/**
 * Global dragstart listener for tree nodes.
 * Firefox requires setData to be called synchronously in the dragstart event
 * for the drag operation to initiate. Blazor Server's async dispatch happens
 * too late, so we set minimal data here and let the .NET handler manage state.
 */
document.addEventListener('dragstart', function (e) {
    var header = e.target.closest('.tn-header');
    if (header && header.hasAttribute('draggable')) {
        e.dataTransfer.setData('text/plain', 'tree-node');
        e.dataTransfer.effectAllowed = 'move';
    }
});