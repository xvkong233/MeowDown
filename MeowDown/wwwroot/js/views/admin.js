// 管理后台：用户管理（GET/POST/PATCH/DELETE /api/admin/users…）+ 系统配置（GET/PATCH /api/admin/config）。

import { api, errText } from '../api.js';
import { esc, toast, withLoading, fmtTs, confirmDialog, formDialog } from '../ui.js';

const WRITABLE_RANGE = {
  maxQueueLength: { min: 1, max: 10000, label: '全局等待队列上限', int: true },
  maxPendingTasksPerUser: { min: 1, max: 1000, label: '单用户等待+执行任务上限', int: true },
  maxFinishedTasks: { min: 1, max: 100000, label: '已完成任务保留条数', int: true },
  finishedTaskRetentionSeconds: { min: 3600, max: 2592000, label: '已完成任务保留秒数', int: true },
  allowRegistration: { label: '允许自助注册', bool: true },
  sessionLifetimeHours: { min: 1, max: 720, label: '会话滑动有效期（小时）', int: true },
};

const READONLY_LABEL = {
  listenUrl: '监听地址',
  downloadRoot: '下载根目录',
  webDataDir: 'Web 数据目录',
  allowAria2cArgs: '允许 aria2c 附加参数',
  allowCustomOutput: '允许自定义输出路径',
  allowCustomNetworkHosts: '允许自定义网络 Host',
  allowPrivateCallbacks: '允许回调本机/内网地址',
  version: '服务版本',
};

