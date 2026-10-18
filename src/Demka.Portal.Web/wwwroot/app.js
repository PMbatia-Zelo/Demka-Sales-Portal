/* Demka Sales Capability Portal — phone app shell.
   No inline handlers or styles (the Content-Security-Policy forbids them): events use data-action, widths are set from data-w. */
'use strict';

const S = { me: null, data: null, tab: 'home', outletId: null, cachedAt: null, admin: { tab: 'users', users: null, q: '', pin: null, summary: null, uploads: null, unmapped: null, months: null, outlets: null, last: null, busy: false } };
const CACHE_KEY = 'demka.targets.v1';
const $ = (sel) => document.querySelector(sel);

// ---------- helpers ----------
const esc = (s) => String(s ?? '').replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
function kes(v, d) {
  v = Number(v) || 0; const a = Math.abs(v);
  if (a >= 1e6) return 'KES ' + (v / 1e6).toFixed(d ?? 2) + 'M';
  if (a >= 1e3) return 'KES ' + (v / 1e3).toFixed(d ?? 0) + 'k';
  return 'KES ' + Math.round(v);
}
const k = (v, d) => kes(v, d).replace('KES ', '');
const pct = (v) => Math.round((v || 0) * 100) + '%';
const MONTHS = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];
const monthName = (iso) => MONTHS[Number(String(iso).slice(5, 7)) - 1] || '';
const monthLong = (iso) => new Date(String(iso).slice(0, 10) + 'T00:00:00').toLocaleString('en-GB', { month: 'long' });
const daysIn = (iso) => { const d = new Date(String(iso).slice(0, 7) + '-01T00:00:00'); return new Date(d.getFullYear(), d.getMonth() + 1, 0).getDate(); };
function toast(msg) { const t = $('#toast'); t.textContent = msg; t.hidden = false; clearTimeout(toast.h); toast.h = setTimeout(() => (t.hidden = true), 2600); }
function meter(frac, mark) {
  return `<div class="meter" role="img" aria-label="${pct(Math.min(frac, 1))} achieved"><i data-w="${Math.min(100, Math.max(0, frac * 100)).toFixed(1)}"></i>${mark != null ? `<b data-l="${(mark * 100).toFixed(1)}"></b>` : ''}</div>`;
}
function applyWidths() {
  document.querySelectorAll('[data-w]').forEach((el) => (el.style.width = el.dataset.w + '%'));
  document.querySelectorAll('[data-l]').forEach((el) => (el.style.left = el.dataset.l + '%'));
}
function paceStatus(r) { if (r >= 1) return ['On track', 'p-good']; if (r >= 0.9) return ['Close', 'p-warn']; return ['Behind', 'p-bad']; }

async function api(path, opts = {}) {
  const res = await fetch(path, {
    method: opts.method || 'GET',
    credentials: 'same-origin',
    headers: opts.body ? { 'Content-Type': 'application/json' } : {},
    body: opts.body ? JSON.stringify(opts.body) : undefined
  });
  let body = null;
  try { body = await res.json(); } catch { /* empty or not JSON */ }
  if (!res.ok) { const e = new Error(body?.error || `Request failed (${res.status})`); e.status = res.status; throw e; }
  return body;
}
function saveCache(data) { try { localStorage.setItem(CACHE_KEY, JSON.stringify({ savedAt: new Date().toISOString(), data })); } catch { } }
function loadCache() { try { return JSON.parse(localStorage.getItem(CACHE_KEY) || 'null'); } catch { return null; } }
function clearCache() { try { localStorage.removeItem(CACHE_KEY); } catch { } }

// ---------- period helpers ----------
function currentMonthIndex(d) {
  if (!d?.months?.length) return 0;
  const now = new Date(); const ym = now.getFullYear() * 12 + now.getMonth();
  const idx = d.months.findIndex((m) => Number(m.slice(0, 4)) * 12 + Number(m.slice(5, 7)) - 1 === ym);
  if (idx >= 0) return idx;
  const first = Number(d.months[0].slice(0, 4)) * 12 + Number(d.months[0].slice(5, 7)) - 1;
  return ym < first ? 0 : d.months.length - 1;
}
/** Share of the month covered by uploaded sales, or null when nothing has been uploaded for that month. */
function elapsedShare(d, i) {
  const m = d.months[i]; const thru = d.actualsThrough;
  if (!thru) return null;
  if (thru.slice(0, 7) > m.slice(0, 7)) return 1;
  if (thru.slice(0, 7) < m.slice(0, 7)) return null;
  return Number(thru.slice(8, 10)) / daysIn(m);
}

