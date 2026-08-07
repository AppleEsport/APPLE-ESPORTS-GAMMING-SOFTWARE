import { useEffect, useState } from 'react';
import { useAuth } from '../../context/AuthContext';
import { apiClient } from '../../services/apiClient';
import './UpdatesPage.css';

export default function UpdatesPage() {
  const { user } = useAuth();
  const isSuperAdmin = user?.role === 'Super Admin';

  const [currentVersion, setCurrentVersion] = useState(null);
  const [latestVersion, setLatestVersion] = useState(null);
  const [branches, setBranches] = useState([]);
  const [autoUpdateEnabled, setAutoUpdateEnabled] = useState(false);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState(null);

  useEffect(() => {
    loadVersionInfo();
  }, []);

  const loadVersionInfo = async () => {
    try {
      setLoading(true);

      // Get latest approved version
      const versionRes = await apiClient.get('/api/versions/latest');
      if (versionRes.data?.data) {
        setLatestVersion(versionRes.data.data);
      }

      if (isSuperAdmin) {
        // Load all branch versions
        const branchRes = await apiClient.get('/api/versions/all-branches');
        if (branchRes.data?.data) {
          setBranches(branchRes.data.data);
        }
      } else {
        // Load just this branch's version
        const branchId = user?.branchId;
        if (branchId) {
          const branchRes = await apiClient.get(`/api/versions/branch/${branchId}`);
          if (branchRes.data?.data) {
            const branchStatus = branchRes.data.data;
            setBranches([branchStatus]);
            setCurrentVersion(branchStatus.currentVersion);
            setAutoUpdateEnabled(branchStatus.autoUpdateEnabled);
          }
        }
      }

      setError(null);
    } catch (err) {
      setError(err.message || 'Failed to load version information');
      console.error(err);
    } finally {
      setLoading(false);
    }
  };

  const handleApproveVersion = async (versionInfoId) => {
    try {
      await apiClient.post('/api/versions/approve', { versionInfoId });
      await loadVersionInfo();
    } catch (err) {
      setError('Failed to approve version');
      console.error(err);
    }
  };

  const handleToggleAutoUpdate = async (branchId) => {
    try {
      await apiClient.put(`/api/versions/branch/${branchId}/auto-update`, {
        autoUpdateEnabled: !autoUpdateEnabled
      });
      setAutoUpdateEnabled(!autoUpdateEnabled);
      await loadVersionInfo();
    } catch (err) {
      setError('Failed to update auto-update setting');
      console.error(err);
    }
  };

  if (loading) {
    return <div className="updates-page"><div className="loading">Loading...</div></div>;
  }

  return (
    <div className="updates-page">
      <div className="updates-header">
        <h1>Updates & Versions</h1>
        <p>Manage software updates across your branches</p>
      </div>

      {error && <div className="error-banner">{error}</div>}

      {isSuperAdmin ? (
        <SuperAdminView
          latestVersion={latestVersion}
          branches={branches}
          onApproveVersion={handleApproveVersion}
        />
      ) : (
        <OperatorView
          currentVersion={currentVersion}
          latestVersion={latestVersion}
          branches={branches}
          autoUpdateEnabled={autoUpdateEnabled}
          onToggleAutoUpdate={handleToggleAutoUpdate}
        />
      )}
    </div>
  );
}

function SuperAdminView({ latestVersion, branches, onApproveVersion }) {
  return (
    <div className="super-admin-view">
      <div className="latest-version-card">
        <h2>Latest Version</h2>
        {latestVersion ? (
          <>
            <div className="version-number">v{latestVersion.currentVersion}</div>
            <div className="version-details">
              <p><strong>Status:</strong> {latestVersion.approvedForRollout ? '✅ Approved' : '⏳ Pending Approval'}</p>
              <p><strong>Release Date:</strong> {new Date(latestVersion.createdAt).toLocaleDateString()}</p>
              {latestVersion.releaseNotes && (
                <div className="release-notes">
                  <strong>Release Notes:</strong>
                  <p>{latestVersion.releaseNotes}</p>
                </div>
              )}
            </div>
            {!latestVersion.approvedForRollout && (
              <button
                className="btn-approve"
                onClick={() => onApproveVersion(latestVersion.id)}
              >
                Approve for All Branches
              </button>
            )}
          </>
        ) : (
          <p>No versions available</p>
        )}
      </div>

      <div className="branches-overview">
        <h2>Branch Status</h2>
        <div className="branches-grid">
          {branches.map(branch => (
            <BranchStatusCard key={branch.id} branch={branch} />
          ))}
        </div>
      </div>
    </div>
  );
}

