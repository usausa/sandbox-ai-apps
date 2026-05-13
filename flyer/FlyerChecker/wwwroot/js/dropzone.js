// dropzone.js - wire up native drag-and-drop to the hidden InputFile element
window.initDropZone = function (dropDiv) {
    if (!dropDiv || dropDiv._dropZoneInitialized) return;
    dropDiv._dropZoneInitialized = true;

    dropDiv.addEventListener('drop', function (e) {
        e.preventDefault();
        e.stopPropagation();

        const input = dropDiv.querySelector('input[type="file"]');
        if (!input || !e.dataTransfer || e.dataTransfer.files.length === 0) return;

        const dt = new DataTransfer();
        for (const file of e.dataTransfer.files) {
            dt.items.add(file);
        }
        input.files = dt.files;
        input.dispatchEvent(new Event('change', { bubbles: true }));
    });
};
