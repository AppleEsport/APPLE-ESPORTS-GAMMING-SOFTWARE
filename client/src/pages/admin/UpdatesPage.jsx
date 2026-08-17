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

/**
 * -1 if a is older than b, 0 if equal, 1 if a is newer. Never throws — a version this page
 * cannot parse compares as equal, which is the safe direction: it falls back to "nothing to
 * do" rather than claiming a direction that might be wrong.
 *
 * Exists because "different" and "newer" were being treated as the same question. A branch
 * running something newer than what Head Office currently offers (sent to it directly, then
 * rolled back for everyone else) is not the same situation as one that is behind, and this
 * page used to show the same "Update waiting" banner and "Install now" button for both —
 * which, for the first case, pressed and did nothing.
 */
function compareVersions(a, b) {
  if (!a || !b) return 0;
  const pa = String(a).split('.').map((n) => parseInt(n, 10) || 0);
  const pb = String(b).split('.').map((n) => parseInt(n, 10) || 0);
  for (let i = 0; i < Math.max(pa.length, pb.length); i++) {
    const diff = (pa[i] || 0) - (pb[i] || 0);
    if (diff !== 0) return diff > 0 ? 1 : -1;
  }
  return 0;
}

/** The stages a branch can report, and what to actually say to a human about each. */
const STAGES = {
  // Deliberately not moving, and the only stage that is fine to sit in for hours. An update is
  // never installed while somebody is playing - a counter PC's own upgrade stops the database and
  // API the whole shop bills through - so a busy branch holds here until it is free. It is marked
  // `parked` so it is never mistaken for progress that has stalled: it is not stuck, it is
  // waiting, and the two need to look different or every busy evening reads as a fault.
  waiting:     { label: 'Waiting for the branch to be free', tone: 'wait', done: false, parked: true },
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
  const [release, setRelease] = useState(null);
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
      //
      // /releases/latest is the one that matters, and adding it is the fix for a page that
      // confidently lied. /versions/* reads this machine's OWN database. At Head Office that
      // is the truth; at a branch it is a local copy that nothing ever refreshes, so a counter
      // running 2.2.4 read its own row, found 2.2.4, and printed "This branch has the newest
      // version. There is nothing to do." while 2.2.5 sat on the server waiting.
      //
      // /releases/latest is the very question the updater asks, and on a branch it is passed
      // straight through to Head Office. Asking it here means this page and the thing that
      // actually installs can no longer disagree — which is the whole reason the page exists.
      const [latestRes, historyRes, releaseRes] = await Promise.all([
        api.get('/versions/latest'),
        api.get('/versions/history'),
        api.get('/releases/latest').catch(() => null),   // offline is not an error here
      ]);

      setLatest(latestRes.data?.data ?? null);
      setHistory(historyRes.data?.data ?? []);
      setRelease(releaseRes?.data?.data ?? null);

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
  //
  // A branch parked waiting for its customers does not count as running. It can hold that way
  // for a whole busy evening, and five-second polling for hours - on every Super Admin screen
  // that happens to be open - is a lot of traffic to watch a bar that is deliberately still.
  // It still refreshes, just at the slower pace, so the moment it starts downloading the page
  // picks that up and speeds itself back up.
  const somethingRunning = branches.some(
    (b) => b.updateStage && !STAGES[b.updateStage]?.done && !STAGES[b.updateStage]?.parked
  );

  const somethingWaiting = branches.some((b) => STAGES[b.updateStage]?.parked);

  useEffect(() => {
    if (!somethingRunning && !somethingWaiting) return;
    const id = setInterval(load, somethingRunning ? 5000 : 30000);
    return () => clearInterval(id);
  }, [somethingRunning, somethingWaiting, load]);

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

  // "Approve" never had a way back — the one time this was needed, an update rolled back at
  // Head Office got approved again by mistake, kept offering itself to every branch, and
  // nothing on this page could undo it short of editing the database directly.
  const unapprove = async (versionInfoId) => {
    setBusy('unapprove');
    try {
      await api.post(`/versions/${versionInfoId}/unapprove`);
      setNotice('Taken off the branches. Nobody will be offered this update until it is approved again.');
      await load();
    } catch (err) {
      setError(err.response?.data?.error || 'Could not un-approve this update.');
    } finally {
      setBusy(null);
    }
  };

  const removeVersion = async (versionInfoId, version) => {
    if (!window.confirm(
      `Remove version ${version} entirely?\n\n` +
      'This deletes its record and its installer file. No branch will be offered it again, ' +
      'and the previous version becomes "Newest update" in its place.\n\n' +
      'This cannot be undone from here — only re-adding it the same way it was added the first time.'
    )) return;

    setBusy('remove');
    try {
      await api.delete(`/versions/${versionInfoId}`);
      setNotice(`Version ${version} removed.`);
      await load();
    } catch (err) {
      setError(err.response?.data?.error || 'Could not remove this update.');
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

  // The one control on this page that can go backwards. Everything else only ever hands a
  // branch the newest approved version — this names an exact one, older or newer, and the
  // branch does it because Head Office said so, not because it decided to on its own.
  const installVersion = async (branchId, version) => {
    if (!branchId || !version) return;
    setBusy(branchId);
    try {
      const res = await api.post(`/versions/branch/${branchId}/install`, { version });
      setNotice(res.data?.data?.message || `Sent version ${version} to this branch.`);
      setError(null);
      await load();
    } catch (err) {
      setError(err.response?.data?.error || 'Could not send that version.');
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
              onUnapprove={unapprove}
              onRemove={removeVersion}
              onPublish={publish}
              onSetAutoUpdate={setAutoUpdate}
              onInstallVersion={installVersion}
            />
          ) : (
            <BranchView
              latest={latest}
              release={release}
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

function OwnerView({ latest, branches, busy, onApprove, onUnapprove, onRemove, onPublish, onSetAutoUpdate, onInstallVersion }) {
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

            {!latest.approvedForRollout ? (
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
            ) : (
              <div className="pt-1">
                <button
                  className="btn-secondary flex items-center gap-2"
                  disabled={busy === 'unapprove'}
                  onClick={() => onUnapprove(latest.id)}
                >
                  {busy === 'unapprove'
                    ? <Loader2 className="w-4 h-4 animate-spin" />
                    : <XCircle className="w-4 h-4" />}
                  Un-approve — stop offering this to branches
                </button>
                <p className="text-text-3 text-xs mt-2">
                  Keeps the record and the installer, just stops any branch from being sent
                  this version until it is approved again.
                </p>
              </div>
            )}

            <div className="pt-2 border-t border-border/60">
              <button
                type="button"
                onClick={() => onRemove(latest.id, latest.currentVersion)}
                disabled={busy === 'remove'}
                className="text-[11px] text-text-3 hover:text-neon-red underline decoration-dotted decoration-text-3/50"
              >
                {busy === 'remove' ? 'Removing…' : 'Remove this update entirely'}
              </button>
            </div>
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
                onInstallVersion={onInstallVersion}
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

function BranchCard({ branch, latest, busy, onSetAutoUpdate, onInstallVersion }) {
  const total = branch.gamingPcsTotalCount || 0;
  const upToDate = branch.gamingPcsUpToDateCount || 0;
  const pct = total > 0 ? Math.round((upToDate / total) * 100) : 0;
  const [showInstall, setShowInstall] = useState(false);
  const [installTarget, setInstallTarget] = useState('');

  // The server's own updateAvailable only checks "different", not "newer" - correct here
  // rather than there, since this is display only and nothing downstream gates on it. A
  // branch sent a version directly (see the control below) and later left behind when that
  // version was rolled back for everyone else is ahead, not waiting - it should never show
  // the same orange "Update waiting" badge as a branch that is genuinely behind.
  const aheadOfOffered = branch.updateAvailable && branch.latestApprovedVersion
    && compareVersions(branch.latestApprovedVersion, branch.currentVersion) < 0;
  const genuinelyWaiting = branch.updateAvailable && !aheadOfOffered;

  return (
    <div className={`bg-bg-3 border rounded-md p-4 space-y-3 ${
      genuinelyWaiting ? 'border-neon-orange/50' : 'border-border'
    }`}>
      <div className="flex items-start justify-between gap-2">
        <h3 className="font-heading font-bold">{branch.branchName}</h3>
        {genuinelyWaiting ? (
          <span className="badge badge-awaiting">Update waiting</span>
        ) : aheadOfOffered ? (
          <span className="badge badge-awaiting">Ahead of offered</span>
        ) : (
          <span className="badge badge-active">Up to date</span>
        )}
      </div>

      <div className="flex items-center gap-2 font-mono text-sm">
        <span>{branch.currentVersion || '—'}</span>
        {genuinelyWaiting && branch.latestApprovedVersion && (
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

      {/* The one control here that can go backwards. Kept out of the way on purpose — this is
          for putting a branch back on an older version, not something anyone should reach for
          without meaning to. */}
      <div className="pt-2 border-t border-border/60">
        {!showInstall ? (
          <button
            type="button"
            onClick={() => setShowInstall(true)}
            className="text-[11px] text-text-3 hover:text-neon-orange underline decoration-dotted decoration-text-3/50"
          >
            Send this branch a specific version
          </button>
        ) : (
          <div className="space-y-2">
            <p className="text-[11px] text-text-3 leading-relaxed">
              For putting this branch back on an older version, or ahead of what has been sent
              to everyone. This branch will stop and restart its own services to do it.
            </p>
            <div className="flex items-center gap-2">
              <input
                type="text"
                value={installTarget}
                onChange={(e) => setInstallTarget(e.target.value)}
                placeholder="2.4.8"
                className="input font-mono text-sm flex-1 py-1.5"
              />
              <button
                type="button"
                className="btn-secondary text-xs px-3 py-1.5 shrink-0"
                disabled={!installTarget.trim() || busy}
                onClick={() => {
                  const target = installTarget.trim();
                  if (!window.confirm(
                    `Send version ${target} to ${branch.branchName}?\n\n` +
                    'It will download this, check it against the hash Head Office published, ' +
                    'and install it — stopping and restarting its own services to do so.\n\n' +
                    'If that fails partway, Head Office loses its only way to reach this branch ' +
                    'remotely until someone is physically there, or on remote desktop to it.\n\n' +
                    'Only continue if you mean it.'
                  )) return;
                  onInstallVersion(branch.branchId, target);
                  setInstallTarget('');
                  setShowInstall(false);
                }}
              >
                Send
              </button>
              <button
                type="button"
                className="text-text-3 text-xs hover:text-text px-1"
                onClick={() => { setShowInstall(false); setInstallTarget(''); }}
              >
                Cancel
              </button>
            </div>
          </div>
        )}
      </div>
    </div>
  );
}

// ── An operator's or admin's view of their own branch ──

function BranchView({ latest, release, branch, busy, onSetAutoUpdate }) {
  const current = branch?.currentVersion;

  // Head Office's live answer, which is also what the updater goes on. The old code compared
  // against this branch's own database — a copy that never learns about anything new — so the
  // page reported "nothing to do" for a version it had simply never been told about.
  const offered = release?.available ? release.version : null;

  // Not "different" — "newer". A branch can end up running something newer than what Head
  // Office currently offers (a version sent to it directly and later rolled back for
  // everyone else), and the branch's own updater refuses on principle to install anything
  // that isn't strictly newer than what it already has (UpdateService.CheckAsync). This page
  // used to show "Update waiting" and an "Install now" button for that case too, which pressed
  // and did nothing — the button called the same refusal, silently. It looked like it worked.
  // It did not.
  const comparison = offered && current ? compareVersions(offered, current) : 0;
  const updateWaiting = !!offered && comparison > 0;
  const branchIsAhead = !!offered && comparison < 0;

  // Only inside the desktop app is there something able to install. In an ordinary browser
  // there is not, so the button is not offered rather than offered and doing nothing.
  const canInstallHere = typeof window !== 'undefined' && !!window.chrome?.webview;
  const [checking, setChecking] = useState(false);

  const checkNow = () => {
    window.chrome.webview.postMessage('check-for-updates');
    setChecking(true);
  };

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
                {offered}
              </p>
            </div>
          )}
        </div>

        {updateWaiting && (
          <div className="mt-4">
            <WhatChanged notes={release?.releaseNotes || latest?.releaseNotes} />
          </div>
        )}

        {branchIsAhead && (
          <p className="text-text-2 text-sm mt-4">
            This branch is running <span className="font-mono">{current}</span>, ahead of{' '}
            <span className="font-mono">{offered}</span> — the version currently offered for
            new installs. There is nothing to do here. If this branch needs to go back to an
            older version, that has to be sent from Head Office directly; this screen cannot do
            it, and pressing anything below will not either.
          </p>
        )}

        {!updateWaiting && !branchIsAhead && current && release && (
          <p className="text-text-2 text-sm mt-4">
            This branch has the newest version. There is nothing to do.
          </p>
        )}

        {/* Head Office could not be reached. Said plainly, because "up to date" would be a
            guess — the branch has no idea what exists until it can ask. */}
        {!release && current && (
          <p className="text-text-2 text-sm mt-4">
            Could not reach Head Office just now, so there is no way to tell whether an update
            is waiting. Your branch keeps working normally, and it will keep trying.
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

          {/* Always available, not only when an update is waiting. Being able to press this
              and see "you are on the newest version" is the difference between knowing updates
              work and assuming they are broken — which is exactly the doubt this page kept
              creating when it had nothing to press at all. */}
          {canInstallHere && (
            <div>
              <button
                className={updateWaiting ? 'btn-primary flex items-center gap-2' : 'btn-secondary flex items-center gap-2'}
                disabled={checking}
                onClick={checkNow}
              >
                {checking
                  ? <Loader2 className="w-4 h-4 animate-spin" />
                  : <Download className="w-4 h-4" />}
                {updateWaiting ? `Install ${offered} now` : 'Check for updates now'}
              </button>

              <p className="text-text-3 text-xs mt-2 leading-relaxed">
                {checking
                  ? 'Looking now. If there is an update it downloads in the background and the ' +
                    'app restarts itself when it is ready — that can take a few minutes on a ' +
                    'slow line. You can carry on working.'
                  : 'This branch looks for updates by itself every half a minute, and again ' +
                    'as soon as the PC is switched on. Press this if you do not want to wait ' +
                    'even that long.'}
              </p>
            </div>
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

  // `parked` is excluded on purpose. A branch waiting for its last customer to finish has not
  // moved for twenty minutes and is perfectly healthy - on a busy evening it could hold for
  // hours. Warning about that would cry wolf every night and teach the owner to ignore the one
  // warning that matters.
  const stuck = !meta.done && !meta.parked && changedAt &&
    (Date.now() - changedAt.getTime()) > STUCK_AFTER_MINUTES * 60 * 1000;

  const barColour = failed
    ? 'bg-neon-red'
    : meta.tone === 'green'
      ? 'bg-neon-green'
      : meta.tone === 'wait'
        ? 'bg-neon-orange'
        : 'bg-accent';

  return (
    <div className="space-y-2">
      <div className="flex items-center justify-between gap-2 text-sm">
        <span className="flex items-center gap-2 font-bold">
          {failed
            ? <XCircle className="w-4 h-4 text-neon-red" />
            : meta.done
              ? <CheckCircle2 className="w-4 h-4 text-neon-green" />
              : meta.parked
                // A clock, not a spinner. A spinning icon says "working on it, any moment now";
                // this branch is doing nothing and will keep doing nothing until its customers
                // leave, which could be hours. The icon has to say waiting, not working.
                ? <Clock className="w-4 h-4 text-neon-orange" />
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
