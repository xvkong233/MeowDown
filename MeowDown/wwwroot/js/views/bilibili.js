// B站账号绑定：POST /api/bilibili/qr/start → 展示服务端二维码图 → GET /api/bilibili/qr/{bindId} 轮询。

import { api, errText } from '../api.js';
import { esc, toast, withLoading, fmtTs, confirmDialog } from '../ui.js';

const POLL_MS = 2000;

const STATUS_TEXT = {
  waiting: '等待扫码…请使用B站 App 扫描二维码',
  scanned: '已扫码，请在手机上确认登录',
};

export function render(root, ctx) {
  const { getUser, setUser } = ctx;

  root.innerHTML = `
    <div class="view-header">
      <div>
        <h1 class="view-title">B站账号绑定</h1>
        <p class="view-sub">绑定后，你的解析与下载任务将使用自己账号的登录凭据，可解锁更高清晰度。</p>
      </div>
    </div>
    <div id="bili-content"><div class="loading-inline"><span class="spinner"></span>加载中…</div></div>`;

  const content = root.querySelector('#bili-content');
  let pollTimer = null;
  let countdownTimer = null;
  let currentBindId = null;

  function stopTimers() {
    if (pollTimer) clearInterval(pollTimer);
    if (countdownTimer) clearInterval(countdownTimer);
    pollTimer = countdownTimer = null;
  }

  function setBoundFlag(bound) {
    const user = getUser();
    if (user && user.bilibiliBound !== bound) {
      setUser({ ...user, bilibiliBound: bound });
    }
  }

  /* ---------- 已绑定视图 ---------- */
  async function loadBindings() {
    stopTimers();
    try {
      const b = await api('/api/bilibili/bindings');
      setBoundFlag(!!b.bound);
      if (b.bound) {
        content.innerHTML = `
          <div class="card" style="max-width: 560px;">
            <h3 class="card-title">✅ 已绑定B站账号</h3>
            <dl class="bind-info">
              <dt>昵称</dt><dd>${esc(b.uname || '—')}</dd>
              <dt>MID</dt><dd>${esc(b.mid || '—')}</dd>
              <dt>绑定时间</dt><dd>${esc(fmtTs(b.boundAt))}</dd>
            </dl>
            <div style="margin-top:16px">
              <button class="btn danger" id="unbind-btn">解除绑定</button>
            </div>
          </div>`;
        content.querySelector('#unbind-btn').addEventListener('click', (e) => withLoading(e.currentTarget, async () => {
          const ok = await confirmDialog({
            title: '解除B站绑定',
            message: '解除后将删除服务器上保存的你的B站登录凭据，下载任务恢复为未登录状态（清晰度受限）。确定解绑吗？',
            confirmText: '解绑',
            danger: true,
          });
          if (!ok) return;
          try {
            await api('/api/bilibili/bindings', { method: 'DELETE', body: {} });
            setBoundFlag(false);
            toast('已解除绑定', 'success');
            loadBindings();
          } catch (err) {
            toast(errText(err), 'error');
          }
        }));
      } else {
        renderIdle();
      }
    } catch (err) {
      content.innerHTML = `<div class="empty"><span class="big">😿</span>加载失败：${esc(errText(err))}
        <div style="margin-top:10px"><button class="btn ghost sm" id="retry-load">重试</button></div></div>`;
      content.querySelector('#retry-load')?.addEventListener('click', loadBindings);
    }
  }

  /* ---------- 未绑定：发起扫码 ---------- */
  function renderIdle() {
    content.innerHTML = `
      <div class="card" style="max-width: 560px;">
        <h3 class="card-title">尚未绑定</h3>
        <p class="card-hint" style="margin-bottom:14px">扫码登录你的B站账号。凭据仅保存在本服务器、与你的账号一一对应；解绑即删除。</p>
        <button class="btn primary" id="start-btn">📱 扫码绑定B站账号</button>
        <div class="form-error" id="start-error" style="margin-top:12px"></div>
      </div>`;
    content.querySelector('#start-btn').addEventListener('click', (e) => withLoading(e.currentTarget, async () => {
      content.querySelector('#start-error').textContent = '';
      try {
        const s = await api('/api/bilibili/qr/start', { method: 'POST', body: {} });
        renderQr(s);
      } catch (err) {
        content.querySelector('#start-error').textContent = errText(err);
      }
    }));
  }

  /* ---------- 扫码中视图 ---------- */
  function renderQr(session) {
    stopTimers();
    currentBindId = session.bindId;
    const expiresAtMs = (Number(session.expiresAt) || 0) * 1000;

    content.innerHTML = `
      <div class="card" style="max-width: 560px;">
        <h3 class="card-title">扫码绑定</h3>
        <div class="qr-stage">
          <div class="qr-frame">
            <img id="qr-img" alt="B站登录二维码" src="/api/bilibili/qr/${encodeURIComponent(session.bindId)}/image">
            <div class="qr-mask" id="qr-mask" hidden></div>
          </div>
          <div class="qr-status" id="qr-status"><span class="dot run"></span>${esc(STATUS_TEXT.waiting)}</div>
          <div class="qr-countdown" id="qr-countdown"></div>
          <div class="view-actions">
            <button class="btn ghost sm" id="qr-refresh" hidden>重新获取二维码</button>
          </div>
          <div class="form-error" id="qr-error" style="margin:0"></div>
        </div>
      </div>`;

    const statusEl = content.querySelector('#qr-status');
    const maskEl = content.querySelector('#qr-mask');
    const errorEl = content.querySelector('#qr-error');
    const refreshBtn = content.querySelector('#qr-refresh');
    const countdownEl = content.querySelector('#qr-countdown');
    content.querySelector('#qr-img').addEventListener('error', () => {
      errorEl.textContent = '二维码图片加载失败，请重新获取';
    });

    function setTerminalUi(html, { showRefresh = true } = {}) {
      stopTimers();
      statusEl.innerHTML = html;
      maskEl.hidden = false;
      maskEl.innerHTML = html;
      refreshBtn.hidden = !showRefresh;
      countdownEl.textContent = '';
    }

    refreshBtn.addEventListener('click', (e) => withLoading(e.currentTarget, async () => {
      errorEl.textContent = '';
      try {
        const s = await api('/api/bilibili/qr/start', { method: 'POST', body: {} });
        renderQr(s);
      } catch (err) {
        errorEl.textContent = errText(err);
      }
    }));

    // 倒计时
    countdownTimer = setInterval(() => {
      const left = Math.max(0, Math.round((expiresAtMs - Date.now()) / 1000));
      countdownEl.textContent = left > 0 ? `二维码约 ${Math.floor(left / 60)}分${String(left % 60).padStart(2, '0')}秒 后过期` : '';
      if (left <= 0) clearInterval(countdownTimer);
    }, 1000);

    // 轮询绑定状态
    pollTimer = setInterval(async () => {
      if (document.hidden || !currentBindId) return;
      try {
        const st = await api(`/api/bilibili/qr/${encodeURIComponent(currentBindId)}`);
        if (st.status === 'waiting' || st.status === 'scanned') {
          statusEl.innerHTML = `<span class="dot ${st.status === 'scanned' ? 'ok' : 'run'}"></span>${esc(STATUS_TEXT[st.status])}`;
        } else if (st.status === 'success') {
          setTerminalUi(`🎉 绑定成功：${esc(st.uname || '')}`, { showRefresh: false });
          currentBindId = null;
          setBoundFlag(true);
          toast(`B站账号绑定成功：${st.uname || ''}`, 'success');
          setTimeout(loadBindings, 1200);
        } else if (st.status === 'expired') {
          setTerminalUi('⌛ 二维码已过期，请重新获取');
          currentBindId = null;
        } else if (st.status === 'failed') {
          setTerminalUi(`❌ 绑定失败：${esc(st.message || '未知错误')}`);
          currentBindId = null;
        }
      } catch (err) {
        if (err.status === 404) {
          setTerminalUi('绑定会话已失效，请重新获取二维码');
          currentBindId = null;
        }
        // 其他瞬时错误：下个周期重试
      }
    }, POLL_MS);
  }

  loadBindings();

  return function destroy() {
    stopTimers();
    currentBindId = null;
  };
}
