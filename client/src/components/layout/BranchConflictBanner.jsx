import { useState, useEffect, useCallback } from 'react';
import { AlertTriangle } from 'lucide-react';
import api from '../../config/api';
import { useAuth } from '../../contexts/AuthContext';

// ═══════════════════════════════════════════════════════════════════════════
// Two PCs both reporting as the same branch.
//
// This lives in the shell, on every screen, and that is the entire point of it.
//
// The warning already existed — the server has always detected the clash and
// written ConflictingMachine on every beat, and the dashboard already rendered
// a good, specific message about it. It was gated on
// `isSuperAdmin && !activeBranch`, i.e. it only appeared on the dashboard while
// viewing "All Branches". So the one action that hides it was selecting the
// affected branch — exactly what anybody does the moment they suspect that
// branch is wrong. It cost days of chasing a phantom sync bug that was really
// two counter PCs taking turns overwriting each other every three seconds.
//
// A warning you have to already be on the right screen to see is not a warning.
// ═══════════════════════════════════════════════════════════════════════════

const RECHECK_EVERY_MS = 30000;

export default function BranchConflictBanner() {
  const { isSuperAdmin, isOperator, user } = useAuth();
  const [conflicts, setConflicts] = useState([]);

  const check = useCallback(async () => {
    try {
      const { data } = await api.get('/branch-status');
      const rows = data?.data ?? [];
      const clashing = rows.filter((r) => r.conflictingMachine);

      // An operator is shown only their own branch — they are not entitled to
      // the others, and it is their own counter they can actually walk over to
      // and switch off.
      setConflicts(
        isOperator ? clashing.filter((r) => r.branchId === user?.branchId) : clashing
      );
    } catch {
      // Never let this break a page. Silence here means "could not check",
      // which must not be rendered as "all clear" — we simply keep whatever
      // the last successful check found rather than falsely clearing it.
    }
  }, [isOperator, user?.branchId]);

  useEffect(() => {
    check();
    const t = setInterval(check, RECHECK_EVERY_MS);
    return () => clearInterval(t);
  }, [check]);

  if (conflicts.length === 0) return null;

  return (
    <div className="space-y-2 mb-4" role="alert">
      {conflicts.map((c) => (
        <div
          key={c.branchId}
          className="bg-red-500/10 border border-red-500/40 rounded-lg p-4 flex items-start gap-2.5"
        >
          <AlertTriangle className="w-4 h-4 text-red-500 mt-0.5 shrink-0" />
          <p className="text-sm text-text leading-relaxed">
            <span className="font-bold">{c.branchName}</span> has two PCs both reporting as itself
            right now: <span className="font-mono">{c.reportedByMachine}</span> and{' '}
            <span className="font-mono">{c.conflictingMachine}</span>. Each is keeping its own
            records and syncing them under this one branch — their takings are being merged and
            cannot be separated afterwards, and every figure on screen for this branch is being
            overwritten several times a minute by whichever PC reported last.{' '}
            {isSuperAdmin
              ? 'Stop the Apple Esports API service on whichever one is not the real counter PC.'
              : 'Tell Head Office — one of these two machines is not the real counter PC.'}
          </p>
        </div>
      ))}
    </div>
  );
}
