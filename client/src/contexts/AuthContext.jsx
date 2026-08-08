// ═══════════════════════════════════════════════════════════
// Gaming Café ERP — Auth Context
// SOP §6: Login System + §21: Security Model
// Manages auth state, login/logout, token refresh, role detection
// ═══════════════════════════════════════════════════════════

import { createContext, useContext, useState, useCallback, useEffect } from 'react';
import api from '../config/api';
import { ROLES } from '../config/constants';

const AuthContext = createContext(null);

export function AuthProvider({ children }) {
  const [user, setUser] = useState(null);
  const [adminSwitchUser, setAdminSwitchUser] = useState(null); // SOP §22: Admin Quick-Switch
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState(null);

  const fetchCurrentUser = useCallback(async () => {
    try {
      const res = await api.get('/auth/me');
      const userData = res.data.data;
      setUser(userData);
      localStorage.setItem('user', JSON.stringify(userData));
    } catch (error) {
      if (error.response && (error.response.status === 401 || error.response.status === 403)) {
        setUser(null);
        localStorage.removeItem('user');
      }
    }
  }, []);

  // ── Initialize auth state from localStorage (tokens are now in HTTP-only cookies) ──
  useEffect(() => {
    const storedUser = localStorage.getItem('user');

    if (storedUser && storedUser !== 'undefined') {
      try {
        setUser(JSON.parse(storedUser));
        // Verify token is still valid by fetching current user
        fetchCurrentUser().finally(() => setLoading(false));
      } catch (e) {
        console.error("Failed to parse stored user", e);
        localStorage.removeItem('user');
        setLoading(false);
      }
    } else {
      setLoading(false);
    }
  }, [fetchCurrentUser]);

  // ── Super Admin Login (SOP §6.2) ──
  // Tokens are now set as HTTP-only cookies by the backend
  const loginAdmin = useCallback(async (email, password) => {
    try {
      setError(null);
      const response = await api.post('/auth/admin/login', { email, password });
      const { user: userData } = response.data.data;

      localStorage.setItem('user', JSON.stringify(userData));
      setUser(userData);
      return userData;
    } catch (err) {
      const message = err.response?.data?.error || 'Login failed';
      setError(message);
      throw new Error(message);
    }
  }, []);

  // ── Operator Login (SOP §6.3) ──
  // Tokens are now set as HTTP-only cookies by the backend
  const loginOperator = useCallback(async (branchId, username, password) => {
    try {
      setError(null);
      const response = await api.post('/auth/operator/login', { branchId, username, password });
      const { user: userData } = response.data.data;

      localStorage.setItem('user', JSON.stringify(userData));
      setUser(userData);
      return userData;
    } catch (err) {
      const message = err.response?.data?.error || 'Login failed';
      setError(message);
      throw new Error(message);
    }
  }, []);

  // ── Admin Quick-Switch In (SOP §22) ──
  // Tokens are now set as HTTP-only cookies by the backend
  const adminSwitchIn = useCallback(async (adminId, accessPin) => {
    try {
      setError(null);
      const response = await api.post('/auth/admin-switch/in', { adminId, accessPin });
      const { user: adminData } = response.data.data;

      setAdminSwitchUser(adminData);
      return adminData;
    } catch (err) {
      const message = err.response?.data?.error || 'Admin switch failed';
      setError(message);
      throw new Error(message);
    }
  }, []);

  const fetchAvailableAdminsForSwitch = useCallback(async () => {
    try {
      const res = await api.get('/auth/admin-switch/available');
      return res.data.data || [];
    } catch (err) {
      console.error('Failed to fetch available admins for switch:', err);
      return [];
    }
  }, []);

  // ── Exit Admin Mode (SOP §22) ──
  const exitAdminSwitch = useCallback(async () => {
    try {
      await api.post('/auth/admin-switch/out').catch(() => {});
    } finally {
      setAdminSwitchUser(null);
      // Re-fetch operator user data
      await fetchCurrentUser();
    }
  }, [fetchCurrentUser]);

  // ── Logout (SOP §10: shift closure for operators) ──
  const logout = useCallback(async () => {
    // Capture role to determine where to redirect after logout
    const role = user?.role || user?.Role || '';

    try {
      await api.post('/auth/logout', { shiftId: user?.shiftId }).catch(() => {});
    } finally {
      localStorage.removeItem('user');
      localStorage.removeItem('activeBranchId');
      setUser(null);
      setAdminSwitchUser(null);
      setError(null);

      // Explicitly navigate to the correct login portal
      let redirectPath = '/';
      if (role === ROLES.SUPER_ADMIN) {
        redirectPath = '/login/superadmin';
      } else if (typeof role === 'string' && role.toLowerCase().includes('admin')) {
        redirectPath = '/login/admin';
      } else if (typeof role === 'string' && role.toLowerCase().includes('operator')) {
        redirectPath = '/login/operator';
      }

      window.location.href = redirectPath;
    }
  }, [user]);

  /// Ends the session without navigating anywhere.
  ///
  /// `logout` sends the browser to a login portal, which is right for a person clicking
  /// "log out" but useless when a login page needs to discard a stale session it is already
  /// showing — that would bounce the page away and, on a portal that redirects authenticated
  /// users, loop. The server call matters: the token lives in an HttpOnly cookie the page
  /// cannot clear itself, so skipping it would leave the old session usable.
  const clearSession = useCallback(async () => {
    try {
      await api.post('/auth/logout', {}).catch(() => {});
    } finally {
      localStorage.removeItem('user');
      localStorage.removeItem('activeBranchId');
      setUser(null);
      setAdminSwitchUser(null);
    }
  }, []);

  // ── Role checks ──
  const activeUser = adminSwitchUser || user;
  const userRole = activeUser?.role || activeUser?.Role;
  const isSuperAdmin = userRole === ROLES.SUPER_ADMIN || (typeof userRole === 'string' && userRole.toLowerCase().includes('admin'));
  const isOperator = userRole === ROLES.OPERATOR || (typeof userRole === 'string' && userRole.toLowerCase().includes('operator'));
  const isAuthenticated = !!activeUser;

  // ── Dashboard permission check (SOP §19.2) ──
  const hasDashboardAccess = useCallback((dashboardKey) => {
    const checkUser = adminSwitchUser || user;
    if (!checkUser) return false;
    const role = checkUser.role || checkUser.Role;
    if (role === ROLES.SUPER_ADMIN) return true;
    
    // Operator dashboards that Admins should implicitly have access to
    const operatorDashboards = [
      'billing_counter', 'sessions', 'reservations', 'food_orders', 
      'cash_register', 'cash_desk', 'online_desk', 'wallet_desk', 'credits', 'eod'
    ];

    if (role === ROLES.ADMIN) {
      if (dashboardKey === 'main_dashboard' || operatorDashboards.includes(dashboardKey)) return true;
    }

    return checkUser.dashboardPermissions?.[dashboardKey] === true;
  }, [user, adminSwitchUser]);

  const value = {
    user: adminSwitchUser || user,
    baseUser: user, // Keep track of the original operator
    adminSwitchUser,
    loading,
    error,
    isAuthenticated,
    isSuperAdmin,
    isOperator,
    loginAdmin,
    loginOperator,
    logout,
    clearSession,
    adminSwitchIn,
    exitAdminSwitch,
    fetchAvailableAdminsForSwitch,
    hasDashboardAccess,
    fetchCurrentUser,
    setError,
  };

  return (
    <AuthContext.Provider value={value}>
      {children}
    </AuthContext.Provider>
  );
}

export function useAuth() {
  const context = useContext(AuthContext);
  if (!context) {
    throw new Error('useAuth must be used within an AuthProvider');
  }
  return context;
}

export default AuthContext;
