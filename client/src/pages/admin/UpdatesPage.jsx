import { useEffect, useState, useCallback } from 'react';
import { useAuth } from '../../contexts/AuthContext';
import { ROLES } from '../../config/constants';
import api from '../../config/api';
import {
  CheckCircle2, Clock, Download, AlertCircle, Loader2, History,
  Send, RefreshCw, XCircle,
} from 'lucide-react';

/**
 * Updates — the same page for the owner, admins and operators, each seeing their own scope.
 *
 * Written in plain English on purpose. The people reading this are behind a counter in Surat,
 * not developers: "version", "update" and "what changed" are the only technical words allowed
 * anywhere on it. No "artefact", no "rollout", no "deployment", no version-number jargon.
 *
 * One more rule, which is why parts of this page say a thing cannot be done yet: nothing here
 * is allowed to look like it works when it does not. The bar shows progress the branch has
 * actually reported, and "Update Now" only appears when there is genuinely an installer for a
 * branch to fetch. A dashboard that pretends is worse than one that admits — the first time an
 * operator trusts a full progress bar and the update never arrived, they stop trusting the page
 * for good.
 */

// ── Small shared helpers ──

/** "11 Aug 2026, 7:24 pm" — how a person writes a date, not an ISO timestamp. */
function when(value) {
  if (!value) return null;
  const d = new Date(value);
  if (Number.isNaN(d.getTime())) return null;
  return d.toLocaleString('en-IN', {
    day: '2-digit', month: 'short', year: 'numeric',
    hour: 'numeric', minute: '2-digit', hour12: true,
  });
}

/** The stages a branch can report, and what to actually say to a human about each. */
const STAGES = {
  downloading: { label: 'Downloading the update', tone: 'accent', done: false },
  installing:  { label: 'Installing it',          tone: 'accent', done: false },
  restarting:  { label: 'Restarting',             tone: 'accent', done: false },
  done:        { label: 'Finished',               tone: 'green',  done: true  },
  failed:      { label: 'It did not finish',      tone: 'red',    done: true  },
};

/** Nothing has moved in this long → almost certainly stuck rather than slow. */
const STUCK_AFTER_MINUTES = 20;

