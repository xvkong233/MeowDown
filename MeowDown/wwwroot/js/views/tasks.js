// 任务列表：GET /api/tasks 轮询（2.5s），展示等待/进行中/已完成与进度。

import { api, errText } from '../api.js';
import { esc, toast, withLoading, fmtBytes, fmtSpeed, fmtTs, confirmDialog } from '../ui.js';

const POLL_MS = 2500;

// 契约新增 ownerId/ownerName 为 camelCase，其余沿用既有 DownloadTask 的 PascalCase 字段。
const tid = (t) => t.TaskId ?? t.taskId ?? '';
const list = (data, key) => data?.[key] ?? data?.[key[0].toUpperCase() + key.slice(1)] ?? [];

function statusBadge(t, phase) {
  if (phase === 'finished') {
    return t.IsSuccessful
      ? '<span class="badge ok"><span class="dot ok"></span>成功</span>'
      : '<span class="badge danger"><span class="dot err"></span>失败</span>';
  }
  if (phase === 'running') return '<span class="badge info"><span class="dot run"></span>下载中</span>';
  return '<span class="badge warn"><span class="dot pend"></span>等待中</span>';
}

function taskCard(t, phase, { showOwner, onDelete }) {
  const progressPct = Math.max(0, Math.min(100, Math.round((Number(t.Progress) || 0) * 100)));
  const isRunning = phase === 'running';
  const finished = phase === 'finished';
  const speed = isRunning ? fmtSpeed(t.DownloadSpeed) : (finished && t.DownloadSpeed > 0 ? `平均 ${fmtSpeed(t.DownloadSpeed)}` : '');
  const savePaths = Array.isArray(t.SavePaths) && t.SavePaths.length
    ? `<div class="save-paths"><span class="sp-title">输出文件（${t.SavePaths.length}）</span>
         <ul>${t.SavePaths.map((p) => `<li>${esc(p)}</li>`).join('')}</ul></div>`
    : '';
  return `
  <div class="task-card" data-task-id="${esc(tid(t))}">
    <div class="task-head">
      ${statusBadge(t, phase)}
      <span class="task-title" title="${esc(t.Title || t.Url || tid(t))}">${esc(t.Title || t.Url || tid(t))}</span>
      ${showOwner ? `<span class="badge">${esc(t.ownerName || t.ownerId || '?')}</span>` : ''}
      ${finished && !onDelete ? '' : ''}
    </div>
    <div class="task-meta">
      ${t.Aid ? `<span>ID: ${esc(t.Aid)}</span>` : ''}
      <span>提交于 ${esc(fmtTs(t.TaskCreateTime))}</span>
      ${t.TaskFinishTime ? `<span>完成于 ${esc(fmtTs(t.TaskFinishTime))}</span>` : ''}
      ${Number(t.TotalDownloadedBytes) > 0 ? `<span>已下载 ${esc(fmtBytes(t.TotalDownloadedBytes))}</span>` : ''}
      ${speed ? `<span>${esc(speed)}</span>` : ''}
    </div>
    ${!finished ? `
    <div class="progress-row">
      <div class="progress ${isRunning ? 'run' : ''}"><div class="fill" style="width:${isRunning ? progressPct : Math.min(8, progressPct)}%"></div></div>
      <span class="pct">${isRunning ? progressPct + '%' : '排队中'}</span>
    </div>` : ''}
    ${t.Error ? `<div class="task-error">✕ ${esc(t.Error)}</div>` : ''}
    ${savePaths}
    ${finished && onDelete ? `<div class="task-meta" style="margin-top:8px"><button class="btn ghost sm del-btn" data-task-id="${esc(tid(t))}">删除记录</button></div>` : ''}
  </div>`;
}

export function render(root, ctx) {
  const { getUser } = ctx;
  const isAdmin = getUser()?.role === 'admin';

  root.innerHTML = `
    <div class="view-header">
      <div>
        <h1 class="view-title">任务列表</h1>
        <p class="view-sub">每 ${POLL_MS / 1000} 秒自动刷新；任务保存在内存中，服务重启后清空。</p>
      </div>
      <div class="view-actions">
        ${isAdmin ? `<label class="checkbox-row"><input type="checkbox" id="scope-all"><span class="lbl">查看全部用户任务</span></label>` : ''}
        <button class="btn ghost sm" id="refresh-btn">刷新</button>
        <button class="btn danger sm" id="clear-btn">清空已完成</button>
      </div>
    </div>
    <div id="tasks-error"></div>
    <div id="tasks-list"><div class="loading-inline"><span class="spinner"></span>加载中…</div></div>`;

  const listEl = root.querySelector('#tasks-list');
  const errorEl = root.querySelector('#tasks-error');
  const scopeBox = root.querySelector('#scope-all');
  let timer = null;
  let loading = false;

  async function refresh(showError = false) {
    if (loading) return;
    loading = true;
    try {
      const scope = scopeBox?.checked ? '?scope=all' : '';
      const data = await api(`/api/tasks${scope}`);
      const pending = list(data, 'pending');
      const running = list(data, 'running');
      const finished = list(data, 'finished');
      const showOwner = !!scopeBox?.checked;
      const activeHtml = [...running, ...pending].map((t) => taskCard(t, running.includes(t) ? 'running' : 'pending', { showOwner })).join('');
      const finishedHtml = finished.map((t) => taskCard(t, 'finished', {
        showOwner,
        onDelete: true,
      })).join('');

      listEl.innerHTML = `
        <div class="section-label">进行中 / 等待中 <span class="count">${running.length + pending.length} 个</span></div>
        ${activeHtml || `<div class="empty"><span class="big">🍃</span>暂无进行中的任务</div>`}
        <div class="section-label">已完成 <span class="count">${finished.length} 个</span></div>
        ${finishedHtml || `<div class="empty"><span class="big">📭</span>暂无已完成任务</div>`}`;
      errorEl.innerHTML = '';
      bindDeleteButtons();
    } catch (err) {
      if (showError) errorEl.innerHTML = `<div class="form-error">${esc(errText(err))}</div>`;
    } finally {
      loading = false;
    }
  }

  function bindDeleteButtons() {
    listEl.querySelectorAll('.del-btn').forEach((btn) => {
      btn.addEventListener('click', async () => {
        const id = btn.dataset.taskId;
        try {
          await api(`/api/tasks/finished/${encodeURIComponent(id)}`, { method: 'DELETE', body: {} });
          toast('已删除该记录', 'success');
          refresh();
        } catch (err) {
          toast(errText(err), 'error');
        }
      });
    });
  }

  root.querySelector('#refresh-btn').addEventListener('click', () => refresh(true));
  scopeBox?.addEventListener('change', () => refresh(true));
  root.querySelector('#clear-btn').addEventListener('click', (e) => withLoading(e.currentTarget, async () => {
    const scope = scopeBox?.checked;
    const ok = await confirmDialog({
      title: '清空已完成任务',
      message: scope ? '将删除所有用户的已完成任务记录（不影响已下载的文件），确定吗？' : '将删除你的全部已完成任务记录（不影响已下载的文件），确定吗？',
      confirmText: '清空',
      danger: true,
    });
    if (!ok) return;
    try {
      await api(`/api/tasks/finished${scope ? '?scope=all' : ''}`, { method: 'DELETE', body: {} });
      toast('已清空', 'success');
      refresh();
    } catch (err) {
      toast(errText(err), 'error');
    }
  }));

  timer = setInterval(() => { if (!document.hidden) refresh(false); }, POLL_MS);
  refresh(true);

  return function destroy() {
    if (timer) clearInterval(timer);
    timer = null;
  };
}