// ---------- boot ----------
async function boot() {
  if ('serviceWorker' in navigator) navigator.serviceWorker.register('/sw.js').catch(() => { });
  try {
    S.me = await api('/api/me');
  } catch (e) {
    if (e.status === 401) return render();
    const c = loadCache(); // offline: show what we had
    if (c) { S.me = c.data.holder; S.data = c.data; S.cachedAt = c.savedAt; return render(); }
    return renderError('Could not reach the portal. Check your connection and try again.');
  }
  await loadData();
}
async function loadData() {
  if (!S.me || S.me.mustChangePin) return render();
  if (S.me.role === 'NationalAdmin') { S.tab = 'users'; await loadUsers(); return render(); }
  try {
    S.data = await api('/api/me/targets'); S.cachedAt = null; saveCache(S.data);
  } catch (e) {
    if (e.status === 401) { S.me = null; return render(); }
    const c = loadCache(); if (c) { S.data = c.data; S.cachedAt = c.savedAt; } else return renderError(e.message);
  }
  render();
}

// ---------- render ----------
function render() {
  const app = $('#app'), tb = $('#tabbar');
  app.className = 'shell' + (S.me?.role === 'NationalAdmin' && !S.me.mustChangePin ? ' wide' : '');
  if (!S.me) { tb.hidden = true; app.innerHTML = loginView(); return focusFirst(); }
  if (S.me.mustChangePin) { tb.hidden = true; app.innerHTML = changePinView(true); return focusFirst(); }
  if (S.me.role === 'NationalAdmin') { tb.hidden = true; app.innerHTML = adminView(); applyWidths(); return; }

  app.innerHTML = (S.cachedAt ? `<div class="banner">You're offline. Showing figures saved ${esc(new Date(S.cachedAt).toLocaleString('en-GB', { dateStyle: 'medium', timeStyle: 'short' }))}.</div>` : '')
    + (S.outletId ? outletDetail() : S.tab === 'outlets' ? outletsView() : S.tab === 'account' ? accountView() : homeView());
  const tabs = [['home', 'My targets', ICON.home], ['outlets', 'Outlets', ICON.shop], ['account', 'Account', ICON.user]];
  $('#tabs').innerHTML = tabs.map(([id, l, ic]) => `<button class="tab${S.tab === id && !S.outletId ? ' on' : ''}" data-action="tab" data-tab="${id}">${ic}<span>${l}</span></button>`).join('');
  tb.hidden = false;
  applyWidths();
}
function renderError(msg) { $('#app').innerHTML = `<div class="card"><h2>Something went wrong</h2><p class="muted">${esc(msg)}</p><button class="btn primary" data-action="reload">Try again</button></div>`; }
function focusFirst() { const el = document.querySelector('#app input'); if (el) el.focus(); }

const ICON = {
  home: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" aria-hidden="true"><path d="M3 11l9-7 9 7v9a1 1 0 0 1-1 1h-5v-6H9v6H4a1 1 0 0 1-1-1z"/></svg>',
  shop: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" aria-hidden="true"><path d="M4 9h16l-1-5H5zM5 9v11h14V9M9 20v-6h6v6"/></svg>',
  user: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" aria-hidden="true"><circle cx="12" cy="8" r="4"/><path d="M4 21c0-4 4-6 8-6s8 2 8 6"/></svg>'
};