export default function UpdatesPage() {
  const { user } = useAuth();

  // Roles arrive as the stored values ("super_admin"), not display labels. Comparing against
  // 'Super Admin' silently made every Super Admin look like an operator, so the approval
  // controls were unreachable for the only person allowed to use them.
  const role = user?.role || user?.Role;
  const isSuperAdmin = role === ROLES.SUPER_ADMIN;

  const [latest, setLatest] = useState(null);
  const [history, setHistory] = useState([]);
  const [branches, setBranches] = useState([]);
  const [loading, setLoading] = useState(true);
  const [busy, setBusy] = useState(null);
  const [error, setError] = useState(null);
  const [notice, setNotice] = useState(null);

  const load = useCallback(async () => {
    try {
      setLoading(true);

      // No '/api' prefix — the shared client is already based at /api, so writing it again
      // produced /api/api/... and every call 404'd.
      const [latestRes, historyRes] = await Promise.all([
        api.get('/versions/latest'),
        api.get('/versions/history'),
      ]);

      setLatest(latestRes.data?.data ?? null);
      setHistory(historyRes.data?.data ?? []);

      if (isSuperAdmin) {
        const res = await api.get('/versions/all-branches');
        setBranches(res.data?.data ?? []);
      } else if (user?.branchId) {
        const res = await api.get(`/versions/branch/${user.branchId}`);
        setBranches(res.data?.data ? [res.data.data] : []);
      } else {
        setBranches([]);
      }

      setError(null);
    } catch (err) {
      setError(err.response?.data?.error || err.message || 'Could not load the update information.');
    } finally {
      setLoading(false);
    }
  }, [isSuperAdmin, user?.branchId]);

  useEffect(() => { load(); }, [load]);

  // While an update is actually running somewhere, refresh often enough that the bar moves.
  // Polling all the time would be pointless traffic — most of the time nothing is happening.
  const somethingRunning = branches.some(
    (b) => b.updateStage && !STAGES[b.updateStage]?.done
  );

  useEffect(() => {
    if (!somethingRunning) return;
    const id = setInterval(load, 5000);
    return () => clearInterval(id);
  }, [somethingRunning, load]);

  const approve = async (versionInfoId) => {
    setBusy('approve');
    try {
      await api.post('/versions/approve', { versionInfoId });
      setNotice('Approved. Every branch has been emailed to tell them an update is waiting.');
      await load();
    } catch (err) {
      setError(err.response?.data?.error || 'Could not approve this update.');
    } finally {
      setBusy(null);
    }
  };

  const publish = async (version, releaseNotes) => {
    setBusy('publish');
    try {
      await api.post('/versions/create', { version, releaseNotes });
      setNotice('Saved. Nothing has gone out to the branches yet — approve it when you are ready.');
      await load();
    } catch (err) {
      setError(err.response?.data?.error || 'Could not save this update.');
    } finally {
      setBusy(null);
    }
  };

  const setAutoUpdate = async (branchId, enabled) => {
    if (!branchId) return;
    setBusy(branchId);
    try {
      await api.put(`/versions/branch/${branchId}/auto-update`, { autoUpdateEnabled: enabled });
      await load();
      setError(null);
    } catch (err) {
      setError(err.response?.data?.error || 'Could not change that setting.');
    } finally {
      setBusy(null);
    }
  };

  return (
    <div className="space-y-5">
      <div>
        <h1 className="text-2xl font-heading font-bold tracking-wide">Updates</h1>
        <p className="text-text-2 text-sm mt-1">
          {isSuperAdmin
            ? 'Approve an update once and every branch is told about it.'
            : 'What version this branch is running, and what changed in it.'}
        </p>
      </div>

      {error && (
        <div className="flex items-start gap-2 bg-neon-red-dim border border-neon-red/40 text-neon-red rounded-md px-4 py-3 text-sm">
          <AlertCircle className="w-4 h-4 mt-0.5 shrink-0" />
          <span>{error}</span>
        </div>
      )}

      {notice && (
        <div className="flex items-start gap-2 bg-neon-green-dim border border-neon-green/40 text-neon-green rounded-md px-4 py-3 text-sm">
          <CheckCircle2 className="w-4 h-4 mt-0.5 shrink-0" />
          <span>{notice}</span>
        </div>
      )}

      {loading ? (
        <div className="card flex items-center gap-2 text-text-2">
          <Loader2 className="w-4 h-4 animate-spin" /> Loading…
        </div>
      ) : (
        <>
          {isSuperAdmin ? (
            <OwnerView
              latest={latest}
              branches={branches}
              busy={busy}
              onApprove={approve}
              onPublish={publish}
              onSetAutoUpdate={setAutoUpdate}
            />
          ) : (
            <BranchView
              latest={latest}
              branch={branches[0]}
              busy={busy}
              onSetAutoUpdate={setAutoUpdate}
            />
          )}

          <UpdateHistory versions={history} />
        </>
      )}
    </div>
  );
}

// ── The owner's view ──

function OwnerView({ latest, branches, busy, onApprove, onPublish, onSetAutoUpdate }) {
  return (
    <div className="space-y-5">
      <div className="card">
        <div className="card-header">
          <h2 className="card-title">Newest update</h2>
        </div>

        {latest ? (
          <div className="space-y-4">
            <div className="flex flex-wrap items-center gap-3">
              <span className="font-mono text-3xl font-bold">{latest.currentVersion}</span>
              {latest.approvedForRollout ? (
                <span className="badge badge-active flex items-center gap-1">
                  <CheckCircle2 className="w-3 h-3" /> Approved — branches have been told
                </span>
              ) : (
                <span className="badge badge-awaiting flex items-center gap-1">
                  <Clock className="w-3 h-3" /> Not approved yet
                </span>
              )}
            </div>

            <p className="text-text-2 text-sm">
              Added {when(latest.createdAt)}
              {latest.approvedAt && ` · approved ${when(latest.approvedAt)}`}
            </p>

            <WhatChanged notes={latest.releaseNotes} />

            {!latest.approvedForRollout && (
              <div className="pt-1">
                <button
                  className="btn-primary flex items-center gap-2"
                  disabled={busy === 'approve'}
                  onClick={() => onApprove(latest.id)}
                >
                  {busy === 'approve'
                    ? <Loader2 className="w-4 h-4 animate-spin" />
                    : <Send className="w-4 h-4" />}
                  Approve and tell every branch
                </button>
                <p className="text-text-3 text-xs mt-2">
                  This emails every operator to say an update is waiting. Branches set to update
                  by themselves will take it on their own.
                </p>
              </div>
            )}
          </div>
        ) : (
          <p className="text-text-2 text-sm">
            No updates have been added yet. Add the first one below.
          </p>
        )}
      </div>

      <PublishForm busy={busy === 'publish'} onPublish={onPublish} />

      <div className="card">
        <div className="card-header">
          <h2 className="card-title">Branches</h2>
        </div>

        {branches.length === 0 ? (
          <p className="text-text-2 text-sm">
            No branch has reported in yet. A branch appears here the first time it tells Head
            Office which version it is running.
          </p>
        ) : (
          <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-3">
            {branches.map((branch) => (
              <BranchCard
                key={branch.branchId || branch.id}
                branch={branch}
                latest={latest}
                busy={busy === branch.branchId}
                onSetAutoUpdate={onSetAutoUpdate}
              />
            ))}
          </div>
        )}
      </div>
    </div>
  );
}