export function render(root, ctx) {
  root.innerHTML = `
    <div class="view-header">
      <div>
        <h1 class="view-title">管理后台</h1>
        <p class="view-sub">用户账号管理与系统运行配置（安全子集）。</p>
      </div>
      <div class="view-actions">
        <div class="tabs" style="margin:0">
          <button type="button" id="tab-users" class="active">用户管理</button>
          <button type="button" id="tab-config">系统配置</button>
        </div>
      </div>
    </div>
    <div id="admin-body"><div class="loading-inline"><span class="spinner"></span>加载中…</div></div>`;

  const body = root.querySelector('#admin-body');
  const tabUsers = root.querySelector('#tab-users');
  const tabConfig = root.querySelector('#tab-config');

  function selectTab(which) {
    tabUsers.classList.toggle('active', which === 'users');
    tabConfig.classList.toggle('active', which === 'config');
    if (which === 'users') loadUsers();
    else loadConfig();
  }
  tabUsers.addEventListener('click', () => selectTab('users'));
  tabConfig.addEventListener('click', () => selectTab('config'));

  /* ================= 用户管理 ================= */

  function userRow(u, me) {
    const isSelf = u.id === me.id;
    const roleBadge = u.role === 'admin' ? '<span class="badge accent">admin</span>' : '<span class="badge">user</span>';
    const stateBadge = u.disabled ? '<span class="badge danger">已停用</span>' : '<span class="badge ok">正常</span>';
    const bili = u.bilibiliBound
      ? `<span class="badge info" title="MID ${esc(u.bilibiliMid || '')}">已绑定：${esc(u.bilibiliUname || u.bilibiliMid || '✓')}</span>`
      : '<span class="badge">未绑定</span>';
    return `
    <tr data-user-id="${esc(u.id)}">
      <td>
        <div style="font-weight:600">${esc(u.username)}${isSelf ? ' <span class="badge info">我</span>' : ''}</div>
        <div style="font-size:12px;color:var(--text-3)">${esc(u.id)} · 创建于 ${esc(fmtTs(u.createdAt))}</div>
      </td>
      <td>${roleBadge}</td>
      <td>${stateBadge}</td>
      <td>${bili}</td>
      <td style="white-space:nowrap">待 ${Number(u.pendingTasks) || 0} / 行 ${Number(u.runningTasks) || 0}</td>
      <td>
        <div class="actions">
          <select class="role-select" data-act="role" ${u.disabled || isSelf ? 'disabled title="已停用或当前登录用户，不可在此改角色"' : ''}>
            <option value="user" ${u.role === 'user' ? 'selected' : ''}>user</option>
            <option value="admin" ${u.role === 'admin' ? 'selected' : ''}>admin</option>
          </select>
          <button class="btn ghost sm" data-act="toggle">${u.disabled ? '启用' : '停用'}</button>
          <button class="btn ghost sm" data-act="reset-pw">重置密码</button>
          <button class="btn danger sm" data-act="del" ${isSelf ? 'disabled title="不能删除自己"' : ''}>删除</button>
        </div>
      </td>
    </tr>`;
  }

  async function loadUsers() {
    body.innerHTML = `<div class="loading-inline"><span class="spinner"></span>加载中…</div>`;
    try {
      const [usersRes, me] = [await api('/api/admin/users'), ctx.getUser()];
      const users = Array.isArray(usersRes?.users) ? usersRes.users : (Array.isArray(usersRes) ? usersRes : []);
      body.innerHTML = `
        <div class="card">
          <h3 class="card-title">新建用户</h3>
          <div class="form-error" id="create-error"></div>
          <form id="create-form" autocomplete="off" style="display:grid; grid-template-columns: 1fr 1fr auto auto; gap:10px; align-items:end">
            <div class="field" style="margin:0">
              <label>用户名（3-32 位字母/数字/_/-）</label>
              <input type="text" name="username" required>
            </div>
            <div class="field" style="margin:0">
              <label>初始密码（8-128 位）</label>
              <input type="text" name="password" required autocomplete="off">
            </div>
            <div class="field" style="margin:0; min-width:100px">
              <label>角色</label>
              <select name="role"><option value="user">user</option><option value="admin">admin</option></select>
            </div>
            <button type="submit" class="btn primary" id="create-btn">创建</button>
          </form>
        </div>
        <div class="card">
          <h3 class="card-title">用户列表 <span class="badge">${users.length}</span></h3>
          <div class="table-wrap">
            <table class="table">
              <thead><tr><th>用户</th><th>角色</th><th>状态</th><th>B站绑定</th><th>任务</th><th>操作</th></tr></thead>
              <tbody>${users.map((u) => userRow(u, me)).join('')}</tbody>
            </table>
          </div>
        </div>`;

      const form = body.querySelector('#create-form');
      form.addEventListener('submit', (e) => {
        e.preventDefault();
        const errEl = body.querySelector('#create-error');
        errEl.textContent = '';
        const username = form.username.value.trim();
        const password = form.password.value;
        if (!/^[A-Za-z0-9_-]{3,32}$/.test(username)) { errEl.textContent = '用户名需为 3-32 位字母、数字、下划线或短横线'; return; }
        if (password.length < 8 || password.length > 128) { errEl.textContent = '密码长度需为 8-128 位'; return; }
        withLoading(body.querySelector('#create-btn'), async () => {
          try {
            const u = await api('/api/admin/users', { method: 'POST', body: { username, password, role: form.role.value } });
            toast(`用户 ${u.username} 创建成功`, 'success');
            loadUsers();
          } catch (err) {
            errEl.textContent = errText(err);
          }
        });
      });

      body.querySelectorAll('tr[data-user-id]').forEach((tr) => {
        const id = tr.dataset.userId;
        const user = users.find((u) => u.id === id);

        tr.querySelector('[data-act="role"]')?.addEventListener('change', async (e) => {
          const role = e.target.value;
          const ok = await confirmDialog({
            title: '修改角色',
            message: `将用户 ${user.username} 的角色改为 ${role}？若这是最后一个管理员，服务端会拒绝降级。`,
            confirmText: '确认修改',
          });
          if (!ok) { e.target.value = user.role; return; }
          try {
            await api(`/api/admin/users/${encodeURIComponent(id)}`, { method: 'PATCH', body: { role } });
            toast('角色已修改', 'success');
            loadUsers();
          } catch (err) {
            toast(errText(err), 'error');
            e.target.value = user.role;
          }
        });

        tr.querySelector('[data-act="toggle"]')?.addEventListener('click', async (e) => {
          const disable = !user.disabled;
          const ok = await confirmDialog({
            title: disable ? '停用用户' : '启用用户',
            message: disable
              ? `停用后 ${user.username} 的全部登录会话将被吊销，且无法再登录。确定吗？`
              : `重新允许 ${user.username} 登录。`,
            confirmText: disable ? '停用' : '启用',
            danger: disable,
          });
          if (!ok) return;
          await withLoading(e.currentTarget, async () => {
            try {
              await api(`/api/admin/users/${encodeURIComponent(id)}`, { method: 'PATCH', body: { disabled: disable } });
              toast(disable ? '已停用' : '已启用', 'success');
              loadUsers();
            } catch (err) {
              toast(errText(err), 'error');
            }
          });
        });

        tr.querySelector('[data-act="reset-pw"]')?.addEventListener('click', async () => {
          const values = await formDialog({
            title: `重置密码：${user.username}`,
            message: '重置后该用户全部会话被吊销，下次登录将要求修改密码。',
            fields: [{ key: 'newPassword', label: '新密码（8-128 位）', type: 'text', required: true }],
            confirmText: '重置',
          });
          if (!values) return;
          if (values.newPassword.length < 8 || values.newPassword.length > 128) {
            toast('密码长度需为 8-128 位', 'error');
            return;
          }
          try {
            await api(`/api/admin/users/${encodeURIComponent(id)}/password`, { method: 'POST', body: { newPassword: values.newPassword } });
            toast('密码已重置', 'success');
          } catch (err) {
            toast(errText(err), 'error');
          }
        });

        tr.querySelector('[data-act="del"]')?.addEventListener('click', async (e) => {
          const ok = await confirmDialog({
            title: '删除用户',
            message: `将永久删除用户 ${user.username}：其B站绑定凭据一并删除、全部会话吊销；已下载文件保留在磁盘。确定删除吗？`,
            confirmText: '删除',
            danger: true,
          });
          if (!ok) return;
          await withLoading(e.currentTarget, async () => {
            try {
              await api(`/api/admin/users/${encodeURIComponent(id)}`, { method: 'DELETE', body: {} });
              toast('用户已删除', 'success');
              loadUsers();
            } catch (err) {
              toast(errText(err), 'error');
            }
          });
        });
      });
    } catch (err) {
      body.innerHTML = `<div class="empty"><span class="big">😿</span>加载失败：${esc(errText(err))}</div>`;
    }
  }

  /* ================= 系统配置 ================= */

  async function loadConfig() {
    body.innerHTML = `<div class="loading-inline"><span class="spinner"></span>加载中…</div>`;
    let config;
    try {
      config = await api('/api/admin/config');
    } catch (err) {
      body.innerHTML = `<div class="empty"><span class="big">😿</span>加载失败：${esc(errText(err))}</div>`;
      return;
    }
    const w = config.writable || {};
    const ro = config.readOnly || {};

    const numberField = (key) => {
      const r = WRITABLE_RANGE[key];
      const val = w[key];
      return `
      <div class="field" data-key="${key}">
        <label>${esc(r.label)} <code class="inline">${key}</code></label>
        <input type="number" name="${key}" value="${Number(val) ?? ''}" min="${r.min}" max="${r.max}" step="1">
        <div class="hint">允许范围：${r.min} – ${r.max}</div>
      </div>`;
    };
    const boolField = (key) => `
      <label class="checkbox-row" data-key="${key}" style="align-self:end; padding-bottom:10px">
        <input type="checkbox" name="${key}" ${w[key] ? 'checked' : ''}>
        <span class="lbl">${esc(WRITABLE_RANGE[key].label)} <code class="inline">${key}</code></span>
      </label>`;

    body.innerHTML = `
      <div class="card">
        <h3 class="card-title">运行时配置（可修改）</h3>
        <p class="card-hint">仅安全子集允许在运行时修改；范围校验失败会被服务端拒绝。部分项（如会话有效期）对已有会话在下次续期时生效。</p>
        <div class="form-error" id="config-error"></div>
        <form id="config-form">
          <div class="config-grid">
            ${numberField('maxQueueLength')}
            ${numberField('maxPendingTasksPerUser')}
            ${numberField('maxFinishedTasks')}
            ${numberField('finishedTaskRetentionSeconds')}
            ${numberField('sessionLifetimeHours')}
            ${boolField('allowRegistration')}
          </div>
          <div style="margin-top:10px">
            <button type="submit" class="btn primary" id="config-save">保存修改</button>
          </div>
        </form>
      </div>
      <div class="card">
        <h3 class="card-title">启动期配置（只读）</h3>
        <p class="card-hint">以下为启动期安全边界，需修改启动参数 / 配置文件并重启服务。</p>
        <div class="ro-grid">
          ${Object.entries(READONLY_LABEL).map(([k, label]) => ro[k] !== undefined ? `
          <div class="ro-item"><span class="ro-key">${esc(label)} <code class="inline">${k}</code></span>
          <span class="ro-val">${esc(typeof ro[k] === 'boolean' ? (ro[k] ? '开' : '关') : ro[k])}</span></div>` : '').join('')}
        </div>
      </div>`;

    body.querySelector('#config-form').addEventListener('submit', (e) => {
      e.preventDefault();
      const errEl = body.querySelector('#config-error');
      errEl.textContent = '';
      const form = e.target;
      const patch = {};
      for (const key of Object.keys(WRITABLE_RANGE)) {
        const r = WRITABLE_RANGE[key];
        if (r.bool) {
          const v = form[key].checked;
          if (v !== !!w[key]) patch[key] = v;
        } else {
          const input = form[key];
          const raw = input.value.trim();
          if (raw === '') { errEl.textContent = `${r.label} 不能为空`; return; }
          const n = Number(raw);
          if (!Number.isInteger(n) || n < r.min || n > r.max) {
            errEl.textContent = `${r.label} 需为 ${r.min} – ${r.max} 之间的整数`;
            return;
          }
          if (n !== Number(w[key])) patch[key] = n;
        }
      }
      if (Object.keys(patch).length === 0) {
        toast('没有需要保存的修改', 'info');
        return;
      }
      withLoading(body.querySelector('#config-save'), async () => {
        try {
          await api('/api/admin/config', { method: 'PATCH', body: patch });
          toast('配置已保存', 'success');
          loadConfig();
        } catch (err) {
          errEl.textContent = errText(err);
        }
      });
    });
  }

  selectTab('users');
}
