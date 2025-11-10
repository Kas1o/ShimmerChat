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