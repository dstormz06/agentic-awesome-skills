// A tiny, safe XML reader for the few parts of an .xlsx file this tool needs. It never loads a DTD or an external entity (a document
// type declaration is refused outright), never evaluates anything, and reports each element to a callback so a large sheet is not
// held as a tree.
const ENTITIES = { amp: '&', lt: '<', gt: '>', quot: '"', apos: "'" };
const decode = (s) => (s.indexOf('&') < 0 ? s : s.replace(/&(#x[0-9a-fA-F]+|#[0-9]+|[a-z]+);/g, (m, e) => {
  if (e[0] === '#') { const cp = e[1] === 'x' ? parseInt(e.slice(2), 16) : parseInt(e.slice(1), 10); return cp > 0 && cp <= 0x10ffff ? String.fromCodePoint(cp) : ''; }
  return ENTITIES[e] ?? m;
}));
const local = (name) => { const i = name.indexOf(':'); return i < 0 ? name : name.slice(i + 1); };

/** @param handlers {open(name, attrs, selfClosing), text(text), close(name)} where names are local names (no prefix) */
export function parseXml(xml, handlers) {
  let i = 0;
  const n = xml.length;
  while (i < n) {
    const lt = xml.indexOf('<', i);
    if (lt < 0) { if (handlers.text && xml.slice(i).trim()) handlers.text(decode(xml.slice(i))); break; }
    if (lt > i && handlers.text) handlers.text(decode(xml.slice(i, lt)));
    if (xml.startsWith('<!--', lt)) { const e = xml.indexOf('-->', lt + 4); if (e < 0) throw new Error('bad xml'); i = e + 3; continue; }
    if (xml.startsWith('<![CDATA[', lt)) { const e = xml.indexOf(']]>', lt + 9); if (e < 0) throw new Error('bad xml'); if (handlers.text) handlers.text(xml.slice(lt + 9, e)); i = e + 3; continue; }
    if (xml.startsWith('<!', lt)) throw new Error('A document type declaration is not allowed.');
    if (xml.startsWith('<?', lt)) { const e = xml.indexOf('?>', lt + 2); if (e < 0) throw new Error('bad xml'); i = e + 2; continue; }
    const gt = findTagEnd(xml, lt + 1);
    if (gt < 0) throw new Error('bad xml');
    let body = xml.slice(lt + 1, gt);
    if (body[0] === '/') { handlers.close && handlers.close(local(body.slice(1).trim())); i = gt + 1; continue; }
    const selfClosing = body.endsWith('/');
    if (selfClosing) body = body.slice(0, -1);
    const sp = body.search(/[\s]/);
    const name = local(sp < 0 ? body : body.slice(0, sp));
    const attrs = {};
    if (sp >= 0) {
      const re = /([^\s=]+)\s*=\s*("([^"]*)"|'([^']*)')/g;
      let m;
      while ((m = re.exec(body.slice(sp)))) attrs[local(m[1])] = decode(m[3] ?? m[4]);
    }
    handlers.open && handlers.open(name, attrs, selfClosing);
    if (selfClosing && handlers.close) handlers.close(name);
    i = gt + 1;
  }
}

function findTagEnd(xml, from) {
  let quote = null;
  for (let i = from; i < xml.length; i++) {
    const c = xml[i];
    if (quote) { if (c === quote) quote = null; } else if (c === '"' || c === "'") quote = c; else if (c === '>') return i;
  }
  return -1;
}
