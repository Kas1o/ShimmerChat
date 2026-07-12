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

window.scrollToBottom = function (element) {
    try {
        if (element) {
            // 使用scrollTo方法滚动到底部
            element.scrollTo({
                top: element.scrollHeight,
                behavior: 'smooth'
            });
        } else {
            console.error('Element is null');
        }
    } catch (error) {
        console.error('Error scrolling to bottom:', error);
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