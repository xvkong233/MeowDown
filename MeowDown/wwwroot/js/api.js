// 统一 API 访问层：同源 Cookie 会话、JSON 内容类型约束、统一错误格式解析。

export class ApiError extends Error {
  constructor(status, code, message) {
    super(message);
    this.name = 'ApiError';
    this.status = status;
    this.code = code || 'error';
  }
}

// 会话失效（仅契约中的 unauthenticated）时回调，用于跳转登录页。
let unauthorizedHandler = null;
export function setUnauthorizedHandler(fn) { unauthorizedHandler = fn; }

function humanize(code, message, status) {
  if (message) return message;
  const fallback = {
    400: '请求无效',
    401: '未登录或会话已过期',
    403: '没有权限执行此操作',
    404: '资源不存在',
    409: '操作冲突',
    415: '请求内容类型不受支持',
    429: '请求过于频繁，请稍后再试',
    502: 'B站接口出错，请稍后重试',
    504: 'B站接口超时，请稍后重试',
  };
  return fallback[status] || `请求失败（HTTP ${status}）`;
}

async function toApiError(res) {
  let code = 'error';
  let message = '';
  try {
    const data = await res.json();
    if (data && typeof data === 'object' && data.error) {
      code = data.error.code || code;
      message = data.error.message || '';
    }
  } catch { /* 响应体不是 JSON，走默认文案 */ }
  return new ApiError(res.status, code, humanize(code, message, res.status));
}

/**
 * 发起同源 API 请求。
 * - 除 GET 外统一携带 Content-Type: application/json（契约的 CSRF 约束，缺失返回 415）。
 * - 401 且错误码为 unauthenticated 时触发会话失效回调（登录失败等 invalid_credentials 不触发）。
 * - 204 返回 null；JSON 响应返回解析后的对象。
 */
export async function api(path, { method = 'GET', body, signal } = {}) {
  method = method.toUpperCase();
  const init = { method, credentials: 'same-origin', headers: {} };
  if (signal) init.signal = signal;
  if (method !== 'GET') init.headers['Content-Type'] = 'application/json';
  if (body !== undefined) init.body = JSON.stringify(body);

  let res;
  try {
    res = await fetch(path, init);
  } catch (e) {
    if (e && e.name === 'AbortError') throw e;
    throw new ApiError(0, 'network_error', '网络错误：无法连接服务器');
  }

  if (res.status === 401) {
    const err = await toApiError(res);
    if (err.code === 'unauthenticated' && unauthorizedHandler) unauthorizedHandler();
    throw err;
  }
  if (res.status === 204) return null;
  if (!res.ok) throw await toApiError(res);

  const contentType = res.headers.get('content-type') || '';
  if (contentType.includes('application/json')) {
    try { return await res.json(); }
    catch { return null; }
  }
  return res.text();
}

export function errText(e) {
  if (e instanceof ApiError) return e.message;
  if (e && e.message) return e.message;
  return String(e ?? '未知错误');
}
