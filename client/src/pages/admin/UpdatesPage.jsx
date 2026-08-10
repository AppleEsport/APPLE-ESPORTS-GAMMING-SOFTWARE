import { useEffect, useState, useCallback } from 'react';
import { useAuth } from '../../contexts/AuthContext';
import { ROLES } from '../../config/constants';
import api from '../../config/api';
import { RefreshCw, CheckCircle2, Clock, Download, AlertCircle, Loader2 } from 'lucide-react';

// Styled with the app's own theme tokens rather than a stylesheet of its own. The page
// previously shipped a UpdatesPage.css full of `background: white` and `color: #1a1a1a`,
// which rendered near-invisible text on this dark UI.

export default function UpdatesPage() {
  const { user } = useAuth();

  // Roles arrive as the stored values ("super_admin"), not display labels. Comparing
  // against 'Super Admin' silently made every Super Admin look like an operator, so the
  // approval controls were unreachable for the only person allowed to use them.
  const role = user?.role || user?.Role;
  const isSuperAdmin = role === ROLES.SUPER_ADMIN;

  const [latestVersion, setLatestVersion] = useState(null);
  const [branches, setBranches] = useState([]);
  const [loading, setLoading] = useState(true);
  const [busyBranchId, setBusyBranchId] = useState(null);
  const [error, setError] = useState(null);

  const loadVersionInfo = useCallback(async () => {
    try {
      setLoading(true);

      // No '/api' prefix here — the shared client is already based at /api, so writing it
      // again produced /api/api/... and every call 404'd.
      const versionRes = await api.get('/versions/latest');
      setLatestVersion(versionRes.data?.data ?? null);

      if (isSuperAdmin) {
        const branchRes = await api.get('/versions/all-branches');
        setBranches(branchRes.data?.data ?? []);
      } else if (user?.branchId) {
        const branchRes = await api.get(`/versions/branch/${user.branchId}`);
        setBranches(branchRes.data?.data ? [branchRes.data.data] : []);
      } else {
        setBranches([]);
      }

      setError(null);
    } catch (err) {
      setError(err.response?.data?.error || err.message || 'Could not load version information.');
    } finally {
      setLoading(false);
    }
  }, [isSuperAdmin, user?.branchId]);

  useEffect(() => { loadVersionInfo(); }, [loadVersionInfo]);

  const approveVersion = async (versionInfoId) => {
    try {
      await api.post('/versions/approve', { versionInfoId });
      await loadVersionInfo();
    } catch (err) {
      setError(err.response?.data?.error || 'Could not approve this version.');
    }
  };

  const setAutoUpdate = async (branchId, enabled) => {
    if (!branchId) return;
    setBusyBranchId(branchId);
    try {
      await api.put(`/versions/branch/${branchId}/auto-update`, { autoUpdateEnabled: enabled });
      await loadVersionInfo();
      setError(null);
    } catch (err) {
      setError(err.response?.data?.error || 'Could not change the auto-update setting.');
    } finally {
      setBusyBranchId(null);
    }
  };

  return (
    <div className="space-y-5">
      <div>
        <h1 className="text-2xl font-heading font-bold tracking-wide">Updates &amp; Versions</h1>
        <p className="text-text-2 text-sm mt-1">
          {isSuperAdmin
            ? 'Approve a version once and it rolls out to every branch.'
            : 'The software version running at your branch.'}
        </p>
      </div>

      {error && (
        <div className="flex items-start gap-2 bg-neon-red-dim border border-neon-red/40 text-neon-red rounded-md px-4 py-3 text-sm">
          <AlertCircle className="w-4 h-4 mt-0.5 shrink-0" />
          <span>{error}</span>
        </div>
      )}

      {loading ? (
        <div className="card flex items-center gap-2 text-text-2">
          <Loader2 className="w-4 h-4 animate-spin" /> Loading…
        </div>
      ) : isSuperAdmin ? (
        <SuperAdminView
          latestVersion={latestVersion}
          branches={branches}
          busyBranchId={busyBranchId}
          onApprove={approveVersion}
          onSetAutoUpdate={setAutoUpdate}
        />
      ) : (
        <OperatorView
          latestVersion={latestVersion}
          branch={branches[0]}
          busy={busyBranchId != null}
          onSetAutoUpdate={setAutoUpdate}
        />
      )}
    </div>
  );
}

