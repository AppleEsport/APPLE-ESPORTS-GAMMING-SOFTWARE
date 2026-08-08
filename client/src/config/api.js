// ═══════════════════════════════════════════════════════════
// Gaming Café ERP — API Client Configuration
// Axios instance with JWT interceptor + error handling
// ═══════════════════════════════════════════════════════════

import axios from 'axios';

const API_BASE_URL = '/api';

const api = axios.create({
  baseURL: API_BASE_URL,
  timeout: 15000,
  withCredentials: true,
  headers: {
    'Content-Type': 'application/json',
    'Cache-Control': 'no-cache, no-store, must-revalidate',
    'Pragma': 'no-cache',
    'Expires': '0'
  },
});

// ── Request Interceptor — attach branch header ──
// Tokens are now sent as HTTP-only cookies automatically (no manual attach needed)
api.interceptors.request.use(
  (config) => {
    // Attach branch header for Super Admin branch switching
    const activeBranch = localStorage.getItem('activeBranchId');
    if (activeBranch) {
      config.headers['X-Branch-Id'] = activeBranch;
    }

    return config;
  },
  (error) => Promise.reject(error)
);

let isRefreshing = false;
let refreshSubscribers = [];

const subscribeTokenRefresh = (cb) => {
  refreshSubscribers.push(cb);
};

const onRefreshed = (token) => {
  refreshSubscribers.map((cb) => cb(token));
  refreshSubscribers = [];
};

// ── Response Interceptor — handle token expiry + errors ──
api.interceptors.response.use(
  (response) => response,
  async (error) => {
    const originalRequest = error.config;

    // Do not attempt refresh for login requests or invalid credentials
    if (originalRequest.url.includes('/auth/admin/login') || 
        originalRequest.url.includes('/auth/operator/login') ||
        error.response?.data?.code === 'INVALID_CREDENTIALS') {
      return Promise.reject(error);
    }

    // Token expired — attempt refresh (cookies are sent automatically)
    if (error.response?.status === 401 && !originalRequest._retry) {
      originalRequest._retry = true;

      if (!isRefreshing) {
        isRefreshing = true;

        try {
          // Send refresh request with credentials (cookies auto-included)
          await axios.post(`${API_BASE_URL}/auth/refresh`, {}, {
            withCredentials: true
          });

          isRefreshing = false;
          onRefreshed(true);
          return api(originalRequest);

        } catch (refreshError) {
          isRefreshing = false;
          onRefreshed(new Error('Refresh failed'));
          refreshSubscribers = [];

          // Refresh failed — force logout
          localStorage.removeItem('user');
          localStorage.removeItem('activeBranchId');
          window.location.href = '/';
          return Promise.reject(refreshError);
        }
      } else {
        // Already refreshing, join the queue
        return new Promise((resolve, reject) => {
          subscribeTokenRefresh((token) => {
            if (token instanceof Error) {
              reject(token);
            } else {
              resolve(api(originalRequest));
            }
          });
        });
      }
    }

    // Force logout response (SOP §11: Live Access Revocation)
    // Only redirect if this wasn't a login attempt (login attempts should just show an error)
    const isLoginRequest = originalRequest.url?.includes('/login');
    if (error.response?.status === 403 && error.response?.data?.code === 'ACCOUNT_INACTIVE' && !isLoginRequest) {
      localStorage.clear();
      window.location.href = '/?reason=account_inactive';
      return Promise.reject(error);
    }
    // Broadcast 5xx or Network Errors for Dashboard System Flags
    if (!error.response || error.response.status >= 500) {
      window.dispatchEvent(new CustomEvent('system-error', {
        detail: {
          url: originalRequest.url,
          method: originalRequest.method,
          message: error.message || 'Network or Server Error',
          status: error.response?.status || 'Network',
          timestamp: new Date().toISOString()
        }
      }));
    }

    return Promise.reject(error);
  }
);

export default api;