function loginView() {
  return `<div class="login">
    <div class="brand"><div class="logo">D</div><div><h1>Sales Capability Portal</h1><div class="muted small">Demka Dairy · Modern Trade</div></div></div>
    <form class="card" data-form="login" novalidate>
      <label class="f" for="phone">Phone number<input id="phone" name="phone" type="tel" inputmode="tel" autocomplete="tel" placeholder="07XX XXX XXX" required></label>
      <label class="f" for="pin">PIN<input id="pin" name="pin" type="password" inputmode="numeric" autocomplete="current-password" maxlength="6" placeholder="4–6 digits" required></label>
      <div class="error" id="err" hidden></div>
      <button class="btn primary" type="submit">Sign in</button>
      <div class="small muted">Forgot your PIN? Ask your team leader or the Modern Trade office to reset it.</div>
    </form>
  </div>`;
}
function changePinView(first) {
  return `<div class="login">
    <div><div class="eyebrow">${first ? 'First sign-in' : 'Account'}</div><h1>${first ? 'Choose your own PIN' : 'Change your PIN'}</h1></div>
    <form class="card" data-form="pin" novalidate>
      ${first ? '<div class="small muted">Replace the PIN you were given with one only you know. Use 4 to 6 digits, not 1234 or 1111.</div>' : ''}
      <label class="f" for="cur">${first ? 'PIN you were given' : 'Current PIN'}<input id="cur" name="cur" type="password" inputmode="numeric" maxlength="6" autocomplete="current-password" required></label>
      <label class="f" for="new1">New PIN<input id="new1" name="new1" type="password" inputmode="numeric" maxlength="6" autocomplete="new-password" required></label>
      <label class="f" for="new2">New PIN again<input id="new2" name="new2" type="password" inputmode="numeric" maxlength="6" autocomplete="new-password" required></label>
      <div class="error" id="err" hidden></div>
      <button class="btn primary" type="submit">Save PIN</button>
      ${first ? '<button class="btn link" type="button" data-action="logout">Sign out</button>' : '<button class="btn" type="button" data-action="tab" data-tab="account">Cancel</button>'}
    </form>
  </div>`;
}