function PublishForm({ busy, onPublish }) {
  const [version, setVersion] = useState('');
  const [notes, setNotes] = useState('');

  const ready = version.trim().length > 0 && notes.trim().length > 0;

  const submit = () => {
    if (!ready) return;
    onPublish(version.trim(), notes.trim());
    setVersion('');
    setNotes('');
  };

  return (
    <div className="card">
      <div className="card-header">
        <h2 className="card-title">Add a new update</h2>
      </div>

      <div className="space-y-4">
        <div>
          <label className="block text-text-3 text-[11px] uppercase tracking-wider mb-2">
            Version number
          </label>
          <input
            type="text"
            value={version}
            onChange={(e) => setVersion(e.target.value)}
            placeholder="2.2.0"
            className="input w-full sm:w-48 font-mono"
          />
        </div>

        <div>
          <label className="block text-text-3 text-[11px] uppercase tracking-wider mb-2">
            What changed
          </label>
          <textarea
            value={notes}
            onChange={(e) => setNotes(e.target.value)}
            rows={5}
            placeholder={
              'Write it the way you would tell an operator, one line each. For example:\n\n' +
              'Fixed the cash count showing the wrong opening amount.\n' +
              'The end-of-day email now matches the End of Day screen.'
            }
            className="input w-full text-sm leading-relaxed"
          />
          {/* Deliberately required. These notes are the only thing an operator can read before
              deciding to update, and "various fixes" tells them nothing. */}
          <p className="text-text-3 text-xs mt-2">
            Every operator reads this before they update, so write it in plain words.
          </p>
        </div>

        <button
          className="btn-primary flex items-center gap-2"
          disabled={!ready || busy}
          onClick={submit}
        >
          {busy ? <Loader2 className="w-4 h-4 animate-spin" /> : null}
          Save this update
        </button>
        <p className="text-text-3 text-xs">
          Saving does not send anything. You approve it separately, above.
        </p>
      </div>
    </div>
  );
}

function BranchCard({ branch, latest, busy, onSetAutoUpdate }) {
  const total = branch.gamingPcsTotalCount || 0;
  const upToDate = branch.gamingPcsUpToDateCount || 0;
  const pct = total > 0 ? Math.round((upToDate / total) * 100) : 0;

  return (
    <div className={`bg-bg-3 border rounded-md p-4 space-y-3 ${
      branch.updateAvailable ? 'border-neon-orange/50' : 'border-border'
    }`}>
      <div className="flex items-start justify-between gap-2">
        <h3 className="font-heading font-bold">{branch.branchName}</h3>
        {branch.updateAvailable ? (
          <span className="badge badge-awaiting">Update waiting</span>
        ) : (
          <span className="badge badge-active">Up to date</span>
        )}
      </div>

      <div className="flex items-center gap-2 font-mono text-sm">
        <span>{branch.currentVersion || '—'}</span>
        {branch.updateAvailable && branch.latestApprovedVersion && (
          <>
            <span className="text-text-3">→</span>
            <span className="text-neon-orange">{branch.latestApprovedVersion}</span>
          </>
        )}
      </div>

      <UpdateProgress branch={branch} latest={latest} />

      <div>
        <div className="h-1.5 bg-bg-4 rounded-full overflow-hidden">
          <div className="h-full bg-pc-active transition-all" style={{ width: `${pct}%` }} />
        </div>
        <p className="text-xs text-text-2 mt-1">
          {total === 0
            ? 'No gaming PCs reported'
            : `${upToDate} of ${total} gaming PCs up to date`}
        </p>
      </div>

      {/* The owner can switch a branch to automatic from here, rather than asking the operator
          at that branch to do it themselves. */}
      <AutoUpdateToggle
        checked={!!branch.autoUpdateEnabled}
        busy={busy}
        onChange={(next) => onSetAutoUpdate(branch.branchId, next)}
      />

      <p className="text-text-3 text-[11px]">
        Last heard from {when(branch.lastCheckedForUpdates) || 'never'}
      </p>
    </div>
  );
}

