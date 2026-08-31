// Streams bytes produced on the server to a browser file download.
// Prefers stream() over arrayBuffer(): the latter materialises the whole export as one contiguous
// buffer *and* then copies it into the blob, which is a lot of memory for a large filtered set.
window.downloadFileFromStream = async (fileName, contentStreamReference) => {
    let blob;
    if (typeof contentStreamReference.stream === 'function' && typeof Response === 'function') {
        blob = await new Response(await contentStreamReference.stream()).blob();
    } else {
        blob = new Blob([await contentStreamReference.arrayBuffer()]);
    }

    const url = URL.createObjectURL(blob);
    const anchor = document.createElement('a');
    anchor.href = url;
    anchor.download = fileName ?? 'download';
    document.body.appendChild(anchor);
    anchor.click();
    anchor.remove();
    URL.revokeObjectURL(url);
};

window.copyTextToClipboard = async (text) => {
    if (navigator.clipboard && window.isSecureContext) {
        await navigator.clipboard.writeText(text ?? '');
        return;
    }

    const textarea = document.createElement('textarea');
    textarea.value = text ?? '';
    textarea.style.position = 'fixed';
    textarea.style.left = '-9999px';
    textarea.style.top = '0';
    document.body.appendChild(textarea);
    textarea.focus();
    textarea.select();

    try {
        document.execCommand('copy');
    } finally {
        textarea.remove();
    }
};

window.registerNativeDropInput = (dropZone, inputId) => {
    if (!dropZone || dropZone.__nativeDropHandlers) return;

    const input = document.getElementById(inputId);
    if (!input) return;

    const setDropActive = (active) => dropZone.classList.toggle('drag-over', active);

    const assignFiles = (files) => {
        if (!files || files.length === 0) return false;
        try {
            const transfer = new DataTransfer();
            for (const file of files) transfer.items.add(file);
            input.files = transfer.files;
            input.dispatchEvent(new Event('change', { bubbles: true }));
            return true;
        } catch { return false; }
    };

    const extractFiles = (event) => {
        const dt = event.dataTransfer;
        if (!dt) return [];
        if (dt.items && dt.items.length > 0) {
            const files = [];
            for (const item of dt.items) {
                if (item.kind === 'file') { const f = item.getAsFile(); if (f) files.push(f); }
            }
            return files;
        }
        return Array.from(dt.files || []);
    };

    const onDragOver = (e) => { e.preventDefault(); e.stopPropagation(); setDropActive(true); };
    const onDragLeave = (e) => { e.preventDefault(); e.stopPropagation(); setDropActive(false); };
    const onDrop = (e) => {
        e.preventDefault(); e.stopPropagation(); setDropActive(false);
        assignFiles(extractFiles(e));
    };

    dropZone.addEventListener('dragenter', onDragOver);
    dropZone.addEventListener('dragover', onDragOver);
    dropZone.addEventListener('dragleave', onDragLeave);
    dropZone.addEventListener('drop', onDrop);
    dropZone.__nativeDropHandlers = { onDragOver, onDragLeave, onDrop };
};

window.unregisterNativeDropInput = (dropZone) => {
    if (!dropZone || !dropZone.__nativeDropHandlers) return;
    const { onDragOver, onDragLeave, onDrop } = dropZone.__nativeDropHandlers;
    dropZone.removeEventListener('dragenter', onDragOver);
    dropZone.removeEventListener('dragover', onDragOver);
    dropZone.removeEventListener('dragleave', onDragLeave);
    dropZone.removeEventListener('drop', onDrop);
    dropZone.classList.remove('drag-over');
    delete dropZone.__nativeDropHandlers;
};

// Recently used paths for the folder / live-file boxes (see PathHistory).
// Kept in localStorage so they survive a restart; failures are swallowed because
// history is a convenience and private-mode / policy can block storage.
window.pathHistoryLoad = (key) => {
    try {
        const raw = localStorage.getItem(key);
        const list = raw ? JSON.parse(raw) : [];
        return Array.isArray(list) ? list.filter(v => typeof v === 'string') : [];
    } catch {
        return [];
    }
};

window.pathHistorySave = (key, values) => {
    try {
        localStorage.setItem(key, JSON.stringify(values ?? []));
    } catch { }
};
