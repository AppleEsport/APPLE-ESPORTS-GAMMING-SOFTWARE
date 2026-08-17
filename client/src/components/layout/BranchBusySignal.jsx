import { useEffect } from 'react';
import api from '../../config/api';

/// How often to ask. An update that waits an extra half-minute costs nothing; asking every few
/// seconds from a page nobody is looking at costs the branch API a request for no reason.
const POLL_MS = 30000;

/**
 * Publishes "somebody at this branch is mid-session" onto <body>, so the native shell hosting
 * this dashboard can hold an update back instead of taking the counter down under a live shop.
 *
 * The counter PC needed its own signal. The gaming-PC guard asks the overlay page whether that
 * one seat is playing, which is the right question there and the wrong one here: a counter PC
 * has no session of its own, so it read "nobody is playing" and installed whenever it liked —
 * stopping PostgreSQL and the branch API mid-trade, with no warning to whoever was standing at
 * the till. That is what every release so far has done to all four shops.
 *
 * Lives in AppShell rather than on a page that happens to show PCs, because the operator could
 * be anywhere in the dashboard when an update lands — on Cash Desk, in Reports — and a signal
 * that is only correct on one screen is worse than none: it would read "idle" and let the
 * update through at exactly the wrong moment.
 *
 * Deliberately fails closed. If the branch cannot be asked, the attribute is left exactly as it
 * was rather than cleared, because "I could not find out" must never be mistaken for "nobody is
 * playing" — that is precisely the reasoning error that made the gaming-PC guard useless.
 */
export default function BranchBusySignal() {
  useEffect(() => {
    let cancelled = false;

    const check = async () => {
      try {
        const res = await api.get('/pcs');
        if (cancelled) return;

        const pcs = res.data?.data || [];
        const busy = pcs.some(pc => {
          const state = String(pc.state ?? pc.status ?? '').toLowerCase();
          return state === 'active' || state === 'in_use' || state === 'occupied';
        });

        if (busy) document.body.setAttribute('data-sessions-active', 'true');
        else document.body.removeAttribute('data-sessions-active');
      } catch {
        // Left as-is on purpose - see the note above about failing closed.
      }
    };

    check();
    const timer = setInterval(check, POLL_MS);

    return () => {
      cancelled = true;
      clearInterval(timer);
      document.body.removeAttribute('data-sessions-active');
    };
  }, []);

  return null;
}
