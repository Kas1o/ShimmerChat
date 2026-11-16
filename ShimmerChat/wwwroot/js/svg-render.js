// SVG 渲染卡片的复制 / 下载。
//
// 和代码块工具条一样使用 document 级事件委托：消息 HTML 由 Blazor 的 MarkupString
// 注入，流式生成时 DOM 会被反复替换，逐个绑定迟早失效。

(function () {
    if (window.svgRenderActions) return;

    const COPY_ICON = 'bi bi-clipboard';
    const COPIED_ICON = 'bi bi-check2';
    const FLASH_MS = 1500;

    function findSource(button) {
        const figure = button.closest('.svg-render');
        if (!figure) return null;
        const template = figure.querySelector('template.svg-render-source');
        return template ? template.content.textContent : null;
    }

    // 下载用图片里已经规范化过的 SVG：补过默认命名空间、编码声明为 UTF-8，
    // 下载后可以直接在浏览器 / 矢量软件里打开。复制仍然保留原始源码。
    function findDownloadSource(button) {
        const figure = button.closest('.svg-render');
        const image = figure ? figure.querySelector('img.svg-render-image') : null;
        const src = image ? image.getAttribute('src') || '' : '';
        const marker = 'data:image/svg+xml;base64,';

        if (src.startsWith(marker)) {
            try {
                const binary = atob(src.slice(marker.length));
                const bytes = Uint8Array.from(binary, function (char) {
                    return char.charCodeAt(0);
                });
                return new TextDecoder('utf-8').decode(bytes);
            } catch (error) {
                console.error('[svg-render] failed to decode image data', error);
            }
        }

        return findSource(button);
    }

    // 局域网 http 下 navigator.clipboard 不可用（非安全上下文），退回 execCommand
    function fallbackCopy(text) {
        const area = document.createElement('textarea');
        area.value = text;
        area.setAttribute('readonly', '');
        area.style.position = 'fixed';
        area.style.top = '-1000px';
        area.style.opacity = '0';
        document.body.appendChild(area);
        area.select();
        area.setSelectionRange(0, area.value.length);

        let ok = false;
        try {
            ok = document.execCommand('copy');
        } finally {
            document.body.removeChild(area);
        }

        return ok;
    }

    function copyText(text) {
        if (navigator.clipboard && window.isSecureContext) {
            return navigator.clipboard.writeText(text).catch(function () {
                if (!fallbackCopy(text)) throw new Error('clipboard write failed');
            });
        }

        if (fallbackCopy(text)) return Promise.resolve();
        return Promise.reject(new Error('clipboard unavailable'));
    }

    function flashCopied(button) {
        const label = button.querySelector('.svg-render-btn-text');
        const icon = button.querySelector('i');

        if (label && !button.dataset.originalLabel) {
            button.dataset.originalLabel = label.textContent;
        }

        const copiedLabel = button.getAttribute('data-svg-copied')
            || button.dataset.originalLabel
            || '';
        button.classList.add('is-copied');
        if (label) label.textContent = copiedLabel;
        if (icon) icon.className = COPIED_ICON;

        window.setTimeout(function () {
            button.classList.remove('is-copied');
            if (label && button.dataset.originalLabel) label.textContent = button.dataset.originalLabel;
            if (icon) icon.className = COPY_ICON;
        }, FLASH_MS);
    }

    function download(button, text) {
        const fileName = button.getAttribute('data-svg-filename') || 'snippet.svg';
        const blob = new Blob([text], { type: 'image/svg+xml;charset=utf-8' });
        const url = URL.createObjectURL(blob);

        const anchor = document.createElement('a');
        anchor.href = url;
        anchor.download = fileName;
        document.body.appendChild(anchor);
        anchor.click();
        document.body.removeChild(anchor);

        window.setTimeout(function () { URL.revokeObjectURL(url); }, 0);
    }

    document.addEventListener('click', function (event) {
        const button = event.target && event.target.closest
            ? event.target.closest('[data-svg-action]')
            : null;
        if (!button) return;

        event.preventDefault();

        const action = button.getAttribute('data-svg-action');

        if (action === 'copy') {
            const text = findSource(button);
            if (text === null) {
                console.error('[svg-render] source template missing');
                return;
            }

            copyText(text).then(function () {
                flashCopied(button);
            }).catch(function (error) {
                console.error('[svg-render] copy failed', error);
            });
        } else if (action === 'download') {
            const text = findDownloadSource(button);
            if (text === null) {
                console.error('[svg-render] download source missing');
                return;
            }

            download(button, text);
        }
    });

    window.svgRenderActions = { copyText: copyText, download: download };
})();