function OperatorView({ currentVersion, latestVersion, branches, autoUpdateEnabled, onToggleAutoUpdate }) {
  const branch = branches[0];
  const updateAvailable = latestVersion?.approvedForRollout && currentVersion !== latestVersion?.currentVersion;

  return (
    <div className="operator-view">
      <div className="current-version-card">
        <h2>Your Branch Version</h2>

        <div className="version-display">
          <div className="current">
            <span>Current Version:</span>
            <span className="version-number">{currentVersion || 'Unknown'}</span>
          </div>

          {updateAvailable && (
            <div className="update-available">
              <span>Update Available:</span>
              <span className="version-number">v{latestVersion.currentVersion}</span>
            </div>
          )}
        </div>

        {latestVersion?.releaseNotes && (
          <div className="release-notes-section">
            <strong>Release Notes:</strong>
            <p>{latestVersion.releaseNotes}</p>
          </div>
        )}

        <div className="update-controls">
          <label className="auto-update-toggle">
            <input
              type="checkbox"
              checked={autoUpdateEnabled}
              onChange={() => onToggleAutoUpdate(branch?.branchId)}
            />
            <span>Enable Auto-Update</span>
          </label>

          {updateAvailable && !autoUpdateEnabled && (
            <button className="btn-update-now">
              Update Now
            </button>
          )}

          {autoUpdateEnabled && updateAvailable && (
            <div className="auto-update-info">
              📦 Update will install automatically at the next safe time
            </div>
          )}
        </div>
      </div>

      {branch && (
        <div className="gaming-pcs-status">
          <h3>Gaming PCs Status</h3>
          <div className="pc-status-bar">
            <div
              className="pc-status-filled"
              style={{ width: `${(branch.gamingPcsUpToDateCount / branch.gamingPcsTotalCount) * 100}%` }}
            />
          </div>
          <p>{branch.gamingPcsUpToDateCount}/{branch.gamingPcsTotalCount} PCs up to date</p>
        </div>
      )}
    </div>
  );
}

function BranchStatusCard({ branch }) {
  const updateAvailable = branch.updateAvailable;
  const allUpdated = branch.gamingPcsUpToDateCount === branch.gamingPcsTotalCount;

  return (
    <div className={`branch-card ${updateAvailable ? 'has-update' : ''} ${allUpdated ? 'all-updated' : ''}`}>
      <h3>{branch.branchName}</h3>

      <div className="version-info">
        <p className="current">v{branch.currentVersion}</p>
        {updateAvailable && (
          <>
            <span className="arrow">→</span>
            <p className="latest">v{branch.latestApprovedVersion}</p>
          </>
        )}
      </div>

      <div className="pc-status">
        <div className="status-bar">
          <div
            className="status-filled"
            style={{ width: `${(branch.gamingPcsUpToDateCount / Math.max(branch.gamingPcsTotalCount, 1)) * 100}%` }}
          />
        </div>
        <p>{branch.gamingPcsUpToDateCount}/{branch.gamingPcsTotalCount} PCs</p>
      </div>

      <div className="auto-update-status">
        {branch.autoUpdateEnabled ? '🔄 Auto-Update ON' : '⏸️ Auto-Update OFF'}
      </div>

      {updateAvailable ? (
        <div className="status-badge update">Update Available</div>
      ) : (
        <div className="status-badge current">Up to Date</div>
      )}
    </div>
  );
}
