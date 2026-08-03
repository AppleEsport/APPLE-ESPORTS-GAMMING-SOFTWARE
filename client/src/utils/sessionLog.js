// Lightweight pub/sub for the session activity log strip — decoupled from React state so
// any component (PcCard, SessionsPage, modals) can push an entry without prop drilling.
export const SESSION_LOG_EVENT = 'session-activity-log';

export function logActivity(message, type = 'info') {
  window.dispatchEvent(new CustomEvent(SESSION_LOG_EVENT, {
    detail: { message, type, timestamp: Date.now() },
  }));
}
