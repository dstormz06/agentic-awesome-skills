// Draws the card, the invite chip, notices and the result panels from plain data. Used by the page script (inside a closed shadow
// root) and by the toolbar popup. It never uses innerHTML with data: every string goes in as text, so a note or a page cannot inject markup.
(function (root) {
  'use strict';

  const CSS = `
:host{all:initial}
*{box-sizing:border-box}
.k{--bg:#fdfdfd;--ink:#2a2e33;--ink2:#4a4e53;--ink3:#5c6167;--hair:#ececec;--line:#dedede;--accent:#56626e;--good:#1a6b43;--good-bg:#e6f1ea;
  --warn:#8a5800;--warn-bg:#faf1dc;--bad:#a33636;--bad-bg:#f9e9e9;--track:#e6e6e6;--fill:#6e7a86;--mark:rgba(86,98,110,.10);
  font:13px/1.45 "Segoe UI Variable Text","Segoe UI",-apple-system,system-ui,sans-serif;color:var(--ink)}
button{font:inherit;color:inherit;cursor:pointer}
:focus-visible{outline:2px solid var(--accent);outline-offset:2px;border-radius:4px}
.card{width:372px;max-width:calc(100vw - 24px);max-height:calc(100vh - 24px);overflow:auto;background:var(--bg);border:1px solid var(--line);border-radius:8px;
  box-shadow:0 18px 36px -14px rgba(20,22,24,.22),0 3px 8px rgba(20,22,24,.07)}
.card.wide{width:492px}
.slot{padding:10px 16px;border-top:1px solid var(--hair)}
.slot:first-child{border-top:none}
.idrow{display:flex;align-items:center;gap:8px}
.num{font-size:15px;font-weight:700;letter-spacing:.01em}
.seal{font-size:9.5px;font-weight:800;letter-spacing:.1em;border:1px solid var(--line);border-radius:99px;padding:1px 8px;color:var(--ink3)}
.seal.demo{background:var(--warn-bg);color:var(--warn);border-color:transparent}
.tools{margin-left:auto;display:flex;gap:2px}
.tools button{border:none;background:none;width:30px;height:30px;border-radius:6px;color:var(--ink3);font-size:15px;line-height:1}
.tools button:hover{background:var(--mark)}
.drug{margin:6px 0 0;font-size:14.5px;font-weight:600}
.meta{color:var(--ink2)}
.fresh{font-size:11px;color:var(--ink3);margin-top:2px}
.row{display:flex;gap:9px;align-items:baseline}
.lbl{font-size:9.5px;font-weight:800;letter-spacing:.13em;color:var(--ink3);width:78px;flex:none;white-space:nowrap}
.date{font-size:15px;font-weight:700}
.days{color:var(--ink2);font-size:12px}
.pct{margin-left:auto;color:var(--ink2);font-size:11.5px}
.line{position:relative;height:10px;margin:6px 0 8px}
.line i{position:absolute;left:0;top:4px;height:2px;border-radius:1px;background:var(--fill)}
.line:before{content:"";position:absolute;left:0;right:0;top:4px;height:2px;border-radius:1px;background:var(--track)}
.line b{position:absolute;top:1px;width:9px;height:9px;margin-left:-4px;border-radius:50%;background:var(--fill)}
.delta{font-weight:650}.delta.late{color:var(--warn)}.delta.ok{color:var(--good)}.delta.unk{color:var(--ink3);font-weight:500}
.conf{text-align:right;font-size:11px;color:var(--ink3)}
.attn{display:flex;gap:9px;align-items:baseline;border-radius:8px;padding:8px 10px;border:1px solid var(--warn);color:var(--warn)}
.attn.bad{border-color:var(--bad);color:var(--bad)}
.attn .k2{font-size:9.5px;font-weight:800;letter-spacing:.1em;white-space:nowrap}
.rail{display:flex;gap:9px;align-items:baseline;color:var(--ink2)}
.warn{color:var(--warn)}.bad{color:var(--bad)}.info{color:var(--ink2)}
.more{color:var(--ink3)}
.tag{display:flex;gap:9px;align-items:center;margin-bottom:4px}
.tag .verify{font-size:10.5px;color:var(--ink3)}
.brief{color:var(--ink2)}
.reason{font-size:11.5px;color:var(--ink3);padding-top:0}
.btn{border:1px solid var(--line);background:#fff;border-radius:99px;padding:6px 12px;font-size:12px;min-height:30px}
.btn:hover{background:var(--mark)}
.btn.primary{background:var(--accent);border-color:var(--accent);color:#fff}
.chips{display:flex;flex-wrap:wrap;gap:6px}
.drawer details{border-top:1px solid var(--hair)}
.drawer summary{cursor:pointer;padding:10px 16px;font-weight:650;color:var(--ink2);list-style:none;min-height:40px}
.drawer summary::-webkit-details-marker{display:none}
.drawer summary:before{content:"›";display:inline-block;width:14px;color:var(--ink3)}
.drawer details[open] summary:before{transform:rotate(90deg)}
.drawer .cnt{padding:0 16px 10px;color:var(--ink2)}
.wf{padding:6px 0;border-top:1px solid var(--hair)}.wf:first-child{border-top:none}
.wf .why,.wf .src,.who{font-size:11.5px;color:var(--ink3)}
.kbd{display:flex;gap:12px;padding:6px 16px;font-size:10.5px;color:var(--ink3);border-top:1px solid var(--hair)}
.chip{display:inline-flex;align-items:center;gap:7px;background:var(--bg);border:1px solid var(--line);border-radius:18px;padding:8px 14px;font-weight:600;box-shadow:0 8px 20px -10px rgba(20,22,24,.3);max-width:340px}
.chip .dot{width:8px;height:8px;border-radius:50%;background:var(--accent)}
.chip.notice{font-weight:500;cursor:default;border-radius:10px}
.panel{width:440px;max-width:calc(100vw - 24px);max-height:calc(100vh - 24px);overflow:auto;background:var(--bg);border:1px solid var(--line);border-radius:8px;
  box-shadow:0 18px 36px -14px rgba(20,22,24,.22),0 3px 8px rgba(20,22,24,.07)}
.panel h2{margin:0;font-size:14px}
.find{margin:6px 16px;padding:8px 10px;border-radius:8px;font-size:12px;line-height:1.4}
.find.diff{background:var(--warn-bg);color:var(--warn)}.find.ok{background:var(--good-bg);color:var(--good)}.find.note{background:var(--mark);color:var(--ink2)}
.item{display:flex;gap:10px;align-items:baseline;width:100%;text-align:left;border:none;background:none;padding:8px 16px;border-top:1px solid var(--hair);min-height:40px}
.item:hover{background:var(--mark)}
.item .n{font-weight:700;min-width:60px}.item .d{flex:1;min-width:0}.item .d small{display:block;color:var(--ink3);font-size:11.5px}
.sev{font-size:9.5px;font-weight:800;letter-spacing:.08em;border-radius:99px;padding:1px 7px;white-space:nowrap}
.sev.bad{background:var(--bad-bg);color:var(--bad)}.sev.warn{background:var(--warn-bg);color:var(--warn)}.sev.info{background:var(--mark);color:var(--ink2)}
.empty{padding:12px 16px;color:var(--ink2)}
@media (prefers-reduced-motion:no-preference){.card,.panel{animation:in .16s ease-out}@keyframes in{from{opacity:0;transform:translateY(3px)}to{opacity:1;transform:none}}}
`;

  function h(tag, props, ...kids) {
    const el = document.createElement(tag);
    if (props) {
      for (const [k, v] of Object.entries(props)) {
        if (v === undefined || v === null || v === false) continue;
        if (k === 'class') el.className = v;
        else if (k === 'text') el.textContent = v;
        else if (k === 'style') {
          // set through the style object, never as an attribute: a strict page policy refuses inline style attributes but allows this
          for (const decl of String(v).split(';')) { const i = decl.indexOf(':'); if (i > 0) el.style.setProperty(decl.slice(0, i).trim(), decl.slice(i + 1).trim()); }
        } else if (k.startsWith('on')) el.addEventListener(k.slice(2), v);
        else el.setAttribute(k, v === true ? '' : String(v));
      }
    }
    for (const kid of kids.flat()) if (kid !== null && kid !== undefined && kid !== false) el.append(kid.nodeType ? kid : document.createTextNode(String(kid)));
    return el;
  }

  const sevClass = (s) => (s === 'bad' ? 'bad' : s === 'warn' ? 'warn' : 'info');

  function drawer(m, handlers) {
    const flag = (f) => h('div', { class: 'wf' }, h('div', { class: sevClass(f.severity), text: f.text }), h('div', { class: 'why', text: f.why }), h('div', { class: 'src', text: 'Source: ' + f.source }));
    const parts = [];
    if (m.watch.length > 0) parts.push(h('details', { open: true }, h('summary', { text: `Watch list · ${m.watch.length}` }), h('div', { class: 'cnt' }, m.watch.map(flag))));
    parts.push(h('details', null, h('summary', { text: `Pending & urgent items · ${m.pending.length === 0 ? 'none' : m.pending.length}` }),
      h('div', { class: 'cnt' }, m.pending.length === 0 ? h('div', { text: 'No open items.' })
        : m.pending.map((p) => h('div', { class: 'row' }, h('span', { text: p.title, style: 'flex:1' }), h('span', { class: p.urgent ? 'warn' : 'who', text: p.due }))))));
    parts.push(h('details', null, h('summary', { text: 'Recent project notes' }),
      h('div', { class: 'cnt' }, m.notes.length === 0 ? h('div', { text: 'No notes.' }) : m.notes.map((n) => h('div', null, h('div', { class: 'who', text: n.who }), h('div', { text: n.text }))))));
    parts.push(h('details', null, h('summary', { text: 'Forecast & assumptions' }), h('div', { class: 'cnt', text: m.forecastDetail })));
    if (m.sisters.length > 0)
      parts.push(h('details', null, h('summary', { text: 'Sister / related ANDAs' }),
        h('div', { class: 'cnt' }, m.sisters.map((s) => h('div', null, h('button', { class: 'btn', text: `ANDA ${s.number}`, onclick: () => handlers.onSister && handlers.onSister(s.number) }), ' ', s.description)))));
    return h('div', { class: 'drawer' }, parts);
  }

  /**
   * @param m a card model; @param o {mini, expanded, reason, handlers: {onClose, onExpand, onMore, onCopy, onSister}}
   */
  function renderCard(m, o = {}) {
    const hd = o.handlers || {};
    const sealText = m.demo ? 'FICTIONAL DEMO' : m.source && m.source.startsWith('Your portfolio') ? 'YOUR FILE' : 'YOUR DATA';
    const forecastTone = !m.hasForecast ? 'unk' : m.forecastLate ? 'late' : 'ok';
    const head = h('div', { class: 'slot' },
      h('div', { class: 'idrow' }, h('span', { class: 'num', text: `${m.kind} ${m.number}` }), h('span', { class: 'seal' + (m.demo ? ' demo' : ''), text: sealText }),
        h('span', { class: 'tools' },
          o.mini ? null : h('button', { type: 'button', title: o.expanded ? 'Collapse (E)' : 'Expand (E)', 'aria-label': o.expanded ? 'Collapse the card' : 'Expand the card', text: o.expanded ? '⤡' : '⤢', onclick: () => hd.onExpand && hd.onExpand() }),
          h('button', { type: 'button', title: 'Close (Esc)', 'aria-label': 'Close the card', text: '✕', onclick: () => hd.onClose && hd.onClose() }))),
      h('h2', { class: 'drug', text: m.drug }), h('div', { class: 'meta', text: m.meta }), m.freshness ? h('div', { class: 'fresh', text: m.freshness }) : null);

    const clock = h('div', { class: 'slot' },
      h('div', { class: 'row' }, h('span', { class: 'lbl', text: 'GOAL' }), h('span', { class: 'date', text: m.goalDate }), h('span', { class: 'days', text: m.goalDays }),
        m.progress !== null ? h('span', { class: 'pct', text: m.progress + '%' }) : null),
      m.progress !== null ? h('div', { class: 'line', 'aria-hidden': 'true' }, h('i', { style: `width:${m.progress}%` }), h('b', { style: `left:${m.progress}%` })) : h('div', { style: 'height:8px' }),
      h('div', { class: 'row' }, h('span', { class: 'lbl', text: 'FORECAST' }), h('span', { class: 'date', text: m.forecastDate }), h('span', { class: 'delta ' + forecastTone, text: m.forecastDelta })),
      m.forecastConfidence ? h('div', { class: 'conf', text: m.forecastConfidence }) : null);

    const kids = [head, clock];
    if (o.mini) {
      if (o.reason) kids.push(h('div', { class: 'slot reason', text: 'Opened because: ' + o.reason }));
      kids.push(h('div', { class: 'slot' }, h('button', { class: 'btn primary', type: 'button', text: 'Full brief ↓', onclick: () => hd.onMore && hd.onMore() })));
    } else {
      if (m.attention) kids.push(h('div', { class: 'slot' }, h('div', { class: 'attn' + (m.attention.level === 'bad' ? ' bad' : '') }, h('span', { class: 'k2', text: '⚠ NEEDS YOU' }), h('span', { text: m.attention.text }))));
      kids.push(h('div', { class: 'slot rail' }, h('span', { class: 'lbl', text: 'LATEST' }), h('span', { text: m.latest })));
      if (m.ahead) kids.push(h('div', { class: 'slot rail' }, h('span', { class: 'lbl', text: 'AHEAD' }), h('span', { text: m.ahead })));
      if (m.watch.length > 0) {
        const top = m.watch[0];
        kids.push(h('div', { class: 'slot rail' }, h('span', { class: 'lbl', text: 'WATCH' }),
          h('span', null, h('span', { class: sevClass(top.severity), text: top.text }), m.watchMore > 0 ? h('span', { class: 'more', text: `  (+${m.watchMore} more in the drawer)` }) : null)));
      }
      if (m.reference && m.reference.psg) kids.push(h('div', { class: 'slot rail' }, h('span', { class: 'lbl', text: 'PSG' }), h('span', { text: m.reference.psg })));
      if (m.reference && m.reference.rld) kids.push(h('div', { class: 'slot rail' }, h('span', { class: 'lbl', text: 'RLD LABEL' }), h('span', { text: m.reference.rld })));
      kids.push(h('div', { class: 'slot' }, h('div', { class: 'tag' }, h('b', { style: 'font-size:9.5px;letter-spacing:.13em', text: m.brief.tag }), h('span', { class: 'verify', text: 'assembled from the record' })),
        h('div', { class: 'brief', text: m.brief.text })));
      if (o.expanded) kids.push(drawer(m, hd));
      kids.push(h('div', { class: 'slot' }, h('div', { class: 'chips' },
        h('button', { class: 'btn', type: 'button', text: 'Copy status', 'data-act': 'copy', onclick: (e) => hd.onCopy && hd.onCopy(m.statusLine, e.currentTarget) }),
        m.sisters.length > 0 ? h('button', { class: 'btn', type: 'button', text: `Sister ANDA · ${m.sisters.length}`, onclick: () => hd.onSister && hd.onSister(m.sisters[0].number) }) : null)));
      kids.push(h('div', { class: 'kbd' }, h('span', { text: 'Esc close' }), h('span', { text: 'E expand' }), h('span', { text: 'S copy status' })));
    }
    return h('div', { class: 'card' + (o.expanded && !o.mini ? ' wide' : ''), role: 'dialog', 'aria-label': `${m.kind} ${m.number} quick brief`, tabindex: '-1' }, kids);
  }

  function renderChip(label, onOpen) {
    return h('button', { class: 'chip', type: 'button', onclick: onOpen, 'aria-label': `${label}. Open a peek.` }, h('span', { class: 'dot' }), h('span', { text: label }), h('span', { text: 'Peek ›', style: 'font-weight:700' }));
  }
  const renderNotice = (text) => h('div', { class: 'chip notice', role: 'status', text });

  function panel(title, body, onClose) {
    return h('div', { class: 'panel', role: 'dialog', 'aria-label': title, tabindex: '-1' },
      h('div', { class: 'slot idrow' }, h('h2', { text: title }), h('span', { class: 'tools' }, h('button', { type: 'button', title: 'Close (Esc)', 'aria-label': 'Close', text: '✕', onclick: onClose }))), body);
  }

  function renderFindings(result) {
    return h('div', null, result.findings.map((f) => h('div', { class: 'find ' + (f.kind === 'diff' || f.kind === 'unknown' ? 'diff' : f.kind), text: f.text })));
  }

  /** @param items [{number, drug, goalDate, severity, topFlag, flagCount, urgent}] @param onOpen (number) => void */
  function renderList(items, onOpen, emptyText) {
    if (items.length === 0) return h('div', { class: 'empty', text: emptyText });
    return h('div', null, items.map((it) => h('button', { class: 'item', type: 'button', onclick: () => onOpen(it.number), 'aria-label': `ANDA ${it.number}, ${it.drug}` },
      h('span', { class: 'n', text: it.number }),
      h('span', { class: 'd' }, h('span', { text: it.drug }), it.topFlag ? h('small', { text: it.topFlag }) : null, it.status ? h('small', { text: it.status }) : null),
      it.severity ? h('span', { class: 'sev ' + sevClass(it.severity), text: it.severity === 'bad' ? 'ACT' : it.severity === 'warn' ? 'CHECK' : 'NOTE' }) : null)));
  }

  /** Adds the stylesheet to a shadow root or document with a constructable stylesheet, which a page's strict style policy cannot block. */
  function mountStyles(target) {
    try {
      const sheet = new CSSStyleSheet();
      sheet.replaceSync(CSS);
      target.adoptedStyleSheets = [...(target.adoptedStyleSheets || []), sheet];
    } catch (e) {
      const st = document.createElement('style');
      st.textContent = CSS;
      (target.head || target).append(st);
    }
  }

  root.AndaUI = { CSS, mountStyles, h, renderCard, renderChip, renderNotice, renderFindings, renderList, panel };
})(typeof globalThis !== 'undefined' ? globalThis : this);
