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