// 在线解析与提交下载任务：POST /api/parse 元数据 → POST /api/tasks 提交。

import { api, errText } from '../api.js';
import { esc, toast, withLoading, fmtTs, fmtDur } from '../ui.js';

function template() {
  return `
  <div class="view-header">
    <div>
      <h1 class="view-title">解析下载</h1>
      <p class="view-sub">粘贴视频链接或编号（av / BV / ep / ss），解析后选择分P与清晰度提交任务。</p>
    </div>
  </div>
  <div id="dl-banners"></div>
  <div class="card">
    <div class="parse-bar">
      <input type="text" id="parse-url" placeholder="例如：BV1qt4y1X7TW 或 https://www.bilibili.com/video/BV...">
      <button class="btn primary" id="parse-btn">解 析</button>
    </div>
    <div class="form-error" id="parse-error" style="margin: 10px 0 0;"></div>
  </div>
  <div id="parse-result"></div>`;
}

// 把选中的分P序号压缩为 SelectPage 语法（1,3,5-8）；全选返回 ALL。
function compressPages(indexes) {
  if (indexes.length === 0) return '';
  const sorted = [...indexes].sort((a, b) => a - b);
  const parts = [];
  let i = 0;
  while (i < sorted.length) {
    let j = i;
    while (j + 1 < sorted.length && sorted[j + 1] === sorted[j] + 1) j++;
    parts.push(i === j ? String(sorted[i]) : `${sorted[i]}-${sorted[j]}`);
    i = j + 1;
  }
  return parts.join(',');
}

