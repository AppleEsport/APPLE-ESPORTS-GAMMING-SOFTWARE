import api from '../config/api';

export async function markMaintenanceAsync(pcId, reason, branchId) {
  try {
    const response = await api.post('/pc-management/maintenance-logs/mark', {
      pcId,
      branchId,
      reason
    });
    return response.data;
  } catch (error) {
    throw error?.response?.data || error;
  }
}

export async function resolveMaintenance(pcId, resolutionNotes = null) {
  try {
    const response = await api.post(`/pc-management/maintenance-logs/resolve/${pcId}`, {
      resolutionNotes
    });
    return response.data;
  } catch (error) {
    throw error?.response?.data || error;
  }
}

export async function getBranchMaintenanceLogs(branchId, days = 7) {
  try {
    const response = await api.get(`/pc-management/maintenance-logs/branch/${branchId}`, {
      params: { days }
    });
    return response.data;
  } catch (error) {
    throw error?.response?.data || error;
  }
}

export async function getPcMaintenanceHistory(pcId) {
  try {
    const response = await api.get(`/pc-management/maintenance-logs/pc/${pcId}/history`);
    return response.data;
  } catch (error) {
    throw error?.response?.data || error;
  }
}

export async function getActiveMaintenanceForPc(pcId) {
  try {
    const response = await api.get(`/pc-management/maintenance-logs/pc/${pcId}/active`);
    return response.data;
  } catch (error) {
    throw error?.response?.data || error;
  }
}
