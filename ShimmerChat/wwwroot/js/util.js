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

// 聊天消息区的“自动跟随”状态由 JS 维护，规则：
//   * 每次 scroll 后按“是否在底部附近”更新跟随状态。这样滚轮、触摸、
//     拖动滚动条、键盘都能正确生效。
//   * 自动跟随的滚动只在仍处于跟随状态时执行，绝不会把用户的上滑拉回底部，
//     否则用户会“难以离开跟随状态”。
//   * 用户主动点击「回到底部」使用平滑滚动；平滑动画自身的中间 scroll 事件
//     通过 animating 标记忽略，避免被误判为上滑而关闭跟随。
window.chatScroll = (function () {
    // 距底部不超过该像素数即视为“已回到底部”，并恢复自动跟随
    var BOTTOM_THRESHOLD = 50;

    // 平滑滚动的最长等待时间，超过后即使没到底也结束动画标记
    var MANUAL_ANIMATION_TIMEOUT_MS = 800;

    var states = new WeakMap();

    function atBottom(element) {
        return element.scrollHeight - element.scrollTop - element.clientHeight <= BOTTOM_THRESHOLD;
    }

    function getState(element) {
        var state = states.get(element);
        if (state) return state;

        state = { following: true, animating: false };
        states.set(element, state);

        // 用户接管滚动时立即取消「回到底部」的平滑动画，
        // 让随后的 scroll 事件按真实位置决定是否跟随。
        var cancelManualAnimation = function () { state.animating = false; };
        element.addEventListener('wheel', cancelManualAnimation, { passive: true });
        element.addEventListener('touchstart', cancelManualAnimation, { passive: true });
        element.addEventListener('touchmove', cancelManualAnimation, { passive: true });

        element.addEventListener('scroll', function () {
            if (state.animating) return;
            state.following = atBottom(element);
        }, { passive: true });

        return state;
    }

    return {
        // 当前是否应保持自动跟随
        isFollowing: function (element) {
            var state = getState(element);
            if (!state.animating) {
                state.following = atBottom(element);
            }
            return state.following;
        },

        // 自动跟随：仅在仍处于跟随状态时滚到底部。
        // 即时滚动，并在后续帧重新校正，以处理内容在滚动目标计算完成后
        // 继续增长，以及非整数缩放下的最大滚动偏移偏差。
        scrollToBottom: function (element) {
            var state = getState(element);
            if (!state.following) return;

            var pin = function () { element.scrollTop = element.scrollHeight; };
            pin();
            requestAnimationFrame(function () {
                pin();
                requestAnimationFrame(pin);
            });
        },

        // 用户主动回到底部：无论当前状态如何都恢复跟随并使用平滑滚动
        scrollToBottomManual: function (element) {
            var state = getState(element);
            state.following = true;
            state.animating = true;
            element.scrollTo({ top: element.scrollHeight, behavior: 'smooth' });

            var deadline = Date.now() + MANUAL_ANIMATION_TIMEOUT_MS;
            var settle = function () {
                if (atBottom(element) || Date.now() > deadline) {
                    state.animating = false;
                    state.following = true;
                } else {
                    requestAnimationFrame(settle);
                }
            };
            requestAnimationFrame(settle);
        }
    };
})();

// 自动跟随滚到底部（用户在别处时不会强制拉回）
window.scrollToBottom = function (element) {
    if (!element) {
        console.error('scrollToBottom: element is null');
        return;
    }
    try {
        window.chatScroll.scrollToBottom(element);
    } catch (error) {
        console.error('Error scrolling to bottom:', error);
    }
};

// 用户主动点击「回到底部」
window.scrollToBottomManual = function (element) {
    if (!element) {
        console.error('scrollToBottomManual: element is null');
        return;
    }
    try {
        window.chatScroll.scrollToBottomManual(element);
    } catch (error) {
        console.error('Error scrolling to bottom:', error);
    }
};

// 聊天区是否应保持自动跟随（由 chatScroll 维护）
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

