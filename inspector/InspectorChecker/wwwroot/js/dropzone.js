window.initDropZone = function (dropDiv) {
    if (!dropDiv || dropDiv._dropZoneInitialized) {
        return;
    }

    const setDragState = (isActive) => {
        dropDiv.classList.toggle("is-drag-active", isActive);
    };

    const preventDefaults = (e) => {
        e.preventDefault();
        e.stopPropagation();
    };

    dropDiv._dropZoneInitialized = true;

    ["dragenter", "dragover"].forEach((eventName) => {
        dropDiv.addEventListener(eventName, function (e) {
            preventDefaults(e);
            setDragState(true);
        });
    });

    ["dragleave", "dragend"].forEach((eventName) => {
        dropDiv.addEventListener(eventName, function (e) {
            preventDefaults(e);

            if (e.target === dropDiv) {
                setDragState(false);
            }
        });
    });

    dropDiv.addEventListener("drop", function (e) {
        preventDefaults(e);
        setDragState(false);

        const input = dropDiv.querySelector('input[type="file"]');
        const droppedFiles = e.dataTransfer?.files;
        if (!input || input.disabled || !droppedFiles || droppedFiles.length === 0) {
            return;
        }

        const dt = new DataTransfer();
        dt.items.add(droppedFiles[0]);
        input.files = dt.files;
        input.dispatchEvent(new Event("change", { bubbles: true }));
    });
};