function homeView() {
  const d = S.data;
  if (!d || !d.outlets?.length) {
    return `<div><div class="eyebrow">${esc(S.me.region || '')}</div><h1>Hello, ${esc(S.me.name.split(' ')[0])}</h1></div>
      <section class="card"><h2>No outlets yet</h2><p class="muted">You don't hold any outlet targets in the published targets${d?.cycle ? ' for ' + esc(d.cycle) : ''}. ${S.me.role === 'TeamLeader' ? 'The team view for team leaders arrives in the next update.' : 'Ask your team leader to check your outlet allocation.'}</p></section>`;
  }
  const i = currentMonthIndex(d), m = d.totals.months[i], share = elapsedShare(d, i);
  const first = esc(S.me.name.split(' ')[0]), mon = monthLong(d.months[i]);
  let hero;
  if (m.actual == null || share == null) {
    hero = `<section class="card hero">
      <div class="eyebrow">${mon} target</div><div class="big num">${kes(m.target)}</div>
      <div class="small">No ${mon} sales have been uploaded yet. Your progress will show here after the next upload.</div>
      <div class="grid3">${d.totals.months.map((x, j) => `<div class="tile"><span class="l">${monthName(d.months[j])}</span><span class="v num">${k(x.target)}</span></div>`).join('')}</div>
    </section>`;
  } else {
    const ratio = m.target ? m.actual / (m.target * share) : 0, [st, cl] = paceStatus(ratio);
    const daysLeft = Math.max(1, Math.round(daysIn(d.months[i]) * (1 - share)));
    hero = `<section class="card hero">
      <div class="row"><div><div class="eyebrow">${mon} to date</div><div class="big num">${kes(m.actual)}</div></div><span class="pill">${st}</span></div>
      ${meter(m.target ? m.actual / m.target : 0, share)}
      <div class="small">${pct(m.target ? m.actual / m.target : 0)} of your ${mon} target with ${pct(share)} of the month in the figures. Sales to ${esc(new Date(d.actualsThrough + 'T00:00:00').toLocaleDateString('en-GB', { day: 'numeric', month: 'short' }))}.</div>
      <div class="grid3">
        <div class="tile"><span class="l">${monthName(d.months[i])} target</span><span class="v num">${k(m.target)}</span></div>
        <div class="tile"><span class="l">Needed per day</span><span class="v num">${k(Math.max(0, m.target - m.actual) / daysLeft)}</span></div>
        <div class="tile"><span class="l">${esc(d.cycle)} target</span><span class="v num">${k(d.totals.q4Target)}</span></div>
      </div>
    </section>`;
  }
  const vsQ3 = d.totals.q3Actual ? d.totals.q4Target / d.totals.q3Actual - 1 : 0;
  const ranked = d.outlets.slice().sort((a, b) => outletPace(a, i, share) - outletPace(b, i, share));
  return `<div class="top"><div><div class="eyebrow">${esc(S.me.region || '')} · ${d.outlets.length} outlets</div><h1>Hello, ${first}</h1></div><span class="pill p-info">Targets v${d.version}</span></div>
    ${hero}
    <section class="card"><div class="row"><h2>${esc(d.cycle)}</h2><span class="small muted">KES incl. VAT</span></div>
      <div class="small muted">Your ${esc(d.cycle)} target is ${kes(d.totals.q4Target)}, ${vsQ3 >= 0 ? '+' : ''}${(vsQ3 * 100).toFixed(1)}% on your Q3 sales of ${kes(d.totals.q3Actual)}.</div></section>
    <section class="card"><div class="row"><h2>${share == null ? 'Biggest targets' : 'Needs attention'}</h2><span class="small muted">${share == null ? 'largest first' : 'lowest pace first'}</span></div>
      <div class="list">${(share == null ? d.outlets.slice(0, 4) : ranked.slice(0, 4)).map((o) => outletRow(o, i, share)).join('')}</div>
      <button class="btn" data-action="tab" data-tab="outlets">See all ${d.outlets.length} outlets</button></section>`;
}
function outletPace(o, i, share) { const m = o.months[i]; if (!share || !m.target) return 0; return (m.actual || 0) / (m.target * share); }
function outletRow(o, i, share) {
  const m = o.months[i];
  const tag = o.recovery ? '<span class="pill p-butter">Recovery</span>' : o.status === 'New' ? '<span class="pill p-info">New</span>' : '';
  if (share == null || m.actual == null) {
    return `<button class="item" data-action="outlet" data-id="${o.id}"><div class="row"><span class="name">${esc(o.name)}</span>${tag}</div>
      <div class="row small muted"><span class="num">${monthName(S.data.months[i])} target ${k(m.target)}</span><span class="num">Q4 ${k(o.q4Target)}</span></div></button>`;
  }
  const [st, cl] = paceStatus(outletPace(o, i, share));
  return `<button class="item" data-action="outlet" data-id="${o.id}"><div class="row"><span class="name">${esc(o.name)}</span><span class="pill ${cl}">${st}</span></div>
    ${meter(m.target ? m.actual / m.target : 0, share)}
    <div class="row small muted"><span class="num">${k(m.actual)} of ${k(m.target)}</span><span>${tag}</span></div></button>`;
}
function outletsView() {
  const d = S.data; if (!d?.outlets?.length) return homeView();
  const i = currentMonthIndex(d), share = elapsedShare(d, i);
  return `<div><div class="eyebrow">${d.outlets.length} outlets · largest Q4 target first</div><h1>My outlets</h1></div>
    <section class="card"><div class="list">${d.outlets.map((o) => outletRow(o, i, share)).join('')}</div></section>`;
}
function outletDetail() {
  const d = S.data, o = d.outlets.find((x) => x.id === S.outletId); if (!o) { S.outletId = null; return homeView(); }
  const i = currentMonthIndex(d), m = o.months[i], share = elapsedShare(d, i);
  const progress = (m.actual == null || share == null)
    ? `<div class="small muted">No ${monthLong(d.months[i])} sales uploaded yet.</div>`
    : `${meter(m.target ? m.actual / m.target : 0, share)}<div class="grid3"><div class="tile"><span class="l">Actual</span><span class="v num">${k(m.actual)}</span></div><div class="tile"><span class="l">Target</span><span class="v num">${k(m.target)}</span></div><div class="tile"><span class="l">Gap</span><span class="v num">${k(Math.max(0, m.target - m.actual))}</span></div></div>`;
  return `<button class="btn link" data-action="back">← Back</button>
    <div><div class="eyebrow">${esc(o.chain)} · ${esc(o.region)}</div><h1>${esc(o.name)}</h1>${o.route ? `<div class="small muted">${esc(o.route)}</div>` : ''}</div>
    <section class="card"><div class="row"><h2>${monthLong(d.months[i])}</h2><span class="small num">Target ${kes(m.target)}</span></div>${progress}</section>
    <section class="card chart"><div class="row"><h2>Sales and targets</h2><span class="small muted">KES per month</span></div>${chart(o, d)}
      <div class="small muted">Grey: sales Jan–Sep (September provisional). Outlined: ${esc(d.cycle)} targets. Filled: sales so far.</div></section>
    <section class="card"><h2>How your target is built</h2><table><tbody>
      <tr><td>Baseline sales per month</td><td class="r">${kes(o.baselinePerMonth)}</td></tr>
      <tr><td>Growth</td><td class="r">+${Math.round(o.growth * 100)}%</td></tr>
      ${o.recovery ? `<tr><td>Recovery (half the way back to your best quarter)</td><td class="r">+${kes(o.recoveryUpliftPerMonth)}/mo</td></tr>` : ''}
      <tr><td><b>${esc(d.cycle)} target</b></td><td class="r"><b>${kes(o.q4Target)}</b></td></tr></tbody></table></section>`;
}
function chart(o, d) {
  const hist = o.history.map((h) => h.amount), tg = o.months.map((m) => m.target), ac = o.months.map((m) => m.actual);
  const vals = [...hist, ...tg], mx = Math.max(1, ...vals, ...ac.map((a) => a || 0)) * 1.1;
  const W = 360, H = 170, L = 40, B = 24, T = 10, n = vals.length, bw = (W - L - 8) / n;
  const y = (v) => T + (H - T - B) * (1 - Math.max(0, v) / mx);
  let s = `<svg viewBox="0 0 ${W} ${H}" role="img" aria-label="Monthly sales and targets for ${esc(o.name)}">`;
  [0, mx / 2.2, mx / 1.1].forEach((t) => { s += `<line x1="${L}" x2="${W - 4}" y1="${y(t)}" y2="${y(t)}" stroke="var(--line)"/><text x="${L - 6}" y="${y(t) + 4}" text-anchor="end" font-size="10" fill="var(--muted)">${k(t)}</text>`; });
  const labels = [...o.history.map((h) => h.month), ...d.months];
  vals.forEach((v, j) => {
    const bx = L + 4 + j * bw, w = bw - 5;
    if (j < hist.length) s += `<rect x="${bx}" y="${y(v)}" width="${w}" height="${y(0) - y(v)}" rx="2" fill="var(--bar)"/>`;
    else {
      s += `<rect x="${bx + 0.75}" y="${y(v)}" width="${w - 1.5}" height="${y(0) - y(v)}" rx="2" fill="none" stroke="var(--accent)" stroke-width="1.5"/>`;
      const a = ac[j - hist.length]; if (a) s += `<rect x="${bx}" y="${y(a)}" width="${w}" height="${y(0) - y(a)}" rx="2" fill="var(--accent)"/>`;
    }
    s += `<text x="${bx + w / 2}" y="${H - 8}" text-anchor="middle" font-size="10" fill="var(--muted)">${monthName(labels[j])[0]}</text>`;
  });
  return s + '</svg>';
}
function accountView() {
  return `<div><div class="eyebrow">${esc(S.me.role === 'TeamLeader' ? 'Team leader' : 'Merchandiser')} · ${esc(S.me.region || '')}</div><h1>${esc(S.me.name)}</h1></div>
    <section class="card"><button class="btn" data-action="changepin">Change my PIN</button><button class="btn" data-action="logout">Sign out</button>
      <div class="small muted">Signing out also removes the figures saved on this phone.</div></section>`;
}

