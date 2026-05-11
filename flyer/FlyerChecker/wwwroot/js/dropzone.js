// dropzone.js – wire up native drag-and-drop to the hidden InputFile element
// Called once after the drop zone div is rendered.
window.initDropZone = function (dropDiv) {
    dropDiv.addEventListener('drop', function (e) {
        e.preventDefault();
        e.stopPropagation();

        const input = dropDiv.querySelector('input[type="file"]');
        if (!input || !e.dataTransfer || e.dataTransfer.files.length === 0) return;

        // Assign the dropped FileList to the input and trigger change so Blazor picks it up.
        const dt = new DataTransfer();
        for (const file of e.dataTransfer.files) {
            dt.items.add(file);
        }
        input.files = dt.files;
        input.dispatchEvent(new Event('change', { bubbles: true }));
    });
};