export function render(root, ctx) {
  const { getUser, navigate } = ctx;
  root.innerHTML = template();

  const urlInput = root.querySelector('#parse-url');
  const parseBtn = root.querySelector('#parse-btn');
  const parseError = root.querySelector('#parse-error');
  const banners = root.querySelector('#dl-banners');
  const resultEl = root.querySelector('#parse-result');

  if (getUser()?.bilibiliBound === false) {
    banners.innerHTML = `<div class="banner info">ℹ️ <span>你尚未绑定B站账号，只能下载未登录可用的清晰度。前往 <a href="#/bilibili">B站绑定</a> 扫码后可解锁更高清晰度。</span></div>`;
  }

  let meta = null;              // /api/parse 的返回
  const selectedPages = new Set();
  let pickedQualities = [];     // 有序：选择顺序即 DfnPriority 优先级

  function summaryText() {
    if (!meta) return '';
    const pagesStr = meta.pages.length > 1
      ? (selectedPages.size === meta.pages.length ? 'ALL（全部分P）' : `${compressPages([...selectedPages])}（共 ${selectedPages.size}P）`)
      : 'P1';
    const dfnStr = pickedQualities.length
      ? pickedQualities.map((id) => meta.qualities.find((q) => q.id === id)?.name ?? id).join(' → ')
      : '默认（自动选择最优可用清晰度）';
    return { pagesStr, dfnStr };
  }

  function renderSummary() {
    const box = root.querySelector('#task-summary');
    if (!box || !meta) return;
    const { pagesStr, dfnStr } = summaryText();
    box.innerHTML = `
      <div><b>分P选择：</b>${esc(pagesStr)}</div>
      <div><b>清晰度优先级：</b>${esc(dfnStr)}</div>`;
  }

  function bindPageItemEvents() {
    // page-item 是 label，点击任意处原生触发内部 checkbox 的 change，无需手动 toggle。
    resultEl.querySelectorAll('.page-item input').forEach((cb) => {
      cb.addEventListener('change', () => {
        const idx = Number(cb.dataset.idx);
        if (cb.checked) selectedPages.add(idx); else selectedPages.delete(idx);
        cb.closest('.page-item').classList.toggle('checked', cb.checked);
        updatePageButtons();
        renderSummary();
      });
    });
  }

  function updatePageButtons() {
    const allBtn = root.querySelector('#pages-all');
    const noneBtn = root.querySelector('#pages-none');
    if (!allBtn) return;
    allBtn.textContent = selectedPages.size === meta.pages.length ? '全不选' : '全选';
    noneBtn.style.display = selectedPages.size ? '' : 'none';
  }

  function renderQualities() {
    const wrap = root.querySelector('#quality-chips');
    if (!wrap) return;
    wrap.innerHTML = meta.qualities.map((q) => {
      const pos = pickedQualities.indexOf(q.id);
      const order = pos >= 0 ? `<span class="order">${pos + 1}</span>` : '';
      return `<button type="button" class="chip ${pos >= 0 ? 'selected' : ''}" data-qid="${q.id}"
        title="${pos >= 0 ? '点击取消；编号为优先级顺序' : '点击选择，越早点选优先级越高'}">${order}${esc(q.name)}</button>`;
  }).join('') + (meta.qualities.length === 0 ? '<span class="empty" style="padding:6px 0">无可用清晰度</span>' : '');
    wrap.querySelectorAll('.chip').forEach((chip) => {
      chip.addEventListener('click', () => {
        const id = Number(chip.dataset.qid);
        const pos = pickedQualities.indexOf(id);
        if (pos >= 0) pickedQualities.splice(pos, 1);
        else pickedQualities.push(id);
        renderQualities();
        renderSummary();
      });
    });
  }

  function renderMeta() {
    const p = meta;
    const pagesHtml = p.pages.map((pg) => `
      <label class="page-item ${selectedPages.has(pg.index) ? 'checked' : ''}">
        <input type="checkbox" data-idx="${pg.index}" ${selectedPages.has(pg.index) ? 'checked' : ''}>
        <span class="p-idx">P${pg.index}</span>
        <span class="p-title" title="${esc(pg.title)}">${esc(pg.title)}</span>
        <span class="p-dur">${fmtDur(pg.durationSeconds)}</span>
      </label>`).join('');

    resultEl.innerHTML = `
      <div class="card">
        <div class="media-card">
          <div class="media-cover" id="media-cover">
            <img id="media-img" alt="" hidden referrerpolicy="no-referrer">
          </div>
          <div class="media-info">
            <h2 class="media-title">${esc(p.title || p.bvid || p.aid || '（无标题）')}</h2>
            <div class="media-meta">
              ${p.isBangumi ? '<span class="badge accent">番剧</span>' : '<span class="badge">普通视频</span>'}
              ${p.bvid ? `<span class="badge">${esc(p.bvid)}</span>` : ''}
              ${p.aid ? `<span class="badge">av${esc(p.aid)}</span>` : ''}
              ${p.pubTime ? `<span class="badge">${esc(fmtTs(p.pubTime))}</span>` : ''}
              <span class="badge">${p.pages.length} P</span>
            </div>
            ${p.desc ? `<div class="media-desc">${esc(p.desc)}</div>` : ''}
          </div>
        </div>
      </div>

      <div class="card">
        <h3 class="card-title">选择分P</h3>
        <div class="view-actions" style="margin-bottom:10px">
          <button type="button" class="btn ghost sm" id="pages-all">全选</button>
          <button type="button" class="btn ghost sm" id="pages-none" style="display:none">全不选</button>
        </div>
        ${p.pages.length > 1
          ? `<div class="page-list">${pagesHtml}</div>`
          : `<div class="card-hint" style="margin:0">该视频共 1 个分P，将下载 P1。</div>`}
      </div>

      <div class="card">
        <h3 class="card-title">清晰度优先级</h3>
        <p class="card-hint">按点击顺序排定优先级（第 1 个点选的最优先）；不选择则由服务端自动取最优可用档。可追加当前列表以外的档位名称作为回退——如需请改用「高级选项」或直接提交任务接口。</p>
        <div class="chip-row" id="quality-chips"></div>
      </div>

      <div class="card">
        <h3 class="card-title">提交下载任务</h3>
        <details class="adv">
          <summary>高级选项（可选）</summary>
          <div class="adv-body">
            <label class="checkbox-row"><input type="checkbox" id="opt-multi" checked><span class="lbl">多线程下载（MultiThread）</span></label>
            <label class="checkbox-row"><input type="checkbox" id="opt-audio"><span class="lbl">仅下载音频（AudioOnly）</span></label>
            <label class="checkbox-row"><input type="checkbox" id="opt-danmaku"><span class="lbl">下载弹幕（DownloadDanmaku）</span></label>
            <div class="field">
              <label>字幕语言代码（SubtitleLanguage，可选）</label>
              <input type="text" id="opt-sublang" placeholder="例如：zh,en">
            </div>
            <div class="field">
              <label>AI 字幕策略（AiSubtitlePolicy，可选）</label>
              <select id="opt-policy">
                <option value="">默认</option>
                <option value="exclude">exclude（排除AI字幕）</option>
                <option value="include">include（允许AI字幕）</option>
                <option value="prefer-human">prefer-human（优先人工字幕）</option>
                <option value="only">only（仅AI字幕）</option>
              </select>
            </div>
          </div>
        </details>
        <div class="summary-box" id="task-summary" style="margin-bottom:14px"></div>
        <div class="form-error" id="submit-error"></div>
        <div style="display:flex; gap:10px; flex-wrap:wrap">
          <button class="btn primary" id="submit-btn">提交下载任务</button>
          <button class="btn ghost" id="reparse-btn">重新解析</button>
        </div>
        <div id="submit-ok" style="margin-top:12px"></div>
      </div>`;

    if (p.pic) {
      const img = root.querySelector('#media-img');
      const cover = root.querySelector('#media-cover');
      img.addEventListener('load', () => { cover.classList.add('has-img'); img.hidden = false; });
      img.addEventListener('error', () => { img.hidden = true; cover.classList.remove('has-img'); });
      img.src = p.pic;
    }

    root.querySelector('#pages-all').addEventListener('click', () => {
      const selectAll = selectedPages.size !== p.pages.length;
      selectedPages.clear();
      if (selectAll) p.pages.forEach((pg) => selectedPages.add(pg.index));
      resultEl.querySelectorAll('.page-item').forEach((item) => {
        const cb = item.querySelector('input');
        cb.checked = selectedPages.has(Number(cb.dataset.idx));
        item.classList.toggle('checked', cb.checked);
      });
      updatePageButtons();
      renderSummary();
    });
    root.querySelector('#pages-none').addEventListener('click', () => {
      selectedPages.clear();
      resultEl.querySelectorAll('.page-item').forEach((item) => {
        const cb = item.querySelector('input');
        cb.checked = false;
        item.classList.toggle('checked', false);
      });
      updatePageButtons();
      renderSummary();
    });

    bindPageItemEvents();
    renderQualities();
    renderSummary();

    root.querySelector('#reparse-btn').addEventListener('click', () => {
      resultEl.innerHTML = '';
      meta = null;
      pickedQualities = [];
      selectedPages.clear();
      urlInput.focus();
    });

    root.querySelector('#submit-btn').addEventListener('click', (e) => {
      const errEl = root.querySelector('#submit-error');
      const okEl = root.querySelector('#submit-ok');
      errEl.textContent = '';
      okEl.innerHTML = '';
      if (p.pages.length > 1 && selectedPages.size === 0) {
        errEl.textContent = '请至少选择一个分P';
        return;
      }
      const body = { Url: urlInput.value.trim() };
      if (p.pages.length > 1) {
        body.SelectPage = selectedPages.size === p.pages.length ? 'ALL' : compressPages([...selectedPages]);
      }
      if (pickedQualities.length) {
        body.DfnPriority = pickedQualities.map((id) => p.qualities.find((q) => q.id === id)?.name ?? '').filter(Boolean).join(',');
      }
      if (root.querySelector('#opt-multi').checked) body.MultiThread = true;
      if (root.querySelector('#opt-audio').checked) body.AudioOnly = true;
      if (root.querySelector('#opt-danmaku').checked) body.DownloadDanmaku = true;
      const subLang = root.querySelector('#opt-sublang').value.trim();
      if (subLang) body.SubtitleLanguage = subLang;
      const policy = root.querySelector('#opt-policy').value;
      if (policy) body.AiSubtitlePolicy = policy;

      withLoading(e.currentTarget, async () => {
        try {
          const res = await api('/api/tasks', { method: 'POST', body });
          const taskId = res?.taskId ?? res?.TaskId ?? '';
          toast('任务已提交', 'success');
          okEl.innerHTML = `
            <div class="banner info" style="margin-bottom:0">✅ <span>任务已加入队列（<code class="inline">${esc(taskId)}</code>）
            <a href="#/tasks">查看任务列表 →</a></span></div>`;
        } catch (err) {
          errEl.textContent = errText(err);
        }
      });
    });
  }

  async function doParse() {
    const url = urlInput.value.trim();
    parseError.textContent = '';
    resultEl.innerHTML = '';
    meta = null;
    pickedQualities = [];
    selectedPages.clear();
    if (!url) {
      parseError.textContent = '请输入视频链接或编号';
      urlInput.focus();
      return;
    }
    await withLoading(parseBtn, async () => {
      try {
        meta = await api('/api/parse', { method: 'POST', body: { url } });
        meta.pages = Array.isArray(meta.pages) ? meta.pages : [];
        meta.qualities = Array.isArray(meta.qualities) ? meta.qualities : [];
        selectedPages.clear();
        meta.pages.forEach((pg) => selectedPages.add(pg.index)); // 默认全选
        renderMeta();
      } catch (err) {
        parseError.textContent = errText(err);
      }
    });
  }

  parseBtn.addEventListener('click', doParse);
  urlInput.addEventListener('keydown', (e) => { if (e.key === 'Enter') doParse(); });
}
