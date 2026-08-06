import api from '../config/api';

export const getSessionActivities = async (sessionId) => {
  const { data } = await api.get(`/sessions/${sessionId}/activities`);
  return data.data || [];
};

export const getRecentActivities = async (limit = 100) => {
  const { data } = await api.get(`/sessions/activities/recent?limit=${limit}`);
  return data.data || [];
};
