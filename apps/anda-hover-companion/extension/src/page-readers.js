// These run inside the page (injected with chrome.scripting.executeScript), so each one must stand alone: no imports, no outer variables.
// They read only what the person selected or is typing, or the visible text of the page they asked to scan.
export function pageSelection() {
  const s = getSelection();
  let text = s ? s.toString() : '', rect = null;
  const ae = document.activeElement;
  const isField = ae && (ae.tagName === 'TEXTAREA' || (ae.tagName === 'INPUT' && /^(text|search|)$/.test(ae.type)));
  if (!text && isField && ae.selectionStart !== ae.selectionEnd) text = ae.value.slice(ae.selectionStart, ae.selectionEnd);
  if (s && s.rangeCount && text) { const r = s.getRangeAt(0).getBoundingClientRect(); if (r && (r.width || r.height)) rect = { x: r.left, y: r.top, w: r.width, h: r.height }; }
  if (!rect && isField && text) { const r = ae.getBoundingClientRect(); rect = { x: r.left, y: r.top, w: r.width, h: r.height }; }
  return { text: text.slice(0, 20000), rect, top: window === window.top };
}
export function pageDraft() {
  const s = getSelection();
  let text = s ? s.toString() : '', from = 'selection';
  const ae = document.activeElement;
  if (!text && ae) {
    if (ae.tagName === 'TEXTAREA' || ae.tagName === 'INPUT') { const sel = ae.selectionStart !== ae.selectionEnd ? ae.value.slice(ae.selectionStart, ae.selectionEnd) : ''; text = sel || ae.value; from = sel ? 'selection' : 'the field you are typing in'; }
    else if (ae.isContentEditable) { text = ae.innerText; from = 'the message you are writing'; }
  }
  return { text: text.slice(0, 20000), from };
}
export function pageText() { return { text: (document.body ? document.body.innerText : '').slice(0, 400000) }; }

