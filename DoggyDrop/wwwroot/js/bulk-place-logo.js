(function () {
    "use strict";
    const input = document.getElementById("bulkLogoFile");
    const preview = document.getElementById("bulkLogoPreview");
    if (!input || !preview || !window.URL?.createObjectURL) return;
    const image = preview.querySelector("img");
    let objectUrl = null;
    function clear() {
        preview.hidden = true;
        image.removeAttribute("src");
        if (objectUrl) URL.revokeObjectURL(objectUrl);
        objectUrl = null;
    }
    input.addEventListener("change", function () {
        clear();
        const file = input.files?.[0];
        if (!file || file.size > 5 * 1024 * 1024 || !["image/png", "image/jpeg", "image/webp"].includes(file.type)) return;
        objectUrl = URL.createObjectURL(file);
        image.src = objectUrl;
        preview.hidden = false;
    });
    image.addEventListener("error", clear);
    window.addEventListener("pagehide", clear);
}());
