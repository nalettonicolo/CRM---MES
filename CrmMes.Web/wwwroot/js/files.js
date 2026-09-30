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
