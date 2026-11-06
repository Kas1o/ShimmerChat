// 代码块工具条：复制 / 下载。
//
// 为什么用事件委托：消息 HTML 是 Blazor 的 MarkupString 注入的，没法挂 @onclick；
// 而且流式生成时这段 DOM 会被反复替换，逐个元素绑定必然失效——委托到 document 就行。

(function () {
    if (window.codeBlockActions) return;

    const COPY_ICON = 'bi bi-clipboard';
    const COPIED_ICON = 'bi bi-check2';
    const FLASH_MS = 1500;

    function findCode(button) {
        const block = button.closest('.code-block');
        if (!block) return null;
        const code = block.querySelector('pre code');
        return code ? code.textContent : null;
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
        const label = button.querySelector('.code-block-btn-text');
        const icon = button.querySelector('i');

        if (label && !button.dataset.originalLabel) {
            button.dataset.originalLabel = label.textContent;
        }

        const copiedLabel = button.getAttribute('data-code-copied') || button.dataset.originalLabel || '';
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
        const fileName = button.getAttribute('data-code-filename') || 'snippet.txt';
        const blob = new Blob([text], { type: 'text/plain;charset=utf-8' });
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
            ? event.target.closest('[data-code-action]')
            : null;
        if (!button) return;

        event.preventDefault();

        const text = findCode(button);
        if (text === null) return;

        const action = button.getAttribute('data-code-action');

        if (action === 'copy') {
            copyText(text).then(function () {
                flashCopied(button);
            }).catch(function (error) {
                console.error('[code-block-actions] copy failed', error);
            });
        } else if (action === 'download') {
            download(button, text);
        }
    });

    window.codeBlockActions = { copyText: copyText, download: download };
})();
