// Exports one screen of docs/research/ui_mockups (adopted.html, served over http) as measured elements for Unity.
// Run in the browser console on adopted.html: await exportScreen(figureIndex, 'name') -> POSTs name.json (+ pngs) to
// http://127.0.0.1:8766 (a tiny receiver that writes files). Coordinates are 1920x1080 stage pixels, top-left origin.
window.exportScreen = async function (idx, name, opts) {
  opts = opts || {};
  const fig = document.querySelectorAll('figure')[idx];
  const st = fig.querySelector('.stage');
  const sr = st.getBoundingClientRect();
  const k = sr.width / 1920;
  const cs0 = getComputedStyle(st);
  const vars = {};
  ['--bg', '--sf', '--ln', '--ac', '--tx', '--t2', '--ok', '--wr', '--acs2', '--acs', '--t1', '--r'].forEach(v => vars[v] = cs0.getPropertyValue(v).trim());
  const items = [];
  const post = (n, body) => fetch('http://127.0.0.1:8766/' + n, { method: 'POST', body });
  const rgba = c => { const m = c.match(/[\d.]+/g); if (!m) return null; const a = m.length > 3 ? +m[3] : 1; return a === 0 ? null : [+m[0], +m[1], +m[2], Math.round(a * 255)]; };
  const R = r => [Math.round((r.left - sr.left) / k * 10) / 10, Math.round((r.top - sr.top) / k * 10) / 10, Math.round(r.width / k * 10) / 10, Math.round(r.height / k * 10) / 10];
  const fix = (x, col) => x.replace(/currentColor/g, col || '#e6ecf2').replace(/var\((--[a-z0-9]+)\)/g, (m, v) => vars[v] || col || '#e6ecf2');
  let pngN = 0;
  const raster = async (svg, w, h, key) => {
    const c2 = svg.cloneNode(true);
    const col = getComputedStyle(svg.parentElement || svg).color;
    let x = fix(new XMLSerializer().serializeToString(c2), col);
    x = x.replace(/<svg([^>]*)>/, (m, a) => '<svg' + a.replace(/ style="[^"]*"/, '').replace(/ (width|height)="[^"]*"/g, '') + ` width="${w}" height="${h}">`);
    const im = new Image(); im.src = 'data:image/svg+xml;charset=utf-8,' + encodeURIComponent(x);
    try { await im.decode(); } catch (e) { return null; }
    const c = document.createElement('canvas'); c.width = w; c.height = h; c.getContext('2d').drawImage(im, 0, 0, w, h);
    const fn = name + '_' + key + '.png';
    await post('scr_' + fn, c.toDataURL('image/png'));
    return fn;
  };
  // Background art (hall etc.), rasterised whole.
  const art = opts.root ? null : st.querySelector('.art svg');
  if (art) { const fn = await raster(art, 1920, 1080, 'bg'); if (fn) items.push({ k: 'img', src: fn, r: [0, 0, 1920, 1080] }); }
  const walk = async el => {
    if (el.closest('.art')) return;
    const cs = getComputedStyle(el);
    if (cs.display === 'none' || cs.visibility === 'hidden' || +cs.opacity === 0) return;
    const tag = el.tagName.toLowerCase();
    const rect = el.getBoundingClientRect();
    if (tag === 'svg') {
      if (rect.width > 1 && rect.height > 1) {
        const w = Math.max(2, Math.round(rect.width / k)), h = Math.max(2, Math.round(rect.height / k));
        const sc = w < 120 ? 4 : 1;
        const fn = await raster(el, w * sc, h * sc, 'i' + (pngN++));
        if (fn) items.push({ k: 'img', src: fn, r: R(rect) });
      }
      return;
    }
    if (el.matches('.stage')) { /* root */ } else if (rect.width > 0.5 && rect.height > 0.5) {
      const bg = rgba(cs.backgroundColor);
      const gm = cs.backgroundImage.match(/linear-gradient\(([^)]*\)[^)]*)\)/);
      const bw = parseFloat(cs.borderTopWidth) || 0, bc = rgba(cs.borderTopColor);
      const sameB = ['Right', 'Bottom', 'Left'].every(s => parseFloat(cs['border' + s + 'Width']) === bw);
      const rad = cs.borderTopLeftRadius;
      const radPx = rad.endsWith('%') ? Math.min(rect.width, rect.height) / k / 2 : Math.min(parseFloat(rad) || 0, Math.min(rect.width, rect.height) / k / 2);
      let bar = null;
      const sh = cs.boxShadow.match(/(rgba?\([^)]*\))\s+0px\s+(-?\d+)px\s+0px\s+0px\s+inset/);
      if (sh && +sh[2] < 0) bar = [-sh[2], rgba(sh[1])];
      const grad = gm ? [...cs.backgroundImage.matchAll(/rgba?\([^)]*\)/g)].map(m => rgba(m[0]) || [0, 0, 0, 0]) : null;
      if (bg || grad || (bw > 0 && sameB && bc) || bar) {
        const act = el.getAttribute('data-act') || el.getAttribute('data-go') || null;
        items.push({ k: 'box', r: R(rect), bg, gt: grad && grad.length >= 2 ? grad[0] : null, gb: grad && grad.length >= 2 ? grad[grad.length - 1] : null, bw: sameB && bc ? bw : 0, bc: sameB ? bc : null, rad: Math.round(radPx), barH: bar ? bar[0] : 0, barC: bar ? bar[1] : null,
          id: el.getAttribute('data-fid') || el.id || null, act, go: el.getAttribute('data-go'), btn: tag === 'button' || el.hasAttribute('data-act') || el.hasAttribute('data-go'), dis: el.disabled || el.hasAttribute('disabled') || el.getAttribute('aria-disabled') === 'true' });
      } else if (tag === 'button' || el.hasAttribute('data-act') || el.hasAttribute('data-go')) {
        items.push({ k: 'box', r: R(rect), bg: null, gt: null, gb: null, bw: 0, bc: null, rad: 0, barH: 0, barC: null, id: el.getAttribute('data-fid') || el.id || null, act: el.getAttribute('data-act') || el.getAttribute('data-go'), go: el.getAttribute('data-go'), btn: true, dis: el.disabled });
      }
    }
    for (const n of el.childNodes) {
      if (n.nodeType === 3) {
        const t = n.textContent.replace(/\s+/g, ' ').trim();
        if (!t) continue;
        const range = document.createRange(); range.selectNodeContents(n);
        const rs = [...range.getClientRects()].filter(r => r.width > 0.5);
        if (!rs.length) continue;
        const u = { left: Math.min(...rs.map(r => r.left)), top: Math.min(...rs.map(r => r.top)), right: Math.max(...rs.map(r => r.right)), bottom: Math.max(...rs.map(r => r.bottom)) };
        const lines = new Set(rs.map(r => Math.round(r.top))).size;
        items.push({ k: 'text', t, r: R({ left: u.left, top: u.top, width: u.right - u.left, height: u.bottom - u.top }), fs: parseFloat(cs.fontSize), fw: +cs.fontWeight, col: rgba(cs.color),
          al: cs.textAlign, wrap: lines > 1, mono: /mono/i.test(cs.fontFamily), lines });
      } else if (n.nodeType === 1) await walk(n);
    }
  };
  await walk(opts.root ? st.querySelector(opts.root) : st);
  const json = JSON.stringify({ name, w: 1920, h: 1080, items });
  await post(name + '.json', json);
  return { items: items.length, boxes: items.filter(i => i.k === 'box').length, texts: items.filter(i => i.k === 'text').length, imgs: items.filter(i => i.k === 'img').length, buttons: items.filter(i => i.btn).length, bytes: json.length };
};