function SuperAdminView({ latestVersion, branches, busyBranchId, onApprove, onSetAutoUpdate }) {
  return (
    <div className="space-y-5">
      <div className="card">
        <div className="card-header">
          <h2 className="card-title">Latest Version</h2>
        </div>

        {latestVersion ? (
          <div className="space-y-3">
            <div className="flex items-center gap-3">
              <span className="font-mono text-3xl font-bold">v{latestVersion.currentVersion}</span>
              {latestVersion.approvedForRollout ? (
                <span className="badge badge-active flex items-center gap-1">
                  <CheckCircle2 className="w-3 h-3" /> Approved
                </span>
              ) : (
                <span className="badge badge-awaiting flex items-center gap-1">
                  <Clock className="w-3 h-3" /> Awaiting your approval
                </span>
              )}
            </div>

            <p className="text-text-2 text-sm">
              Released {new Date(latestVersion.createdAt).toLocaleDateString('en-IN', {
                day: '2-digit', month: 'short', year: 'numeric',
              })}
            </p>

            {latestVersion.releaseNotes && (
              <div className="bg-bg-3 border border-border rounded-md p-3">
                <p className="text-xs uppercase tracking-wider text-text-3 font-bold mb-1">What changed</p>
                <p className="text-sm text-text-2 whitespace-pre-line">{latestVersion.releaseNotes}</p>
              </div>
            )}

            {!latestVersion.approvedForRollout && (
              <button className="btn-primary" onClick={() => onApprove(latestVersion.id)}>
                Approve for all branches
              </button>
            )}
          </div>
        ) : (
          <p className="text-text-2 text-sm">No versions have been published yet.</p>
        )}
      </div>

      <div className="card">
        <div className="card-header">
          <h2 className="card-title">Branches</h2>
        </div>

        {branches.length === 0 ? (
          <p className="text-text-2 text-sm">
            No branch has reported its version yet. A branch appears here once it checks in.
          </p>
        ) : (
          <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-3">
            {branches.map((branch) => (
              <BranchCard
                key={branch.branchId || branch.id}
                branch={branch}
                busy={busyBranchId === branch.branchId}
                onSetAutoUpdate={onSetAutoUpdate}
              />
            ))}
          </div>
        )}
      </div>
    </div>
  );
}

function BranchCard({ branch, busy, onSetAutoUpdate }) {
  const total = Math.max(branch.gamingPcsTotalCount || 0, 1);
  const upToDate = branch.gamingPcsUpToDateCount || 0;
  const pct = Math.round((upToDate / total) * 100);

  return (
    <div className={`bg-bg-3 border rounded-md p-4 space-y-3 ${
      branch.updateAvailable ? 'border-neon-orange/50' : 'border-border'
    }`}>
      <div className="flex items-start justify-between gap-2">
        <h3 className="font-heading font-bold">{branch.branchName}</h3>
        {branch.updateAvailable ? (
          <span className="badge badge-awaiting">Update ready</span>
        ) : (
          <span className="badge badge-active">Up to date</span>
        )}
      </div>

      <div className="flex items-center gap-2 font-mono text-sm">
        <span>v{branch.currentVersion || '—'}</span>
        {branch.updateAvailable && (
          <>
            <span className="text-text-3">→</span>
            <span className="text-neon-orange">v{branch.latestApprovedVersion}</span>
          </>
        )}
      </div>

      <div>
        <div className="h-1.5 bg-bg-4 rounded-full overflow-hidden">
          <div className="h-full bg-pc-active transition-all" style={{ width: `${pct}%` }} />
        </div>
        <p className="text-xs text-text-2 mt-1">{upToDate}/{branch.gamingPcsTotalCount || 0} gaming PCs up to date</p>
      </div>

      {/* Super Admin can switch a branch to automatic here, rather than asking the operator
          at that branch to do it themselves. */}
      <AutoUpdateToggle
        checked={!!branch.autoUpdateEnabled}
        busy={busy}
        onChange={(next) => onSetAutoUpdate(branch.branchId, next)}
      />
    </div>
  );
}

