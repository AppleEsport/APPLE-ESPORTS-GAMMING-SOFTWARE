import { useEffect, useState, useCallback } from 'react';
import { ZapOff, Play, Square, Loader2, AlertTriangle } from 'lucide-react';
import api from '../../config/api';

/**
 * Asks the one question only a person standing at the counter can answer after a power cut:
 * is the customer still here?
 *
 * A session held after an outage has a stopped clock and unused paid time. Resuming it gives
 * the customer the minutes they paid for; stopping it bills only what they actually played.
 * The system deliberately will not choose — restarting the clock on an empty seat would
 * charge someone for time they never got, and closing a session whose customer is standing
 * there waiting would rob them of it.
 *
 * Shown as a banner rather than inside a PC's detail panel because a cut hits several PCs at
 * once, and an operator opening the dashboard afterwards sees machines that look busy with no
 * indication anything is wrong.
 */
export default function InterruptedSessionsBanner({ onChanged }) {
  const [sessions, setSessions] = useState([]);
  const [busyId, setBusyId] = useState(null);
  const [error, setError] = useState(null);

  const load = useCallback(async () => {
    try {
      const res = await api.get('/sessions/interrupted');
      setSessions(res.data?.data || []);
    } catch {
      // A branch that cannot reach its own API has bigger problems, and this banner
      // shouting about it would only add noise.
      setSessions([]);
    }
  }, []);

  useEffect(() => {
    load();
    // Recovery runs at start-up, so a session can appear here without any action on this
    // screen. Polling slowly is enough — nobody is watching for it by the second.
    const timer = setInterval(load, 30000);
    return () => clearInterval(timer);
  }, [load]);

  const act = async (session, action) => {
    setBusyId(session.sessionId);
    setError(null);
    try {
      if (action === 'resume') {
        await api.post(`/sessions/${session.sessionId}/resume`);
      } else {
        await api.post(`/sessions/${session.sessionId}/stop`, { deferPayment: false });
      }
      await load();
      onChanged?.();
    } catch (err) {
      setError(err.response?.data?.error || 'That did not go through. Try again.');
    } finally {
      setBusyId(null);
    }
  };

  if (sessions.length === 0) return null;

  return (
    <div className="bg-neon-orange-dim border border-neon-orange/50 rounded-lg p-4 mb-4">
      <div className="flex items-center gap-2 mb-3">
        <ZapOff className="w-4 h-4 text-neon-orange" />
        <h3 className="font-heading font-bold text-sm uppercase tracking-wider text-neon-orange">
          {sessions.length === 1
            ? '1 session was interrupted'
            : `${sessions.length} sessions were interrupted`}
        </h3>
      </div>

      <p className="text-xs text-text-2 mb-3">
        The clock is stopped on these. Nobody is being charged while they wait.
      </p>

      {error && <p className="text-xs text-accent mb-2">{error}</p>}

      <div className="space-y-2">
        {sessions.map((s) => {
          const busy = busyId === s.sessionId;
          return (
            <div
              key={s.sessionId}
              className="flex flex-wrap items-center gap-3 bg-bg-2 border border-border rounded-md px-3 py-2.5"
            >
              <div className="min-w-0 flex-1">
                <div className="flex items-center gap-2 flex-wrap">
                  <span className="font-mono font-bold text-sm">{s.pcName}</span>
                  <span className="text-sm text-text-2">{s.customerName}</span>
                  {s.needsReview && (
                    <span
                      className="badge badge-offline flex items-center gap-1"
                      title="This gap ran through hours the branch was closed, so it is more likely a session nobody stopped than a power cut."
                    >
                      <AlertTriangle className="w-3 h-3" /> Check this one
                    </span>
                  )}
                </div>
                <div className="text-xs text-text-3 mt-0.5">
                  Played {s.playedMinutes} min
                  {s.remainingMinutes != null && (
                    <> · <span className="text-pc-active font-semibold">{s.remainingMinutes} min still owed</span></>
                  )}
                  {s.outageMinutes > 0 && <> · off for {s.outageMinutes} min</>}
                  {' '}· held since {s.heldSince}
                </div>
              </div>

              <div className="flex items-center gap-2">
                <button
                  onClick={() => act(s, 'resume')}
                  disabled={busy}
                  className="btn-primary text-xs flex items-center gap-1.5 disabled:opacity-50"
                >
                  {busy ? <Loader2 className="w-3.5 h-3.5 animate-spin" /> : <Play className="w-3.5 h-3.5" />}
                  Customer is here
                </button>
                <button
                  onClick={() => act(s, 'stop')}
                  disabled={busy}
                  className="btn-secondary text-xs flex items-center gap-1.5 disabled:opacity-50"
                  title="Bills only the minutes actually played"
                >
                  <Square className="w-3.5 h-3.5" />
                  They left
                </button>
              </div>
            </div>
          );
        })}
      </div>
    </div>
  );
}
