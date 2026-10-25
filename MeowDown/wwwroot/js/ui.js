// 通用 UI 工具：转义、格式化、Toast、确认/输入弹窗。

export const esc = (s) => String(s ?? '').replace(/[&<>"']/g, (c) => ({
  '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;',
}[c]));

export function fmtBytes(n) {
  const v = Number(n);
  if (!Number.isFinite(v) || v < 0) return '—';
  if (v < 1024) return `${Math.round(v)} B`;
  const units = ['KB', 'MB', 'GB', 'TB'];
  let x = v / 1024, i = 0;
  while (x >= 1024 && i < units.length - 1) { x /= 1024; i++; }
  return `${x.toFixed(x >= 100 ? 0 : 1)} ${units[i]}`;
}

export const fmtSpeed = (n) => (Number(n) > 0 ? `${fmtBytes(n)}/s` : '—');

export function fmtTs(sec) {
  const n = Number(sec);
  if (!Number.isFinite(n) || n <= 0) return '—';
  const ms = n < 1e12 ? n * 1000 : n; // 兼容秒/毫秒时间戳
  return new Date(ms).toLocaleString('zh-CN', { hour12: false });
}

export function fmtDur(sec) {
  const n = Math.floor(Number(sec));
  if (!Number.isFinite(n) || n < 0) return '';
  const h = Math.floor(n / 3600), m = Math.floor((n % 3600) / 60), s = n % 60;
  const mm = String(m).padStart(2, '0'), ss = String(s).padStart(2, '0');
  return h > 0 ? `${h}:${mm}:${ss}` : `${m}:${ss}`;
}

/* ---------- Toast ---------- */
export function toast(message, type = 'info', timeoutMs = 3500) {
  const root = document.getElementById('toast-root');
  if (!root) return;
  const node = document.createElement('div');
  node.className = `toast ${type}`;
  node.textContent = message;
  root.appendChild(node);
  setTimeout(() => {
    node.classList.add('hide');
    setTimeout(() => node.remove(), 320);
  }, timeoutMs);
}

/* ---------- 弹窗 ---------- */
function buildModal(innerHtml) {
  const root = document.getElementById('modal-root');
  const overlay = document.createElement('div');
  overlay.className = 'modal-overlay';
  overlay.innerHTML = `<div class="modal">${innerHtml}</div>`;
  root.appendChild(overlay);
  const close = () => overlay.remove();
  return { overlay, close };
}

/** 确认弹窗，resolve(true/false)。 */
export function confirmDialog({ title = '确认操作', message = '', confirmText = '确认', cancelText = '取消', danger = false }) {
  return new Promise((resolve) => {
    const { overlay, close } = buildModal(`
      <h3 class="modal-title">${esc(title)}</h3>
      <p class="modal-msg">${esc(message)}</p>
      <div class="modal-actions">
        <button class="btn ghost" data-act="cancel">${esc(cancelText)}</button>
        <button class="btn ${danger ? 'danger' : 'primary'}" data-act="ok">${esc(confirmText)}</button>
      </div>`);
    const done = (val) => { close(); resolve(val); };
    overlay.addEventListener('click', (e) => {
      if (e.target === overlay) done(false);
      const act = e.target.closest('[data-act]')?.dataset.act;
      if (act === 'ok') done(true);
      if (act === 'cancel') done(false);
    });
    const onKey = (e) => { if (e.key === 'Escape') { done(false); document.removeEventListener('keydown', onKey); } };
    document.addEventListener('keydown', onKey);
    overlay.querySelector('[data-act="ok"]')?.focus();
  });
}

/**
 * 带输入的弹窗。fields: [{ key, label, type='text', placeholder, value, required, hint }]
 * resolve(值对象 | null)。
 */
export function formDialog({ title, message = '', fields = [], confirmText = '确认', cancelText = '取消' }) {
  return new Promise((resolve) => {
    const fieldsHtml = fields.map((f) => `
      <div class="field">
        <label>${esc(f.label)}</label>
        <input type="${esc(f.type || 'text')}" name="${esc(f.key)}" placeholder="${esc(f.placeholder || '')}"
          value="${esc(f.value ?? '')}" autocomplete="off">
        ${f.hint ? `<div class="hint">${esc(f.hint)}</div>` : ''}
      </div>`).join('');
    const { overlay, close } = buildModal(`
      <h3 class="modal-title">${esc(title)}</h3>
      ${message ? `<p class="modal-msg">${esc(message)}</p>` : ''}
      <div class="modal-error form-error"></div>
      <form>${fieldsHtml}
        <div class="modal-actions">
          <button type="button" class="btn ghost" data-act="cancel">${esc(cancelText)}</button>
          <button type="submit" class="btn primary">${esc(confirmText)}</button>
        </div>
      </form>`);
    const done = (val) => { close(); resolve(val); };
    overlay.addEventListener('click', (e) => {
      if (e.target === overlay) done(null);
      if (e.target.closest('[data-act="cancel"]')) done(null);
    });
    overlay.querySelector('form').addEventListener('submit', (e) => {
      e.preventDefault();
      const values = {};
      for (const f of fields) {
        const input = overlay.querySelector(`input[name="${f.key}"]`);
        const v = (input?.value ?? '').trim();
        if (f.required && !v) {
          overlay.querySelector('.modal-error').textContent = `请填写${f.label}`;
          input?.focus();
          return;
        }
        values[f.key] = v;
      }
      done(values);
    });
    overlay.querySelector('input')?.focus();
  });
}

/** 按钮加载态包装：执行 async 操作期间禁用按钮并显示转圈。 */
export async function withLoading(btn, fn) {
  if (!btn || btn.disabled) return;
  const origHtml = btn.innerHTML;
  btn.disabled = true;
  btn.innerHTML = `<span class="spinner"></span>${btn.textContent ? esc(btn.textContent.trim()) : ''}`;
  try {
    return await fn();
  } finally {
    btn.disabled = false;
    btn.innerHTML = origHtml;
  }
}
