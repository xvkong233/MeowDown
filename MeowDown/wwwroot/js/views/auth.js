// 登录 / 注册页（无壳渲染，直接占据整个 #app）。

import { api, errText } from '../api.js';
import { esc, toast, withLoading } from '../ui.js';

function template(mode) {
  const isLogin = mode === 'login';
  return `
  <div class="auth-wrap">
    <div class="auth-card">
      <div class="auth-brand"><span class="cat">🐱</span> MeowDown</div>
      <div class="auth-sub">B 站视频解析与多用户下载服务</div>
      <div class="tabs" id="auth-tabs">
        <button type="button" data-mode="login" class="${isLogin ? 'active' : ''}">登录</button>
        <button type="button" data-mode="register" class="${isLogin ? '' : 'active'}">注册</button>
      </div>
      <div class="form-error" id="auth-error"></div>
      <form id="auth-form" autocomplete="off">
        <div class="field">
          <label>用户名</label>
          <input type="text" name="username" placeholder="3-32 位字母、数字、_ 或 -" required autofocus>
        </div>
        <div class="field">
          <label>密码</label>
          <input type="password" name="password" placeholder="8-128 位" required>
        </div>
        ${isLogin ? '' : `
        <div class="field">
          <label>确认密码</label>
          <input type="password" name="password2" placeholder="再次输入密码" required>
        </div>`}
        <button type="submit" class="btn primary block" id="auth-submit">${isLogin ? '登 录' : '注 册'}</button>
      </form>
      <div class="auth-switch" id="auth-switch">
        ${isLogin
          ? '没有账号？<a href="#/register">注册新账号</a>'
          : '已有账号？<a href="#/login">直接登录</a>'}
      </div>
    </div>
  </div>`;
}

export function render(root, { mode = 'login', setUser, onDone }) {
  root.innerHTML = template(mode);
  const errorEl = root.querySelector('#auth-error');
  const form = root.querySelector('#auth-form');

  root.querySelectorAll('#auth-tabs button').forEach((btn) => {
    btn.addEventListener('click', () => {
      const target = btn.dataset.mode === mode ? null : `#${btn.dataset.mode}`;
      if (target) location.hash = target;
    });
  });

  root.querySelector('#auth-switch a')?.addEventListener('click', (e) => {
    e.preventDefault();
    const target = e.currentTarget.getAttribute('href');
    if (location.hash === target) render(root, { mode: target === '#/register' ? 'register' : 'login', setUser, onDone });
    else location.hash = target;
  });

  form.addEventListener('submit', (e) => {
    e.preventDefault();
    errorEl.textContent = '';
    const username = form.username.value.trim();
    const password = form.password.value;
    if (!/^[A-Za-z0-9_-]{3,32}$/.test(username)) {
      errorEl.textContent = '用户名需为 3-32 位字母、数字、下划线或短横线';
      return;
    }
    if (password.length < 8 || password.length > 128) {
      errorEl.textContent = '密码长度需为 8-128 位';
      return;
    }
    if (mode !== 'login' && password !== form.password2.value) {
      errorEl.textContent = '两次输入的密码不一致';
      return;
    }

    withLoading(root.querySelector('#auth-submit'), async () => {
      try {
        if (mode === 'login') {
          const user = await api('/api/auth/login', { method: 'POST', body: { username, password } });
          setUser(user);
          toast(`欢迎回来，${user.username}`, 'success');
          onDone();
        } else {
          await api('/api/auth/register', { method: 'POST', body: { username, password } });
          toast('注册成功，请登录', 'success');
          if (location.hash === '#/login') {
            render(root, { mode: 'login', setUser, onDone });
            const input = root.querySelector('input[name="username"]');
            if (input) input.value = username;
          } else {
            location.hash = '#/login';
          }
        }
      } catch (err) {
        errorEl.textContent = errText(err);
      }
    });
  });
}