// ── An operator's or admin's view of their own branch ──

function BranchView({ latest, branch, busy, onSetAutoUpdate }) {
  const current = branch?.currentVersion;
  const updateWaiting = !!latest?.approvedForRollout && current !== latest?.currentVersion;

  const total = branch?.gamingPcsTotalCount || 0;
  const upToDate = branch?.gamingPcsUpToDateCount || 0;

  return (
    <div className="space-y-5">
      <div className="card">
        <div className="card-header">
          <h2 className="card-title">{branch?.branchName || 'Your branch'}</h2>
        </div>

        <div className="flex flex-wrap items-start gap-x-12 gap-y-4">
          <div>
            <p className="text-xs uppercase tracking-wider text-text-3 font-bold">Running now</p>
            <p className="font-mono text-3xl font-bold mt-1">
              {current || <span className="text-text-3 text-xl font-sans">Not reported yet</span>}
            </p>
          </div>

          {updateWaiting && (
            <div>
              <p className="text-xs uppercase tracking-wider text-neon-orange font-bold">
                Update waiting
              </p>
              <p className="font-mono text-3xl font-bold text-neon-orange mt-1">
                {latest.currentVersion}
              </p>
            </div>
          )}
        </div>

        {updateWaiting && <div className="mt-4"><WhatChanged notes={latest.releaseNotes} /></div>}

        {!updateWaiting && current && (
          <p className="text-text-2 text-sm mt-4">
            This branch has the newest version. There is nothing to do.
          </p>
        )}

        <div className="mt-5 pt-4 border-t border-border space-y-4">
          <UpdateProgress branch={branch} latest={latest} />

          <div>
            <AutoUpdateToggle
              checked={!!branch?.autoUpdateEnabled}
              busy={busy === branch?.branchId}
              onChange={(next) => onSetAutoUpdate(branch?.branchId, next)}
            />
            <p className="text-text-3 text-xs mt-2 leading-relaxed">
              {branch?.autoUpdateEnabled
                ? 'Leave this on and updates install by themselves, in the background. Nobody ' +
                  'playing will be interrupted, and you do not need to watch this page.'
                : 'This is off, so updates wait for you to press Update Now. Turning it on is ' +
                  'usually better — nothing installs until the owner has approved it anyway.'}
            </p>
          </div>

          {updateWaiting && !latest?.hasInstaller && (
            <p className="text-text-2 text-sm flex items-start gap-2">
              <Clock className="w-4 h-4 mt-0.5 shrink-0 text-text-3" />
              <span>
                This update cannot install itself yet — the part of the program that does that
                is still being built. Nothing is wrong at your branch.
              </span>
            </p>
          )}

          {updateWaiting && latest?.hasInstaller && !branch?.autoUpdateEnabled && (
            <button className="btn-primary flex items-center gap-2">
              <Download className="w-4 h-4" /> Update now
            </button>
          )}
        </div>
      </div>

      {branch && total > 0 && (
        <div className="card">
          <div className="card-header">
            <h2 className="card-title">Gaming PCs at this branch</h2>
          </div>
          <div className="h-2 bg-bg-4 rounded-full overflow-hidden">
            <div
              className="h-full bg-pc-active transition-all"
              style={{ width: `${Math.round((upToDate / total) * 100)}%` }}
            />
          </div>
          <p className="text-sm text-text-2 mt-2">{upToDate} of {total} up to date</p>
        </div>
      )}
    </div>
  );
}

// ── The progress bar ──

/**
 * Shows only what the branch has actually reported.
 *
 * When nothing is running this renders nothing at all, rather than an empty bar — an empty bar
 * reads as "stuck at zero", which is the one state it must never imply by accident.
 */
