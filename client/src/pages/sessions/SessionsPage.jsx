import { useState, useEffect, useCallback, useMemo } from 'react';
import { MonitorPlay, MonitorOff, IndianRupee, Clock, ShieldAlert, Banknote, Minus, Plus } from 'lucide-react';
import { useAuth } from '../../contexts/AuthContext';
import { useBranch } from '../../contexts/BranchContext';
import { useSocket } from '../../contexts/SocketContext';
import api from '../../config/api';

import PcGrid from '../../components/sessions/PcGrid';
import { TILE_SIZES } from '../../components/sessions/PcTile';
import PcDetailPanel from '../../components/sessions/PcDetailPanel';
import QuickStartModal from '../../components/sessions/QuickStartModal';
import SessionActivityLog from '../../components/sessions/SessionActivityLog';
import { MaintenanceReasonModal } from '../../components/modals/MaintenanceReasonModal';
import { useToast } from '../../components/ui/Toast';
import { startReservedSession, overrideReservation } from '../../api/reservations.api';
import { getRangeReport } from '../../api/food.api';
import { getActiveBills, getBill, processPayment } from '../../api/billing.api';
import { markMaintenanceAsync, resolveMaintenance } from '../../api/maintenanceLogs.api';
import { logActivity } from '../../utils/sessionLog';
import { roundBillTotal } from '../../utils/billRounding';
import { useNavigate } from 'react-router-dom';

