// A plain canvas signature pad, the same drawing logic as the technician page (/tecnici): capturing a
// handwritten signature image, not a qualified electronic signature. One pad per canvas id, so a page can
// host more than one if it ever needs to.
window.nicolomes = window.nicolomes || {};
window.nicolomes.signature = (function () {
  const pads = new Map();

  function pad(canvasId) {
    const canvas = document.getElementById(canvasId);
    if (!canvas) {
      return null;
    }

    let state = pads.get(canvasId);
    if (state) {
      return state;
    }

    const context = canvas.getContext("2d");
    state = { context, drawing: false, strokes: 0 };
    pads.set(canvasId, state);

    function resize() {
      const ratio = window.devicePixelRatio || 1;
      const rect = canvas.getBoundingClientRect();
      canvas.width = Math.max(1, Math.round(rect.width * ratio));
      canvas.height = Math.max(1, Math.round(rect.height * ratio));
      context.setTransform(ratio, 0, 0, ratio, 0, 0);
      context.fillStyle = "#FFFFFF";
      context.fillRect(0, 0, rect.width, rect.height);
      context.lineWidth = 2.2;
      context.lineCap = "round";
      context.lineJoin = "round";
      context.strokeStyle = "#111111";
      state.strokes = 0;
    }

    function point(event) {
      const rect = canvas.getBoundingClientRect();
      return [event.clientX - rect.left, event.clientY - rect.top];
    }

    canvas.addEventListener("pointerdown", (event) => {
      state.drawing = true;
      canvas.setPointerCapture(event.pointerId);
      const [x, y] = point(event);
      context.beginPath();
      context.moveTo(x, y);
      state.strokes++;
    });
    canvas.addEventListener("pointermove", (event) => {
      if (!state.drawing) return;
      const [x, y] = point(event);
      context.lineTo(x, y);
      context.stroke();
    });
    ["pointerup", "pointercancel", "pointerleave"].forEach((type) => canvas.addEventListener(type, () => { state.drawing = false; }));

    state.resize = resize;
    resize();
    return state;
  }

  return {
    init(canvasId) { pad(canvasId); },
    reset(canvasId) { pad(canvasId)?.resize(); },
    isEmpty(canvasId) { return (pad(canvasId)?.strokes ?? 0) === 0; },
    toDataUrl(canvasId) {
      const canvas = document.getElementById(canvasId);
      return canvas ? canvas.toDataURL("image/png") : null;
    },
  };
})();
