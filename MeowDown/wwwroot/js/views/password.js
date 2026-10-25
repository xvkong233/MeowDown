// 修改自己的密码（POST /api/auth/password，成功后服务端吊销其他会话）。

import { api, errText } from '../api.js';
import { esc, toast, withLoading } from '../ui.js';

export function render(root, ctx) {
  const { getUser, setUser, navigate } = ctx;
  const forced = !!getUser()?.passwordChangeRequired;

  root.innerHTML = `
    <div class="view-header">
      <div>
        <h1 class="view-title">修改密码</h1>
        <p class="view-sub">修改成功后，你在其他设备上的登录会话将被吊销。</p>
      </div>
    </div>
    ${forced ? `<div class="banner">⚠️ <span>当前使用的是初始密码（首次登录或已被管理员重置），请设置新密码后继续使用。</span></div>` : ''}
    <div class="card" style="max-width: 460px;">
      <div class="form-error" id="pw-error"></div>
      <form id="pw-form" autocomplete="off">
        <div class="field">
          <label>当前密码</label>
          <input type="password" name="current" required>
        </div>
        <div class="field">
          <label>新密码</label>
          <input type="password" name="next" placeholder="8-128 位" required>
          <div class="hint">长度 8-128 位。</div>
        </div>
        <div class="field">
          <label>确认新密码</label>
          <input type="password" name="next2" required>
        </div>
        <button type="submit" class="btn primary" id="pw-submit">确认修改</button>
      </form>
    </div>`;

  const errorEl = root.querySelector('#pw-error');
  const form = root.querySelector('#pw-form');

  form.addEventListener('submit', (e) => {
    e.preventDefault();
    errorEl.textContent = '';
    const currentPassword = form.current.value;
    const newPassword = form.next.value;
    if (newPassword.length < 8 || newPassword.length > 128) {
      errorEl.textContent = '新密码长度需为 8-128 位';
      return;
    }
    if (newPassword !== form.next2.value) {
      errorEl.textContent = '两次输入的新密码不一致';
      return;
    }
    withLoading(root.querySelector('#pw-submit'), async () => {
      try {
        await api('/api/auth/password', { method: 'POST', body: { currentPassword, newPassword } });
        const user = getUser();
        if (user) setUser({ ...user, passwordChangeRequired: false });
        toast('密码修改成功', 'success');
        navigate('#/download');
      } catch (err) {
        errorEl.textContent = err.code === 'invalid_credentials' ? '当前密码错误' : errText(err);
      }
    });
  });
}
