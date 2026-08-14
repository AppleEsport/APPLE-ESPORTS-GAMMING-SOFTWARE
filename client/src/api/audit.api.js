import api from '../config/api';

// The trail every login, session, payment, wallet change and member edit already wrote down,
// now readable across every branch from one screen instead of only at the counter it happened
// at. See AuditLogsController.cs for what changed to make that true.
export const getAuditLog = ({ branchId, userName, action, from, to, page = 1, pageSize = 50 } = {}) => {
  const params = { page, pageSize };
  if (branchId) params.branchId = branchId;
  if (userName) params.userName = userName;
  if (action) params.action = action;
  if (from) params.from = from;
  if (to) params.to = to;
  return api.get('/audit-logs', { params }).then(r => r.data?.data);
};