function OperatorView({ latestVersion, branch, busy, onSetAutoUpdate }) {
  const current = branch?.currentVersion;
  const updateAvailable =
    latestVersion?.approvedForRollout && current !== latestVersion?.currentVersion;

  const total = Math.max(branch?.gamingPcsTotalCount || 0, 1);
  const upToDate = branch?.gamingPcsUpToDateCount || 0;

  return (
    <div className="space-y-5">
      <div className="card">
        <div className="card-header">
          <h2 className="card-title">{branch?.branchName || 'Your branch'}</h2>
        </div>

        <div className="flex flex-wrap items-center gap-x-10 gap-y-4">
          <div>
            <p className="text-xs uppercase tracking-wider text-text-3 font-bold">Running now</p>
            <p className="font-mono text-3xl font-bold mt-1">
              {current ? `v${current}` : <span className="text-text-3 text-xl">Not reported yet</span>}
            </p>
          </div>

          {updateAvailable && (
            <div>
              <p className="text-xs uppercase tracking-wider text-neon-orange font-bold">Update available</p>
              <p className="font-mono text-3xl font-bold text-neon-orange mt-1">
                v{latestVersion.currentVersion}
              </p>
            </div>
          )}
        </div>

        {latestVersion?.releaseNotes && (
          <div className="bg-bg-3 border border-border rounded-md p-3 mt-4">
            <p className="text-xs uppercase tracking-wider text-text-3 font-bold mb-1">What changed</p>
            <p className="text-sm text-text-2 whitespace-pre-line">{latestVersion.releaseNotes}</p>
          </div>
        )}

        <div className="mt-4 pt-4 border-t border-border space-y-3">
          <AutoUpdateToggle
            checked={!!branch?.autoUpdateEnabled}
            busy={busy}
            onChange={(next) => onSetAutoUpdate(branch?.branchId, next)}
          />

          {updateAvailable && !branch?.autoUpdateEnabled && (
            <button className="btn-primary flex items-center gap-2">
              <Download className="w-4 h-4" /> Update now
            </button>
          )}

          {updateAvailable && branch?.autoUpdateEnabled && (
            <p className="text-sm text-text-2 flex items-center gap-2">
              <RefreshCw className="w-4 h-4 text-pc-active" />
              This will install by itself once no one is mid-session.
            </p>
          )}
        </div>
      </div>

      {branch && (
        <div className="card">
          <div className="card-header">
            <h2 className="card-title">Gaming PCs</h2>
          </div>
          <div className="h-2 bg-bg-4 rounded-full overflow-hidden">
            <div
              className="h-full bg-pc-active transition-all"
              style={{ width: `${Math.round((upToDate / total) * 100)}%` }}
            />
          </div>
          <p className="text-sm text-text-2 mt-2">
            {upToDate} of {branch.gamingPcsTotalCount || 0} up to date
          </p>
        </div>
      )}
    </div>
  );
}

function AutoUpdateToggle({ checked, busy, onChange }) {
  return (
    <label className="flex items-center gap-2.5 cursor-pointer select-none w-fit">
      <input
        type="checkbox"
        checked={checked}
        disabled={busy}
        onChange={(e) => onChange(e.target.checked)}
        className="w-4 h-4 accent-accent cursor-pointer disabled:cursor-wait"
      />
      <span className="text-sm">Install updates automatically</span>
      {busy && <Loader2 className="w-3.5 h-3.5 animate-spin text-text-3" />}
    </label>
  );
}