// ─── Chat image attachments: drop & paste ─────────

// 聊天输入区接受「拖入图像」与「粘贴图像」：两者都先把文件写回隐藏的
// <input type="file">（Blazor 的 InputFile），再派发一次原生 change 事件，
// 于是它们与「点击选择文件」共用同一条上传流程 —— 格式 / 大小 / 数量校验、
// 落盘与错误提示都只有一份实现。
window.chatImageDrop = (function () {
    // 拖动文件进入聊天区时加在拖放区上的类名，由 CSS 显示投放提示层
    var ACTIVE_CLASS = 'chat-drop-active';

    // 注册表：key → { zone, handlers }。key 由 .NET 侧生成并持有，
    // 注销时只传 key，不依赖元素引用（组件释放时 DOM 可能已经被移除）。
    var registrations = new Map();

    var guard = null;

    function isImage(type) {
        return typeof type === 'string' && type.lastIndexOf('image/', 0) === 0;
    }

    // 拖动内容里是否含文件。页面内的文本 / 节点拖拽是 text/plain，不受影响。
    function hasFileDrag(dataTransfer) {
        if (!dataTransfer || !dataTransfer.types) return false;
        for (var i = 0; i < dataTransfer.types.length; i++) {
            if (dataTransfer.types[i] === 'Files') return true;
        }
        return false;
    }

    function contains(zone, node) {
        return !!node && zone.contains(node);
    }

    // 把文件交给 InputFile 的 <input>：文件列表由本模块自己的 DataTransfer 构造，
    // 不使用事件的拖拽数据（后者在事件返回后可能失效）。
    // 生成过程中该 input 处于 disabled，这里仍然写入并派发 change：
    // 「生成中不能添加图像」由 .NET 侧明确提示，而不是把用户拖入的图像悄悄丢掉。
    function feed(inputHost, files) {
        if (files.length === 0) return;

        var input = inputHost ? inputHost.querySelector('input[type=file]') : null;
        if (!input) {
            console.error('[chatImageDrop] The chat shell has no <input type="file"> to receive the images.');
            return;
        }

        var transfer = new DataTransfer();
        for (var i = 0; i < files.length; i++) transfer.items.add(files[i]);

        try {
            input.files = transfer.files;
        } catch (error) {
            console.error('[chatImageDrop] The browser refused the dropped/pasted file list.', error);
            return;
        }

        if (!input.files || input.files.length === 0) {
            // 赋值被静默忽略：明确报错，绝不假装图像已经进入上传流程
            console.error('[chatImageDrop] The browser ignored the dropped/pasted file list.');
            return;
        }

        input.dispatchEvent(new Event('change', { bubbles: true }));
    }

    // 剪贴板中的图像文件。只有剪贴板里确实存在图像时本模块才接管，
    // 普通文本粘贴完全不受影响。
    //
    // 剪贴板同时带文本与图像时（例如从网页复制）以图像为准：否则会把图像的
    // 文件名 / URL 一起粘进输入框。非图像的文件不会被粘贴进来，因为纯文本
    // 输入框本来就没有它们的粘贴表示。
    function clipboardImages(clipboard) {
        var files = [];
        if (!clipboard) return files;

        if (clipboard.files) {
            for (var i = 0; i < clipboard.files.length; i++) {
                if (isImage(clipboard.files[i].type)) files.push(clipboard.files[i]);
            }
        }

        if (files.length === 0 && clipboard.items) {
            for (var j = 0; j < clipboard.items.length; j++) {
                var item = clipboard.items[j];
                if (item.kind !== 'file' || !isImage(item.type)) continue;
                var file = item.getAsFile();
                if (file) files.push(file);
            }
        }

        return files;
    }

    // 文件被拖到聊天区之外时阻止浏览器直接打开它（那会把整个应用页面替换掉）。
    // 只在拖动内容确实含文件时拦截，页面内的文本 / 节点拖拽不受影响；
    // 没有任何聊天界面时卸载拦截，其它页面的行为保持原样。
    function installGuard() {
        if (guard) return;

        var onDragOver = function (e) {
            if (hasFileDrag(e.dataTransfer)) e.preventDefault();
        };
        var onDrop = function (e) {
            if (hasFileDrag(e.dataTransfer)) e.preventDefault();
        };

        document.addEventListener('dragover', onDragOver);
        document.addEventListener('drop', onDrop);
        guard = { onDragOver: onDragOver, onDrop: onDrop };
    }

    function uninstallGuard() {
        if (!guard) return;

        document.removeEventListener('dragover', guard.onDragOver);
        document.removeEventListener('drop', guard.onDrop);
        guard = null;
    }

    function register(key, zone, inputHost) {
        var handlers = {};

        handlers.dragenter = function (e) {
            if (!hasFileDrag(e.dataTransfer)) return;
            zone.classList.add(ACTIVE_CLASS);
        };

        handlers.dragover = function (e) {
            if (!hasFileDrag(e.dataTransfer)) return;
            // 必须阻止默认行为，否则浏览器不会把拖放区当作可投放目标
            e.preventDefault();
            if (e.dataTransfer) e.dataTransfer.dropEffect = 'copy';
            // 幂等：拖动过程中若被误判为「已离开」，这里立即恢复提示
            zone.classList.add(ACTIVE_CLASS);
        };

        handlers.dragleave = function (e) {
            // 进入子元素同样会触发 dragleave，此时 relatedTarget 仍在拖放区内
            if (contains(zone, e.relatedTarget)) return;
            zone.classList.remove(ACTIVE_CLASS);
        };

        handlers.drop = function (e) {
            if (!hasFileDrag(e.dataTransfer)) return;
            e.preventDefault();
            zone.classList.remove(ACTIVE_CLASS);

            // 拖入的文件全部交给 .NET 侧校验：不支持的格式会得到明确提示，
            // 而不是在这里被静默过滤掉。
            var files = e.dataTransfer && e.dataTransfer.files
                ? Array.prototype.slice.call(e.dataTransfer.files)
                : [];
            feed(inputHost, files);
        };

        // 粘贴监听挂在拖放区上：输入框聚焦时粘贴事件由它冒泡上来，
        // 焦点在消息区等其它位置时也能直接粘贴图像。
        handlers.paste = function (e) {
            var files = clipboardImages(e.clipboardData);
            if (files.length === 0) return;
            e.preventDefault();
            feed(inputHost, files);
        };

        zone.addEventListener('dragenter', handlers.dragenter);
        zone.addEventListener('dragover', handlers.dragover);
        zone.addEventListener('dragleave', handlers.dragleave);
        zone.addEventListener('drop', handlers.drop);
        zone.addEventListener('paste', handlers.paste);

        registrations.set(key, { zone: zone, handlers: handlers });
        installGuard();
    }

    function unregister(key) {
        var registration = registrations.get(key);
        if (!registration) return;

        var zone = registration.zone;
        var handlers = registration.handlers;
        zone.removeEventListener('dragenter', handlers.dragenter);
        zone.removeEventListener('dragover', handlers.dragover);
        zone.removeEventListener('dragleave', handlers.dragleave);
        zone.removeEventListener('drop', handlers.drop);
        zone.removeEventListener('paste', handlers.paste);
        zone.classList.remove(ACTIVE_CLASS);

        registrations.delete(key);
        if (registrations.size === 0) uninstallGuard();
    }

    return {
        // key：调用方持有的标识；zone：拖放区根元素；inputHost：含 <InputFile> 的元素
        attach: function (key, zone, inputHost) {
            if (!key || !zone) {
                console.error('[chatImageDrop] attach requires a key and a drop zone.');
                return;
            }
            if (registrations.has(key)) return;
            register(key, zone, inputHost);
        },

        detach: function (key) {
            unregister(key);
        }
    };
})();

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