// ---------- admin ----------
async function loadUsers() {
  try {
    [S.admin.users, S.admin.summary] = await Promise.all([api('/api/admin/users'), api('/api/admin/targets/summary').catch(() => null)]);
  } catch (e) { if (e.status === 401) S.me = null; else toast(e.message); }
}
async function loadAdminData() {
  try {
    const [uploads, unmapped, months, outlets] = await Promise.all([
      api('/api/admin/uploads'), api('/api/admin/unmapped'), api('/api/admin/months'),
      S.admin.outlets ? Promise.resolve(S.admin.outlets) : api('/api/admin/outlets')]);
    Object.assign(S.admin, { uploads, unmapped, months, outlets });
    if (!S.admin.last) S.admin.last = uploads.find((u) => u.type === 'LedgerEntries')?.summary || null;
  } catch (e) { if (e.status === 401) S.me = null; else toast(e.message); }
}
function adminView() {
  const tabs = [['users', 'Users'], ['data', 'Data uploads']];
  return `<div class="top"><div><div class="eyebrow">National Modern Trade</div><h1>Admin panel</h1></div><button class="btn small" data-action="logout">Sign out</button></div>
    <div class="subnav" role="tablist">${tabs.map(([id, l]) => `<button class="tab${S.admin.tab === id ? ' on' : ''}" role="tab" aria-selected="${S.admin.tab === id}" data-action="admintab" data-tab="${id}">${l}</button>`).join('')}</div>
    ${S.admin.tab === 'data' ? dataView() : usersView()}`;
}
const monthLabel = (iso) => new Date(String(iso).slice(0, 10) + 'T00:00:00').toLocaleString('en-GB', { month: 'short', year: 'numeric' });
function dataView() {
  const A = S.admin, months = A.months || [];
  if (!A.months) return '<div class="loading">Loading…</div>';
  const firstOpen = months.find((m) => !m.closed)?.month || months[0]?.month;
  const last = A.last;
  const resultCard = last ? `<section class="card"><div class="row"><h2>Last upload</h2><span class="small muted">${esc(last.file || '')}</span></div>
      <div class="small muted">${Number(last.rowsRead).toLocaleString()} rows read · ${Number(last.invoiceAndCreditLines).toLocaleString()} invoice and credit lines · entries ${esc(last.firstDate)} to ${esc(last.lastDate)} · replaced ${monthLabel(last.refreshFrom)} to ${monthLabel(last.refreshedTo)}</div>
      <div class="tablewrap"><table><thead><tr><th>Month</th><th class="r">Outlets with sales</th><th class="r">Modern Trade taxable (KES)</th><th>Status</th></tr></thead><tbody>
      ${(last.months || []).map((m) => `<tr><td>${monthLabel(m.month)}</td><td class="r">${m.outlets}</td><td class="r">${kes(m.amount)}</td><td>${m.provisional ? '<span class="pill p-warn">Provisional</span>' : '<span class="pill p-good">Closed</span>'}</td></tr>`).join('')}
      </tbody></table></div>
      <div class="small">${last.reviewNames ? `<b>${last.reviewNames} Modern Trade names</b> (${kes(last.reviewAmount)}) could not be placed on an outlet: see below.` : 'Every Modern Trade name was placed on an outlet.'}
        ${last.newAutomaticLinks ? ` ${last.newAutomaticLinks} new names were linked automatically by outlet name.` : ''}</div>
      ${last.rejected ? `<details><summary class="small">${last.rejected} rows skipped</summary><ul class="small">${(last.rejectedExamples || []).map((r) => `<li>Row ${r.row}: ${esc(r.reason)}</li>`).join('')}</ul></details>` : ''}
    </section>` : '';
  const un = A.unmapped || [];
  const reviewCard = `<section class="card"><div class="row"><h2>Names to review</h2><span class="small muted">${un.length} names · ${kes(un.reduce((s, u) => s + u.amount, 0))}</span></div>
    <div class="small muted">Sales the last upload couldn't place. Link each name to its outlet (its sales move there straight away), or ignore names that aren't Modern Trade.</div>
    <datalist id="outletlist">${(A.outlets || []).map((o) => `<option value="${esc(o.name)} · ${esc(o.region)} #${o.id}"></option>`).join('')}</datalist>
    ${un.length ? `<div class="tablewrap"><table><thead><tr><th>Name on invoice</th><th>Account</th><th class="r">Sales</th><th>Link to outlet</th><th></th></tr></thead><tbody>
    ${un.slice(0, 100).map((u, i) => `<tr><td>${esc(u.nameKey)}<div class="small muted">${esc(u.postingGroup || '')}</div></td><td>${esc(u.customerNo)}<div class="small muted">${esc(u.customerName || '')}</div></td><td class="r">${kes(u.amount)}</td>
      <td><form class="inline" data-form="link" data-i="${i}"><input type="search" name="outlet" list="outletlist" placeholder="Type outlet name" aria-label="Outlet for ${esc(u.nameKey)}"><button class="btn small primary" type="submit">Link</button></form></td>
      <td class="r"><button class="btn small" data-action="ignore" data-i="${i}">Not MT</button></td></tr>`).join('')}
    </tbody></table></div>` : '<div class="small">Nothing to review.</div>'}</section>`;
  const monthsCard = `<section class="card"><h2>Months</h2>
    <div class="small muted">Close a month after the day-5 ledger close; merchandisers then see its figures as final. You can reopen it if late postings arrive.</div>
    <div class="tablewrap"><table><thead><tr><th>Month</th><th class="r">Outlets</th><th class="r">Sales uploaded</th><th>Status</th><th></th></tr></thead><tbody>
    ${months.map((m) => `<tr><td>${monthLabel(m.month)}</td><td class="r">${m.outlets}</td><td class="r">${kes(m.amount)}</td><td>${m.closed ? '<span class="pill p-good">Closed</span>' : '<span class="pill p-warn">Provisional</span>'}</td>
      <td class="r"><button class="btn small" data-action="month" data-month="${String(m.month).slice(0, 7)}" data-do="${m.closed ? 'reopen' : 'close'}">${m.closed ? 'Reopen' : 'Close month'}</button></td></tr>`).join('')}
    </tbody></table></div></section>`;
  return `<section class="card"><h2>Upload Business Central ledger</h2>
      <div class="small muted">Customer Ledger Entries export, .xlsx or .csv, including at least the months you refresh. Uploading again replaces those months, so a corrected file is always safe.</div>
      <form data-form="ledger" class="formgrid" novalidate>
        <label class="f" for="lf">File<input id="lf" name="file" type="file" accept=".xlsx,.csv" required></label>
        <label class="f" for="rf">Replace sales from<select id="rf" name="refreshFrom">${months.map((m) => `<option value="${String(m.month).slice(0, 7)}"${m.month === firstOpen ? ' selected' : ''}>${monthLabel(m.month)}</option>`).join('')}</select></label>
        <div class="f"><span>&nbsp;</span><button class="btn primary" type="submit"${A.busy ? ' disabled' : ''}>${A.busy ? 'Reading file…' : 'Upload and refresh'}</button></div>
      </form>
      ${A.busy ? '<div class="banner">Reading the file. A full 40 MB export takes about a minute; keep this page open.</div>' : ''}
      <div class="error" id="err" hidden></div>
    </section>${resultCard}${reviewCard}${monthsCard}`;
}
function usersView() {
  const users = (S.admin.users || []).filter((u) => !S.admin.q || (u.name + ' ' + (u.region || '') + ' ' + u.role).toLowerCase().includes(S.admin.q.toLowerCase()));
  const sum = S.admin.summary;
  const statusPill = (s) => `<span class="pill ${s === 'Active' ? 'p-good' : s === 'Locked' ? 'p-bad' : s === 'Not issued' ? 'p-mute' : 'p-warn'}">${esc(s)}</span>`;
  return `${S.admin.pin ? `<section class="card"><div class="row"><h2>New PIN for ${esc(S.admin.pin.name)}</h2><button class="btn small" data-action="dismisspin">Done</button></div>
      <div class="pinbox num">${esc(S.admin.pin.temporaryPin)}</div>
      <div class="small muted">Give this PIN to ${esc(S.admin.pin.name.split(' ')[0])} by phone or in person. It is shown only once; they must choose their own PIN when they first sign in.</div></section>` : ''}
    ${sum ? `<section class="card"><div class="row"><h2>${esc(sum.cycle)} targets · v${sum.version}</h2><span class="num">${kes(sum.total, 1)}</span></div>
      <div class="grid4">${sum.regions.map((r) => `<div class="tile"><span class="l">${esc(r.region)}</span><span class="v num">${k(r.target, 1)}</span></div>`).join('')}</div></section>` : ''}
    <section class="card"><div class="row"><h2>Users</h2><span class="small muted">${users.length} of ${(S.admin.users || []).length}</span></div>
      <label class="f" for="uq">Search<input id="uq" type="search" value="${esc(S.admin.q)}" placeholder="Name, region or role" data-action="usersearch"></label>
      <div class="tablewrap"><table><thead><tr><th>Name</th><th>Role</th><th>Region</th><th class="r">Outlets</th><th>Phone</th><th>PIN</th><th></th></tr></thead><tbody>
      ${users.map((u) => `<tr><td>${esc(u.name)}</td><td>${esc(u.role)}</td><td>${esc(u.region || '–')}</td><td class="r">${u.outlets}</td>
        <td><form class="inline" data-form="phone" data-id="${u.id}"><input type="tel" name="phone" aria-label="Phone for ${esc(u.name)}" value="${esc(u.phone ? '0' + u.phone.slice(4) : '')}" placeholder="07XX XXX XXX"><button class="btn small" type="submit">Save</button></form></td>
        <td>${statusPill(u.pinStatus)}</td>
        <td class="r"><button class="btn small" data-action="resetpin" data-id="${u.id}"${u.phone ? '' : ' disabled title="Set a phone number first"'}>${u.pinStatus === 'Not issued' ? 'Issue PIN' : 'Reset PIN'}</button></td></tr>`).join('')}
      </tbody></table></div></section>`;
}

