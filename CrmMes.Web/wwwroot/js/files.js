// Saves a file the app downloaded with the user's token (an XML invoice, for instance): a plain link
// can't carry the Authorization header. Loaded from this site only, as the CSP requires.
window.nicolomes = window.nicolomes || {};
window.nicolomes.saveFile = function (fileName, contentType, base64) {
  const bytes = Uint8Array.from(atob(base64), c => c.charCodeAt(0));
  const url = URL.createObjectURL(new Blob([bytes], { type: contentType }));
  const link = document.createElement("a");
  link.href = url;
  link.download = fileName;
  document.body.appendChild(link);
  link.click();
  link.remove();
  setTimeout(() => URL.revokeObjectURL(url), 10000);
};

// Opens a downloaded file in a new tab (a drawing, a PDF of work instructions) instead of saving it.
window.nicolomes.openFile = function (fileName, contentType, base64) {
  // Only PDFs and images are shown in the browser; anything else is saved, never rendered.
  if (!/^(application\/pdf|image\/(png|jpeg|gif|webp|bmp))$/.test(contentType)) {
    window.nicolomes.saveFile(fileName, "application/octet-stream", base64);
    return;
  }
  const bytes = Uint8Array.from(atob(base64), c => c.charCodeAt(0));
  const url = URL.createObjectURL(new Blob([bytes], { type: contentType }));
  const opened = window.open(url, "_blank");
  if (!opened) {
    window.nicolomes.saveFile(fileName, contentType, base64);
  }
  setTimeout(() => URL.revokeObjectURL(url), 60000);
};
