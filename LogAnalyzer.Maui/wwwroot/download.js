// Streams bytes produced on the server to a browser file download.
window.downloadFileFromStream = async (fileName, contentStreamReference) => {
    const arrayBuffer = await contentStreamReference.arrayBuffer();
    const blob = new Blob([arrayBuffer]);
    const url = URL.createObjectURL(blob);
    const anchor = document.createElement('a');
    anchor.href = url;
    anchor.download = fileName ?? 'download';
    document.body.appendChild(anchor);
    anchor.click();
    anchor.remove();
    URL.revokeObjectURL(url);
};

// Wires a visual drop zone to hidden <input type="file"> controls used by Blazor InputFile.
window.registerLogDropZone = (dropZone, zipInputId, logInputId) => {
    if (!dropZone || dropZone.__logDropHandlers) return;

    const zipInput = document.getElementById(zipInputId);
    const logInput = document.getElementById(logInputId);
    if (!dropZone || !zipInput || !logInput) return;

    const setDropActive = (active) => {
        dropZone.classList.toggle('drag-over', active);
    };

    const setDropError = () => {
        dropZone.classList.remove('drag-over');
        dropZone.classList.add('drop-error');
        window.setTimeout(() => dropZone.classList.remove('drop-error'), 1200);
    };

    const setFiles = (input, files) => {
        if (!files || files.length === 0) return false;
        const transfer = new DataTransfer();
        for (const file of files) {
            transfer.items.add(file);
        }

        try {
            input.files = transfer.files;
            input.dispatchEvent(new Event('change', { bubbles: true }));
            return true;
        } catch {
            return false;
        }
    };

    const getDroppedFiles = (event) => {
        const dt = event.dataTransfer;
        if (!dt) return [];
        if (dt.items && dt.items.length > 0) {
            const files = [];
            for (const item of dt.items) {
                if (item.kind === 'file') {
                    const file = item.getAsFile();
                    if (file) files.push(file);
                }
            }
            return files;
        }
        return Array.from(dt.files || []);
    };

    const onDrag = (event) => {
        event.preventDefault();
        event.stopPropagation();
        setDropActive(true);
    };

    const onLeave = (event) => {
        event.preventDefault();
        event.stopPropagation();
        setDropActive(false);
    };

    const onDrop = (event) => {
        event.preventDefault();
        event.stopPropagation();
        setDropActive(false);

        const dropped = getDroppedFiles(event);
        if (dropped.length === 0) {
            setDropError();
            return;
        }

        const zipFiles = dropped.filter((f) => f.name.toLowerCase().endsWith('.zip'));
        const logFiles = dropped.filter((f) => f.name.toLowerCase().endsWith('.log'));

        // Supported inputs: either one ZIP or one/many .log files.
        if (zipFiles.length > 1 || (zipFiles.length > 0 && logFiles.length > 0)) {
            setDropError();
            return;
        }

        if (zipFiles.length > 0) {
            if (!setFiles(zipInput, [zipFiles[0]])) setDropError();
            return;
        }

        if (logFiles.length > 0) {
            if (!setFiles(logInput, logFiles)) setDropError();
            return;
        }

        setDropError();
    };

    dropZone.addEventListener('dragenter', onDrag);
    dropZone.addEventListener('dragover', onDrag);
    dropZone.addEventListener('dragleave', onLeave);
    dropZone.addEventListener('drop', onDrop);

    dropZone.__logDropHandlers = { onDrag, onLeave, onDrop };
};

window.unregisterLogDropZone = (dropZone) => {
    if (!dropZone || !dropZone.__logDropHandlers) return;

    const { onDrag, onLeave, onDrop } = dropZone.__logDropHandlers;
    dropZone.removeEventListener('dragenter', onDrag);
    dropZone.removeEventListener('dragover', onDrag);
    dropZone.removeEventListener('dragleave', onLeave);
    dropZone.removeEventListener('drop', onDrop);
    dropZone.classList.remove('drag-over');
    dropZone.classList.remove('drop-error');
    delete dropZone.__logDropHandlers;
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

    const setDropActive = (active) => {
        dropZone.classList.toggle('drag-over', active);
    };

    const assignFiles = (files) => {
        if (!files || files.length === 0) return false;

        try {
            const transfer = new DataTransfer();
            for (const file of files) transfer.items.add(file);
            input.files = transfer.files;
            input.dispatchEvent(new Event('change', { bubbles: true }));
            return true;
        } catch {
            return false;
        }
    };

    const extractFiles = (event) => {
        const dt = event.dataTransfer;
        if (!dt) return [];
        if (dt.items && dt.items.length > 0) {
            const files = [];
            for (const item of dt.items) {
                if (item.kind === 'file') {
                    const f = item.getAsFile();
                    if (f) files.push(f);
                }
            }
            return files;
        }
        return Array.from(dt.files || []);
    };

    const onDragOver = (event) => {
        event.preventDefault();
        event.stopPropagation();
        setDropActive(true);
    };

    const onDragLeave = (event) => {
        event.preventDefault();
        event.stopPropagation();
        setDropActive(false);
    };

    const onDrop = (event) => {
        event.preventDefault();
        event.stopPropagation();
        setDropActive(false);

        const files = extractFiles(event);
        assignFiles(files);
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