// ---------- events ----------
document.addEventListener('click', async (e) => {
  const el = e.target.closest('[data-action]'); if (!el || el.tagName === 'INPUT') return;
  const a = el.dataset.action;
  if (a === 'tab') { S.tab = el.dataset.tab; S.outletId = null; render(); window.scrollTo(0, 0); }
  else if (a === 'outlet') { S.outletId = Number(el.dataset.id); render(); window.scrollTo(0, 0); }
  else if (a === 'back') { S.outletId = null; render(); }
  else if (a === 'reload') { location.reload(); }
  else if (a === 'changepin') { $('#app').innerHTML = changePinView(false); $('#tabbar').hidden = true; focusFirst(); }
  else if (a === 'logout') { try { await api('/api/auth/logout', { method: 'POST' }); } catch { } clearCache(); S.me = null; S.data = null; S.tab = 'home'; render(); }
  else if (a === 'admintab') { S.admin.tab = el.dataset.tab; render(); if (S.admin.tab === 'data') { await loadAdminData(); render(); } }
  else if (a === 'ignore') {
    const u = S.admin.unmapped[Number(el.dataset.i)]; el.disabled = true;
    try { await api('/api/admin/mappings', { method: 'POST', body: { customerNo: u.customerNo, nameKey: u.nameKey, ignore: true } }); toast('Marked as not Modern Trade'); await loadAdminData(); render(); }
    catch (err) { toast(err.message); el.disabled = false; }
  }
  else if (a === 'month') {
    el.disabled = true;
    try { await api(`/api/admin/months/${el.dataset.month}/${el.dataset.do}`, { method: 'POST' }); toast(el.dataset.do === 'close' ? 'Month closed' : 'Month reopened'); await loadAdminData(); render(); }
    catch (err) { toast(err.message); el.disabled = false; }
  }
  else if (a === 'dismisspin') { S.admin.pin = null; render(); }
  else if (a === 'resetpin') {
    el.disabled = true;
    try { S.admin.pin = await api(`/api/admin/users/${el.dataset.id}/reset-pin`, { method: 'POST' }); await loadUsers(); render(); window.scrollTo(0, 0); }
    catch (err) { toast(err.message); el.disabled = false; }
  }
});
document.addEventListener('input', (e) => {
  if (e.target.dataset.action === 'usersearch') {
    S.admin.q = e.target.value; clearTimeout(S.admin.t);
    S.admin.t = setTimeout(() => { render(); const f = $('#uq'); f.focus(); f.setSelectionRange(f.value.length, f.value.length); }, 250);
  }
});
document.addEventListener('submit', async (e) => {
  const f = e.target.closest('form[data-form]'); if (!f) return; e.preventDefault();
  const err = f.querySelector('#err'); const showErr = (m) => { if (err) { err.textContent = m; err.hidden = false; } else toast(m); };
  const btn = f.querySelector('button[type=submit]'); if (btn) btn.disabled = true;
  try {
    if (f.dataset.form === 'login') {
      S.me = await api('/api/auth/login', { method: 'POST', body: { phone: f.phone.value, pin: f.pin.value } });
      await loadData();
    } else if (f.dataset.form === 'pin') {
      if (f.new1.value !== f.new2.value) throw new Error('The two new PINs are not the same.');
      S.me = await api('/api/auth/change-pin', { method: 'POST', body: { currentPin: f.cur.value, newPin: f.new1.value } });
      toast('PIN saved'); S.tab = 'home'; await loadData();
    } else if (f.dataset.form === 'ledger') {
      const file = f.file.files[0]; if (!file) throw new Error('Choose the ledger file first.');
      const fd = new FormData(); fd.append('file', file); fd.append('refreshFrom', f.refreshFrom.value);
      S.admin.busy = true; render();
      try {
        const res = await fetch('/api/admin/uploads/ledger', { method: 'POST', body: fd, credentials: 'same-origin' });
        let body = null; try { body = await res.json(); } catch { }
        if (!res.ok) throw new Error(body?.error || (res.status === 413 ? 'The file is too large.' : `Upload failed (${res.status})`));
        S.admin.last = body; toast('Sales refreshed');
      } finally { S.admin.busy = false; }
      await loadAdminData(); render(); return;
    } else if (f.dataset.form === 'link') {
      const u = S.admin.unmapped[Number(f.dataset.i)];
      const m = /#(\d+)$/.exec(f.outlet.value.trim());
      if (!m) throw new Error('Pick an outlet from the list.');
      const r = await api('/api/admin/mappings', { method: 'POST', body: { customerNo: u.customerNo, nameKey: u.nameKey, outletId: Number(m[1]) } });
      toast(`Linked; ${kes(r.movedAmount)} moved to the outlet`); await loadAdminData(); render(); return;
    } else if (f.dataset.form === 'phone') {
      await api(`/api/admin/users/${f.dataset.id}/phone`, { method: 'PUT', body: { phone: f.phone.value } });
      toast('Phone number saved'); await loadUsers(); render();
    }
  } catch (ex) {
    showErr(ex.status === 429 ? 'Too many attempts. Wait a minute and try again.' : ex.message);
  } finally { if (btn && document.body.contains(btn)) btn.disabled = false; }
});

boot();