function UpdateProgress({ branch, latest }) {
  const stage = branch?.updateStage;
  if (!stage) return null;

  const meta = STAGES[stage];
  if (!meta) return null;

  // "done" is only worth showing until the branch's version catches up; after that the version
  // number itself says it, and a permanent "Finished" bar is just clutter.
  if (stage === 'done' && branch?.currentVersion === latest?.currentVersion) return null;

  const failed = stage === 'failed';
  const pct = failed ? 100 : Math.max(2, branch.updateProgressPercent || 0);

  const changedAt = branch.updateStageChangedAt ? new Date(branch.updateStageChangedAt) : null;
  const stuck = !meta.done && changedAt &&
    (Date.now() - changedAt.getTime()) > STUCK_AFTER_MINUTES * 60 * 1000;

  const barColour = failed ? 'bg-neon-red' : meta.tone === 'green' ? 'bg-neon-green' : 'bg-accent';

  return (
    <div className="space-y-2">
      <div className="flex items-center justify-between gap-2 text-sm">
        <span className="flex items-center gap-2 font-bold">
          {failed
            ? <XCircle className="w-4 h-4 text-neon-red" />
            : meta.done
              ? <CheckCircle2 className="w-4 h-4 text-neon-green" />
              : <RefreshCw className="w-4 h-4 text-accent animate-spin" />}
          {meta.label}
        </span>
        {!meta.done && (
          <span className="font-mono text-text-2 text-xs">{branch.updateProgressPercent || 0}%</span>
        )}
      </div>

      <div className="h-2 bg-bg-4 rounded-full overflow-hidden">
        <div className={`h-full transition-all duration-500 ${barColour}`} style={{ width: `${pct}%` }} />
      </div>

      {branch.updateMessage && (
        <p className={`text-xs leading-relaxed ${failed ? 'text-neon-red' : 'text-text-2'}`}>
          {branch.updateMessage}
        </p>
      )}

      {stuck && (
        <p className="text-xs text-neon-orange leading-relaxed">
          This has not moved since {when(branch.updateStageChangedAt)}. It may have stopped —
          tell the owner if it stays like this. Your branch keeps working normally in the meantime.
        </p>
      )}

      {failed && (
        <p className="text-xs text-text-2 leading-relaxed">
          Your branch is still running the version it had before, so nothing is broken. It will
          try again on its own.
        </p>
      )}
    </div>
  );
}

// ── What changed, and the history ──

function WhatChanged({ notes }) {
  if (!notes || !notes.trim()) {
    return (
      <div className="bg-bg-3 border border-border rounded-md p-3">
        <p className="text-sm text-text-3">No details were written for this update.</p>
      </div>
    );
  }

  return (
    <div className="bg-bg-3 border border-border rounded-md p-3">
      <p className="text-xs uppercase tracking-wider text-text-3 font-bold mb-2">What changed</p>
      {/* whitespace-pre-line, so the line breaks the owner typed survive. */}
      <p className="text-sm text-text-2 whitespace-pre-line leading-relaxed">{notes.trim()}</p>
    </div>
  );
}

/**
 * Every update ever, newest first, with what was in it.
 *
 * Kept because this is the only place anyone at a branch can find out what changed and when.
 * Collapsed by default so the newest update stays the thing you see first.
 */
function UpdateHistory({ versions }) {
  const [openId, setOpenId] = useState(null);

  if (!versions || versions.length === 0) return null;

  return (
    <div className="card">
      <div className="card-header">
        <h2 className="card-title flex items-center gap-2">
          <History className="w-4 h-4" /> Past updates
        </h2>
      </div>

      <p className="text-text-2 text-sm mb-3">
        Every update, newest first. Click one to read what was in it.
      </p>

      <div className="divide-y divide-border">
        {versions.map((v) => {
          const open = openId === v.id;
          return (
            <div key={v.id} className="py-3">
              <button
                className="w-full flex items-center justify-between gap-3 text-left"
                onClick={() => setOpenId(open ? null : v.id)}
              >
                <span className="flex items-center gap-3 min-w-0">
                  <span className="font-mono font-bold">{v.currentVersion}</span>
                  {v.approvedForRollout ? (
                    <span className="badge badge-active shrink-0">Sent to branches</span>
                  ) : (
                    <span className="badge badge-awaiting shrink-0">Not approved</span>
                  )}
                </span>
                <span className="text-text-3 text-xs shrink-0">
                  {when(v.approvedAt) || when(v.createdAt)}
                </span>
              </button>

              {open && (
                <div className="mt-3">
                  <WhatChanged notes={v.releaseNotes} />
                </div>
              )}
            </div>
          );
        })}
      </div>
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
      <span className="text-sm font-bold">Install updates by themselves</span>
      {busy && <Loader2 className="w-3.5 h-3.5 animate-spin text-text-3" />}
    </label>
  );
}
