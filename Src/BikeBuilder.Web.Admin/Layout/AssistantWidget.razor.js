// Collocated module for AssistantWidget: the transcript's scroll tween and the panel's
// drag-to-resize handle.

const sizeKey = "bikebuilder.assistant.size";
const minWidth = 320;
const minHeight = 320;

// Wires the resize handle in the panel's top-right corner. The panel is anchored bottom-left,
// so dragging right widens it and dragging up makes it taller; the size is kept per browser
// so it survives closing the panel and reloading the app. Double-click restores the default.
// Idempotent: the panel element is recreated every time it opens, and the flag on it stops a
// second wiring on re-render.
export function initPanel(panel, handle) {
  if (!panel || !handle || panel.dataset.resizable)
    return;

  panel.dataset.resizable = "true";
  applySavedSize(panel);

  let drag = null;

  handle.addEventListener("pointerdown", e => {
    e.preventDefault();
    handle.setPointerCapture(e.pointerId);
    const rect = panel.getBoundingClientRect();
    drag = { x: e.clientX, y: e.clientY, width: rect.width, height: rect.height };
    panel.classList.add("assistant-resizing");
  });

  handle.addEventListener("pointermove", e => {
    if (!drag)
      return;

    const width = clamp(drag.width + (e.clientX - drag.x), minWidth, window.innerWidth - 32);
    const height = clamp(drag.height - (e.clientY - drag.y), minHeight, window.innerHeight - 140);
    panel.style.width = `${width}px`;
    panel.style.height = `${height}px`;
  });

  const end = () => {
    if (!drag)
      return;

    drag = null;
    panel.classList.remove("assistant-resizing");
    try {
      localStorage.setItem(sizeKey, JSON.stringify({ width: panel.offsetWidth, height: panel.offsetHeight }));
    } catch {
      // Storage blocked (private mode, policy) - the size just won't persist.
    }
  };
  handle.addEventListener("pointerup", end);
  handle.addEventListener("pointercancel", end);

  handle.addEventListener("dblclick", () => {
    panel.style.width = "";
    panel.style.height = "";
    try {
      localStorage.removeItem(sizeKey);
    } catch {
      // As above.
    }
  });
}

function applySavedSize(panel) {
  try {
    const saved = JSON.parse(localStorage.getItem(sizeKey) ?? "null");
    if (!saved)
      return;

    panel.style.width = `${clamp(saved.width, minWidth, window.innerWidth - 32)}px`;
    panel.style.height = `${clamp(saved.height, minHeight, window.innerHeight - 140)}px`;
  } catch {
    // Unreadable or blocked storage - keep the stylesheet's default size.
  }
}

function clamp(value, min, max) {
  return Math.min(Math.max(value, min), Math.max(min, max));
}

// Scrolls the transcript to its end with an ease-out tween whenever a message is added, so
// the newest bubble is what the user sees. The target is re-read every frame because the
// answer bubble can still be growing (expansion panel, table layout) while the tween runs.
// Reduced-motion users get an instant jump.
export function scrollToBottom(element) {
  if (!element)
    return;

  const target = () => Math.max(0, element.scrollHeight - element.clientHeight);
  const start = element.scrollTop;
  if (target() - start <= 1)
    return;

  if (window.matchMedia("(prefers-reduced-motion: reduce)").matches) {
    element.scrollTop = target();
    return;
  }

  // Longer for longer distances, capped so a tall table never feels sluggish.
  const duration = Math.min(650, 250 + (target() - start) * 0.4);
  const startedAt = performance.now();
  const easeOutCubic = t => 1 - Math.pow(1 - t, 3);

  const step = now => {
    const progress = Math.min(1, (now - startedAt) / duration);
    element.scrollTop = start + (target() - start) * easeOutCubic(progress);
    if (progress < 1)
      requestAnimationFrame(step);
  };

  requestAnimationFrame(step);
}
