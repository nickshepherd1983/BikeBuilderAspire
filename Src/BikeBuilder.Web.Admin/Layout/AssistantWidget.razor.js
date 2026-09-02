// Collocated module for AssistantWidget: scrolls the transcript to its end with an ease-out
// tween whenever a message is added, so the newest bubble is what the user sees. The target
// is re-read every frame because the answer bubble can still be growing (expansion panel,
// table layout) while the tween runs. Reduced-motion users get an instant jump.
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