export default function SessionsPage() {
  const { isSuperAdmin, user } = useAuth();
  const { activeBranch } = useBranch();
  const { subscribe, connected, SIGNALR_HUBS } = useSocket();
  const toast = useToast();
  const navigate = useNavigate();

  const [pcs, setPcs] = useState([]);
  const [isLoading, setIsLoading] = useState(true);
  const [selectedPcId, setSelectedPcId] = useState(null); // PC shown in the detail panel
  const [quickStartPc, setQuickStartPc] = useState(null); // PC being quick-started via double-click
  const [tileSizeIndex, setTileSizeIndex] = useState(1); // index into TILE_SIZES — controls PC tile size

  // Reservation Override modal states
  const [overrideData, setOverrideData] = useState(null); // { id, pcName }
  const [overrideReason, setOverrideReason] = useState('');
  const [overrideLoading, setOverrideLoading] = useState(false);

  // Maintenance modal states
  const [maintenanceModalOpen, setMaintenanceModalOpen] = useState(false);
  const [maintenancePc, setMaintenancePc] = useState(null);
  const [maintenanceIsMarking, setMaintenanceIsMarking] = useState(true); // true to mark, false to resolve
  const [maintenanceLoading, setMaintenanceLoading] = useState(false);

  // Walk-in requests state
  const [walkinRequests, setWalkinRequests] = useState([]);

  // Activity log height — fixed to the bottom of the viewport, adjustable via drag handle
  const [logHeight, setLogHeight] = useState(140);

  const targetBranchId = isSuperAdmin ? activeBranch?.id : user?.branchId;

  const fetchPcs = useCallback(async (silent = false) => {
    if (!targetBranchId) {
      if (!silent) setPcs([]);
      if (!silent) setIsLoading(false);
      return;
    }
    if (!silent) setIsLoading(true);
    try {
      const { data } = await api.get('/pcs', { params: { branchId: targetBranchId } });
      const sorted = (data?.data || []).sort((a, b) =>
        a.name.localeCompare(b.name, undefined, { numeric: true })
      );
      // Only update if data actually changed
      setPcs(prev => {
        const prevJson = JSON.stringify(prev?.map(p => ({ id: p.id, state: p.state, totalAmount: p.totalAmount })));
        const newJson = JSON.stringify(sorted?.map(p => ({ id: p.id, state: p.state, totalAmount: p.totalAmount })));
        return prevJson !== newJson ? sorted : prev;
      });
    } catch (err) {
      console.error('Failed to load PCs', err);
    } finally {
      if (!silent) setIsLoading(false);
    }
  }, [targetBranchId]);

  const handleStartReservedSession = async (reservationId) => {
    try {
      await startReservedSession(reservationId);
      toast.success('Reserved session started successfully!');
      logActivity('Reserved session started.', 'success');
      fetchPcs();
    } catch (err) {
      toast.error(err.response?.data?.error || err.response?.data?.message || 'Failed to start reserved session');
    }
  };

  const handleOverrideClick = (reservationId, pc) => {
    setOverrideData({ id: reservationId, pcName: pc.name });
    setOverrideReason('');
  };

  const handleFlagMaintenance = async (pc, enable = true) => {
    if (enable) {
      setMaintenancePc(pc);
      setMaintenanceIsMarking(true);
      setMaintenanceModalOpen(true);
    } else {
      setMaintenancePc(pc);
      setMaintenanceIsMarking(false);
      setMaintenanceModalOpen(true);
    }
  };

  const handleMaintenanceConfirm = async (reason) => {
    if (!maintenancePc) return;

    setMaintenanceLoading(true);
    try {
      if (maintenanceIsMarking) {
        await markMaintenanceAsync(maintenancePc.id, reason, targetBranchId);
        toast.success(`${maintenancePc.name} flagged for maintenance.`);
        logActivity(`${maintenancePc.name}: Flagged for maintenance - ${reason}`, 'warn');
      } else {
        await resolveMaintenance(maintenancePc.id, reason);
        toast.success(`${maintenancePc.name} resolved from maintenance.`);
        logActivity(`${maintenancePc.name}: Resolved from maintenance.`, 'success');
      }
      setMaintenanceModalOpen(false);
      setMaintenancePc(null);
      // Refresh PCs immediately to show updated state
      setTimeout(() => fetchPcs(), 500);
    } catch (err) {
      console.error('Maintenance error:', err);
      toast.error(err?.error || err?.message || 'Failed to update maintenance status');
    } finally {
      setMaintenanceLoading(false);
    }
  };

  const handleOverrideSubmit = async (e) => {
    e.preventDefault();
    if (!overrideReason.trim()) {
      toast.error('Override reason is required');
      return;
    }
    setOverrideLoading(true);
    try {
      await overrideReservation(overrideData.id, { reason: overrideReason.trim() });
      toast.success('Reservation overridden successfully');
      logActivity(`${overrideData.pcName}: Reservation overridden.`, 'warn');
      setOverrideData(null);
      setOverrideReason('');
      fetchPcs();
    } catch (err) {
      toast.error(err.response?.data?.error || err.response?.data?.message || 'Failed to override reservation');
    } finally {
      setOverrideLoading(false);
    }
  };

  useEffect(() => {
    if (!targetBranchId) return;
    setIsLoading(true);
    fetchPcs();
  }, [fetchPcs, targetBranchId]);

  // Safety net: refetch periodically (silently, without showing loading) so rates/buffer minutes never sit stale
  // Increased to 20 seconds to minimize visible refresh, only updates if data actually changed
  useEffect(() => {
    const interval = setInterval(() => fetchPcs(true), 20000);
    return () => clearInterval(interval);
  }, [fetchPcs]);

  useEffect(() => {
    const handleRefresh = (e) => {
      const pcId = e.detail?.pcId;
      if (pcId) {
        // Instantly mark active
        setPcs(current => {
          const idx = current.findIndex(p => p.id === pcId || p.name === pcId);
          if (idx === -1) return current;
          const next = [...current];
          next[idx] = { ...next[idx], state: 'Active' };
          return next;
        });
        
        // Instantly remove walkin pending status
        setWalkinRequests(prev => prev.filter(r => r.pcId !== pcId && r.PcId !== pcId));
      }
    };
    window.addEventListener('refresh-pcs', handleRefresh);
    return () => window.removeEventListener('refresh-pcs', handleRefresh);
  }, []);

  // Poll for pending walk-in requests every 5 seconds — reliable fallback regardless of SignalR state
  useEffect(() => {
    if (!targetBranchId) return;

    const fetchPending = async () => {
      try {
        const { data } = await api.get('/public/walkin-pending');
        if (data?.success && Array.isArray(data.data)) {
          setWalkinRequests(data.data);
        }
      } catch {
        // silent — SignalR may still deliver the event
      }
    };

    fetchPending();
    const interval = setInterval(fetchPending, 5000);
    return () => clearInterval(interval);
  }, [targetBranchId]);

  // SignalR realtime PC state updates & immediate walk-in notification
  useEffect(() => {
    if (!connected || !targetBranchId) return;
    const unsubPcStatus = subscribe(SIGNALR_HUBS.PC_STATUS, 'PcStatusChanged', (payload) => {
      console.log('[SessionsPage] PcStatusChanged received. Refetching PCs...');
      const data = payload.payload || payload.Payload || payload.data || payload.Data || payload;
      const status = data.status || data.State || data.state;
      if (status === 'active' || status === 'Active' || status === 1 || status === '1') {
        setWalkinRequests(prev => prev.filter(r => {
          const reqPcId = r.pcId || r.PcId;
          return reqPcId !== (data.pcId || data.id) && reqPcId !== (data.name || data.Name);
        }));
      }
      fetchPcs();
    });

    // Super Admin changed a Pricing Profile (rate or buffer) — refetch instantly so
    // every open PC card reflects it immediately, not just newly started sessions.
    const unsubPricing = subscribe(SIGNALR_HUBS.PC_STATUS, 'PricingProfileUpdated', () => {
      fetchPcs();
    });

    // Immediate delivery via SignalR (polling above provides the fallback)
    const unsubNotification = subscribe(SIGNALR_HUBS.NOTIFICATIONS, 'Alert', (alert) => {
      console.log('[SessionsPage] Received Alert:', alert);
      const type = alert.type || alert.Type;

      // A member's gaming wallet is running low — surface it so the operator can walk over
      // and offer a top-up before the session auto-stops.
      if (type === 'MemberLowBalance') {
        const pcName = alert.pcName || alert.PcName || 'a PC';
        const memberName = alert.memberName || alert.MemberName || 'Member';
        const remaining = alert.remainingBalance ?? alert.RemainingBalance ?? 0;
        const mins = alert.minutesRemaining ?? alert.MinutesRemaining ?? 0;
        toast.warning(`${memberName} on ${pcName}: ₹${Number(remaining).toFixed(2)} gaming balance left (~${mins} min)`);
        logActivity(`${pcName}: ${memberName} low gaming balance — ₹${Number(remaining).toFixed(2)} left (~${mins} min). Offer a top-up.`, 'warn');
        return;
      }

      if (type === 'WalkinSessionRequest') {
        console.log('[SessionsPage] Setting Walkin Request state for pcId:', alert.pcId);
        setWalkinRequests(prev => {
          const exists = prev.find(p => p.pcId === (alert.pcId || alert.PcId));
          if (exists) return prev;
          const newReqs = [...prev, { ...alert, pcId: alert.pcId || alert.PcId }];
          console.log('[SessionsPage] New Walkin Requests state:', newReqs);
          return newReqs;
        });
      }
    });

    return () => {
      unsubPcStatus();
      unsubPricing();
      unsubNotification();
    };
  }, [connected, subscribe, SIGNALR_HUBS.PC_STATUS, SIGNALR_HUBS.NOTIFICATIONS, targetBranchId]);

  const handleApproveWalkin = async (req) => {
    try {
      const expectedAmount = req.duration ? (req.duration / 60) * 100 : 0;
      const pc = pcs.find(p => p.name === req.pcId || p.id === req.pcId);
      const actualPcId = pc ? pc.id : req.pcId;

      const res = await api.post('/sessions/start', {
        pcId: actualPcId,
        memberId: null,
        customerName: req.customerName,
        durationMinutes: req.duration,
        packageName: req.packageName || 'Walk-in',
        expectedAmount: expectedAmount
      });
      if (res.data.success) {
        toast.success(`Walk-in session started for ${req.pcId}`);
        logActivity(`${req.pcId}: Walk-in session approved for ${req.customerName}.`, 'success');
        setWalkinRequests(prev => prev.filter(r => r.pcId !== req.pcId));
        setPcs(current => {
          const idx = current.findIndex(p => p.id === actualPcId);
          if (idx === -1) return current;
          const next = [...current];
          next[idx] = { ...next[idx], state: 'Active' };
          return next;
        });
      }
    } catch (err) {
      toast.error(err.response?.data?.error || 'Failed to approve walk-in');
    }
  };

  const handleDeclineWalkin = async (req) => {
    try {
      await api.post(`/public/pcs/${req.pcId}/decline-walkin`);
      setWalkinRequests(prev => prev.filter(r => r.pcId !== req.pcId));
      toast.info(`Declined walk-in for ${req.pcId}`);
      logActivity(`${req.pcId}: Walk-in request declined.`, 'error');
    } catch (err) {
      toast.error('Failed to decline request');
    }
  };

  const handleCreditClick = async (pc) => {
    try {
      if (pc.activeSessionId) {
        // Stop session and generate bill
        await api.post(`/sessions/${pc.activeSessionId}/stop`, { deferPayment: false });
      }
      navigate('/app/billing', { state: { autoSelectPcId: pc.id, autoSelectPaymentMethod: 'credit' } });
    } catch (err) {
      const errCode = err.response?.data?.code || err.response?.data?.errorCode;
      const errMsg = err.response?.data?.error || err.response?.data?.message || '';
      
      if (errCode === 'SESSION_ALREADY_ENDED' || errMsg?.toLowerCase().includes('already ended')) {
        navigate('/app/billing', { state: { autoSelectPcId: pc.id, autoSelectPaymentMethod: 'credit' } });
      } else {
        toast.error(`Error: ${errMsg || err.message || 'Failed to stop session for credit'}`);
      }
    }
  };

  // Ticker to force live revenue update every 10 seconds
  const [ticker, setTicker] = useState(0);
  useEffect(() => {
    const interval = setInterval(() => setTicker(t => t + 1), 10000);
    return () => clearInterval(interval);
  }, []);

  // ── Stats computed from PC list ──
  const stats = useMemo(() => {
    const activeSessions = pcs.filter(p => p.state === 'Active').length;
    const idleStations = pcs.filter(p => p.state === 'Idle').length;
    const awaitingBilling = pcs.filter(p => p.state === 'AwaitingBilling').length;

    // Live accrued revenue across all active sessions — use the backend's own live
    // totalAmount (buffer-aware, same formula as the final bill) instead of re-deriving
    // it here, so this stat can never drift from what the PC cards / billing show.
    const rawRevenue = pcs
      .filter(p => p.state === 'Active')
      .reduce((sum, p) => sum + (p.totalAmount || 0), 0);
    const liveRevenue = roundBillTotal(rawRevenue);

    return { activeSessions, idleStations, awaitingBilling, liveRevenue };
  }, [pcs, ticker]);

  // Resolve the selected PC / its pending walk-in fresh from the live lists on every render,
  // instead of caching a snapshot — so the detail panel always reflects the latest state.
  const selectedPc = pcs.find(p => p.id === selectedPcId) || null;
  const selectedWalkinReq = selectedPc
    ? walkinRequests?.find(r => r.pcId === selectedPc.name || r.pcId === selectedPc.id)
    : null;

  if (isLoading) {
    return (
      <div className="flex items-center justify-center min-h-[60vh]">
        <div className="w-8 h-8 rounded-full border-2 border-accent border-t-transparent animate-spin" />
      </div>
    );
  }

  return (
    <div className="space-y-4">

      {/* ── Stats Bar ── */}
      <div className="grid grid-cols-2 lg:grid-cols-4 gap-3">
        <StatCard
          icon={<MonitorPlay className="w-4 h-4" />}
          label="ACTIVE SESSIONS"
          value={stats.activeSessions}
          color="text-pc-active"
          borderColor="border-pc-active/20"
        />
        <StatCard
          icon={<MonitorOff className="w-4 h-4" />}
          label="IDLE STATIONS"
          value={stats.idleStations}
          color="text-text-2"
          borderColor="border-border"
        />
        <StatCard
          icon={<IndianRupee className="w-4 h-4" />}
          label="LIVE ACCRUED REVENUE"
          value={`₹${stats.liveRevenue}`}
          color="text-neon-orange"
          borderColor="border-neon-orange/20"
        />
        <StatCard
          icon={<Clock className="w-4 h-4" />}
          label="AWAITING BILLING"
          value={stats.awaitingBilling}
          color="text-neon-orange"
          borderColor="border-neon-orange/20"
        />
      </div>

      {/* ── Instruction strip ── */}
      <p className="text-text-3 text-xs font-mono">
        Click a PC to view details or start a session. <span className="text-pc-active font-semibold">Double-click</span> an idle PC to quick-start.
      </p>

      {/* ── Legend + tile size zoom control ── */}
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div className="flex flex-wrap items-center gap-3 text-[10px] font-bold uppercase tracking-wider">
          <div className="flex items-center gap-1.5"><span className="w-2 h-2 rounded-full bg-pc-idle" /> Idle</div>
          <div className="flex items-center gap-1.5"><span className="w-2 h-2 rounded-full bg-pc-active" /> Active</div>
          <div className="flex items-center gap-1.5"><span className="w-2 h-2 rounded-full bg-pc-reserved" /> Reserved</div>
          <div className="flex items-center gap-1.5"><span className="w-2 h-2 rounded-full bg-neon-orange" /> Awaiting Bill</div>
          <div className="flex items-center gap-1.5"><span className="w-2 h-2 rounded-full bg-pc-offline" /> Maintenance</div>
        </div>
        <div className="flex items-center gap-1 border border-border rounded-lg p-0.5">
          <button
            type="button"
            onClick={() => setTileSizeIndex((i) => Math.max(0, i - 1))}
            disabled={tileSizeIndex === 0}
            className="p-1.5 rounded-md text-text-2 hover:text-text hover:bg-bg-3 disabled:opacity-30 disabled:hover:bg-transparent transition-colors"
            title="Shrink PC tiles"
          >
            <Minus className="w-3.5 h-3.5" />
          </button>
          <span className="text-[10px] font-mono font-bold uppercase text-text-2 w-6 text-center select-none">
            {TILE_SIZES[tileSizeIndex]}
          </span>
          <button
            type="button"
            onClick={() => setTileSizeIndex((i) => Math.min(TILE_SIZES.length - 1, i + 1))}
            disabled={tileSizeIndex === TILE_SIZES.length - 1}
            className="p-1.5 rounded-md text-text-2 hover:text-text hover:bg-bg-3 disabled:opacity-30 disabled:hover:bg-transparent transition-colors"
            title="Enlarge PC tiles"
          >
            <Plus className="w-3.5 h-3.5" />
          </button>
        </div>
      </div>

      {/* ── Detail panel + PC Grid ── */}
      <div className="flex flex-col lg:flex-row gap-4 items-start" style={{ paddingBottom: logHeight + 16 }}>
        <div className="w-full lg:w-[320px] flex-shrink-0 lg:sticky lg:top-4">
          <PcDetailPanel
            pc={selectedPc}
            walkinReq={selectedWalkinReq}
            onClose={() => setSelectedPcId(null)}
            onRefresh={fetchPcs}
            onStartReservedSession={handleStartReservedSession}
            onOverrideReservation={handleOverrideClick}
            onApproveWalkin={handleApproveWalkin}
            onDeclineWalkin={handleDeclineWalkin}
            onFlagMaintenance={handleFlagMaintenance}
            onCreditClick={handleCreditClick}
          />
        </div>
        <div className="flex-1 min-w-0 w-full">
          <PcGrid
            pcs={pcs}
            walkinRequests={walkinRequests}
            selectedPcId={selectedPcId}
            onSelectPc={(pc) => setSelectedPcId(pc.id)}
            onQuickStart={(pc) => setQuickStartPc(pc)}
            onRefresh={fetchPcs}
            size={TILE_SIZES[tileSizeIndex]}
          />
        </div>
      </div>

      {/* ── Activity Log strip (fixed to viewport bottom, resizable) ── */}
      <SessionActivityLog height={logHeight} onHeightChange={setLogHeight} />

      {/* ── Quick Start Modal (double-click on an idle PC) ── */}
      <QuickStartModal
        pc={quickStartPc}
        onClose={() => setQuickStartPc(null)}
        onActionSuccess={() => {
          setSelectedPcId(quickStartPc?.id ?? null);
          setQuickStartPc(null);
          fetchPcs();
        }}
      />

      {/* ── Override Reservation Modal ── */}
      {overrideData && (
        <div className="fixed inset-0 z-50 flex items-center justify-center p-4 bg-black/60 backdrop-blur-sm animate-[fadeIn_0.15s_ease-out]">
          <div className="w-full max-w-sm bg-bg-2 border border-border rounded-xl shadow-2xl overflow-hidden">
            <div className="px-5 py-4 border-b border-border bg-bg-3 flex items-center justify-between">
              <div>
                <h2 className="font-heading font-bold text-text uppercase tracking-wider text-sm flex items-center gap-2">
                  <ShieldAlert className="w-4 h-4 text-neon-orange animate-bounce" />
                  Override Reservation — {overrideData.pcName}
                </h2>
                <p className="text-text-3 text-[10px] font-mono mt-0.5">
                  An audit log entry will document this override.
                </p>
              </div>
              <button onClick={() => setOverrideData(null)} className="text-text-3 hover:text-text text-xl">&times;</button>
            </div>
            <form onSubmit={handleOverrideSubmit} className="p-5 space-y-4">
              <div className="space-y-1.5">
                <label className="text-[10px] font-mono font-semibold text-text-2 uppercase tracking-wider block">
                  Mandatory Reason for Override *
                </label>
                <textarea
                  value={overrideReason}
                  onChange={(e) => setOverrideReason(e.target.value)}
                  placeholder="Provide detailed explanation..."
                  rows={3}
                  className="w-full bg-bg-3 border border-border rounded px-3 py-2 text-xs text-text placeholder-text-3 focus:border-neon-orange focus:outline-none transition-colors resize-none"
                  required
                  autoFocus
                />
              </div>
              <div className="flex justify-end gap-2.5">
                <button
                  type="button"
                  onClick={() => setOverrideData(null)}
                  className="px-4 py-2 border border-border bg-transparent text-text-2 rounded text-xs font-semibold hover:bg-bg-3 transition-colors"
                >
                  Cancel
                </button>
                <button
                  type="submit"
                  disabled={overrideLoading || !overrideReason.trim()}
                  className="px-4 py-2 bg-neon-orange/10 border border-neon-orange/50 text-neon-orange rounded text-xs font-semibold hover:bg-neon-orange/20 transition-colors flex items-center justify-center gap-1.5 disabled:opacity-50"
                >
                  {overrideLoading ? (
                    <span className="w-3.5 h-3.5 border border-current border-t-transparent rounded-full animate-spin" />
                  ) : (
                    'Override PC'
                  )}
                </button>
              </div>
            </form>
          </div>
        </div>
      )}

      {/* ── Maintenance Reason Modal ── */}
      <MaintenanceReasonModal
        isOpen={maintenanceModalOpen}
        pcNumber={maintenancePc?.name}
        onConfirm={handleMaintenanceConfirm}
        onCancel={() => {
          setMaintenanceModalOpen(false);
          setMaintenancePc(null);
        }}
        isLoading={maintenanceLoading}
      />

    </div>
  );
}

// ── Stats card component ──
function StatCard({ icon, label, value, color, borderColor }) {
  return (
    <div className={`bg-bg-2 border ${borderColor} rounded-lg p-4 flex flex-col gap-1.5`}>
      <div className={`flex items-center gap-1.5 text-[9px] font-mono font-semibold uppercase tracking-widest ${color}`}>
        {icon}
        {label}
      </div>
      <div className={`font-heading font-bold text-2xl ${color}`}>{value}</div>
    </div>
  );
}
