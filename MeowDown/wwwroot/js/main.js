// 应用入口：会话探测、路由、页面壳与导航。

import { api, setUnauthorizedHandler, errText } from './api.js';
import { esc, toast } from './ui.js';
import * as auth from './views/auth.js';
import * as password from './views/password.js';
import * as download from './views/download.js';
import * as tasks from './views/tasks.js';
import * as bilibili from './views/bilibili.js';
import * as admin from './views/admin.js';

const state = {
  user: null,          // { id, username, role, passwordChangeRequired, bilibiliBound }
  currentCleanup: null,
  currentRoute: null,
};

const routes = {
  '/download': { view: download, title: '解析下载', nav: true },
  '/tasks': { view: tasks, title: '任务列表', nav: true },
  '/bilibili': { view: bilibili, title: 'B站绑定', nav: true },
  '/password': { view: password, title: '修改密码', nav: true },
  '/admin': { view: admin, title: '管理后台', nav: true, adminOnly: true },
  '/login': { auth: auth, mode: 'login', public: true },
  '/register': { auth: auth, mode: 'register', public: true },
};

const app = document.getElementById('app');

function getUser() { return state.user; }

function setUser(user) {
  state.user = user;
  if (user) {
    try { sessionStorage.setItem('meowdown.user', JSON.stringify(user)); }
    catch { /* 忽略存储失败 */ }
  } else {
    try { sessionStorage.removeItem('meowdown.user'); } catch { /* 同上 */ }
  }
}

function navigate(hash) {
  if (location.hash === hash) route();
  else location.hash = hash;
}

function buildShell() {
  app.innerHTML = `
    <header class="topbar">
      <div class="topbar-inner container">
        <a class="brand" href="#/download"><span class="cat">🐱</span>MeowDown</a>
        <nav class="nav" id="main-nav"></nav>
        <div class="userbox">
          <span class="user-chip"><span id="nav-username"></span><span id="nav-role"></span></span>
          <button class="btn ghost sm" id="logout-btn">退出登录</button>
        </div>
      </div>
    </header>
    <main class="container" id="view-root"></main>`;

  document.getElementById('logout-btn').addEventListener('click', async (e) => {
    const btn = e.currentTarget;
    btn.disabled = true;
    try {
      await api('/api/auth/logout', { method: 'POST', body: {} });
    } catch { /* 幂等，忽略错误 */ }
    btn.disabled = false;
    setUser(null);
    toast('已退出登录', 'success');
    navigate('#/login');
  });
}

function renderNav() {
  const nav = document.getElementById('main-nav');
  const usernameEl = document.getElementById('nav-username');
  const roleEl = document.getElementById('nav-role');
  if (!nav) return;
  const user = getUser();
  const items = Object.entries(routes).filter(([, r]) => r.nav && (!r.adminOnly || user?.role === 'admin'));
  nav.innerHTML = items.map(([path, r]) =>
    `<a href="#${path}" data-path="${path}">${esc(r.title)}</a>`).join('');
  usernameEl.textContent = user?.username ?? '';
  roleEl.innerHTML = user?.role === 'admin' ? '<span class="badge accent">管理员</span>' : '';
}

function renderForceBanner() {
  const existing = document.getElementById('force-banner');
  if (existing) existing.remove();
  if (getUser()?.passwordChangeRequired) {
    const banner = document.createElement('div');
    banner.className = 'container';
    banner.id = 'force-banner';
    banner.innerHTML = `<div class="banner">⚠️ <span>当前密码为初始密码（首次登录或已被管理员重置），请先修改密码后再使用其他功能。</span></div>`;
    document.querySelector('.topbar').after(banner);
  }
}

function setViewRoot(html) {
  document.getElementById('view-root').innerHTML = html;
}

function cleanupCurrent() {
  if (typeof state.currentCleanup === 'function') {
    try { state.currentCleanup(); } catch { /* 忽略清理异常 */ }
  }
  state.currentCleanup = null;
  state.currentRoute = null;
}

async function route() {
  if (!state.user) return; // 未登录时由 showAuth 接管
  let path = (location.hash || '#/download').replace(/^#/, '') || '/download';
  if (!routes[path]) path = '/download';
  const def = routes[path];

  // 契约：passwordChangeRequired 时引导修改密码，未改密前不进入其他页面。
  if (state.user.passwordChangeRequired && path !== '/password') {
    location.hash = '#/password';
    return;
  }
  if (def.adminOnly && state.user.role !== 'admin') {
    toast('该页面仅管理员可访问', 'error');
    location.hash = '#/download';
    return;
  }

  cleanupCurrent();
  state.currentRoute = path;
  renderNav();
  renderForceBanner();
  document.querySelectorAll('#main-nav a').forEach((a) => {
    a.classList.toggle('active', a.dataset.path === path);
  });

  const ctx = { getUser, setUser, navigate };
  if (def.auth) {
    def.auth.render(app, { ...ctx, mode: def.mode, onDone: () => { buildShell(); route(); } });
    return;
  }
  setViewRoot(`<div class="loading-inline"><span class="spinner"></span>加载中…</div>`);
  const root = document.getElementById('view-root');
  try {
    state.currentCleanup = await def.view.render(root, ctx) || null;
  } catch (e) {
    setViewRoot(`<div class="empty"><span class="big">😿</span>页面加载失败：${esc(errText(e))}</div>`);
  }
}

function showAuth() {
  cleanupCurrent();
  const path = (location.hash || '').replace(/^#/, '');
  const mode = path === '/register' ? 'register' : 'login';
  auth.render(app, {
    mode,
    setUser,
    onDone: () => {
      buildShell();
      const target = state.user?.passwordChangeRequired ? '#/password' : '#/download';
      location.hash = target;
      route();
    },
  });
}

async function boot() {
  setUnauthorizedHandler(() => {
    const wasLoggedIn = !!state.user;
    setUser(null);
    if (wasLoggedIn) toast('登录已过期，请重新登录', 'error');
    showAuth(); // 直接切换到认证页，不依赖 hash
  });

  try {
    const me = await api('/api/auth/me');
    setUser(me);
    buildShell();
    route();
  } catch {
    setUser(null);
    showAuth();
  }
}

window.addEventListener('hashchange', () => {
  if (!state.user) { showAuth(); return; }
  route();
});

boot();
