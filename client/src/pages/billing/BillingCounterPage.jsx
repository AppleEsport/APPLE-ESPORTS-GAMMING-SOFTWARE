import { useState, useEffect, useCallback } from 'react';
import { useLocation, useNavigate } from 'react-router-dom';
import { Receipt, Clock, IndianRupee, User } from 'lucide-react';
import { useAuth } from '../../contexts/AuthContext';
import { useBranch } from '../../contexts/BranchContext';
import { useSocket } from '../../contexts/SocketContext';
import api from '../../config/api';
import { useToast } from '../../components/ui/Toast';
import PageHeader from '../../components/layout/PageHeader';
import ActiveBillsList from '../../components/billing/ActiveBillsList';
import BillDetailsPanel from '../../components/billing/BillDetailsPanel';
import BillingAddItemsPanel from '../../components/billing/BillingAddItemsPanel';
import { getActiveReservations } from '../../api/reservations.api';

const dedupeSessionsByPc = (items) => {
  const map = new Map();
  [...items]
    .sort((a, b) => {
      const aTime = new Date(a.updatedAt || a.startTime || 0).getTime();
      const bTime = new Date(b.updatedAt || b.startTime || 0).getTime();
      return bTime - aTime;
    })
    .forEach((item) => {
      if (!map.has(item.pcId)) {
        map.set(item.pcId, item);
      }
    });
  return Array.from(map.values());
};

export default function BillingCounterPage() {
  const { isSuperAdmin, user } = useAuth();
  const { activeBranch } = useBranch();
  const { subscribe, connected, SIGNALR_HUBS } = useSocket();
  const { state } = useLocation();
  const navigate = useNavigate();
  const autoSelectPcId = state?.autoSelectPcId;
  const autoSelectBillId = state?.autoSelectBillId;

  const [bills, setBills] = useState([]);
  const [activeSessions, setActiveSessions] = useState([]);
  const [reservations, setReservations] = useState([]);
  const [selectedItem, setSelectedItem] = useState(null); // { type: 'bill' | 'session', id: billId }
  const [selectedBillData, setSelectedBillData] = useState(null);
  const [summary, setSummary] = useState(null);
  const [pendingWalkins, setPendingWalkins] = useState([]);
  const toast = useToast();

  const [isLoading, setIsLoading] = useState(true);

  // Arrives from marking a food order Delivered - the bill id is already known exactly,
  // so this selects it directly rather than waiting to search for it inside the fetched
  // list the way autoSelectPcId has to.
  useEffect(() => {
    if (autoSelectBillId) setSelectedItem({ type: 'bill', id: autoSelectBillId });
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [autoSelectBillId]);

  const targetBranchId = isSuperAdmin ? activeBranch?.id : user?.branchId;

  // ── 1. Fetch Master Data ──
  const fetchDashboardData = useCallback(async () => {
    if (isSuperAdmin && !targetBranchId) {
      setBills([]);
      setActiveSessions([]);
      setReservations([]);
      setIsLoading(false);
      return;
    }

    try {
      // Fetch pending bills, active sessions, reservations, and summary stats
      const [billsRes, sessionsRes, reservationsList, summaryRes] = await Promise.all([
        api.get('/bills', { params: { page: 1, pageSize: 100, branchId: targetBranchId } }),
        api.get('/sessions', { params: { page: 1, pageSize: 100, branchId: targetBranchId } }),
        getActiveReservations(1, 100).catch(() => []),
        api.get('/dashboard/summary', { params: { branchId: targetBranchId } }).catch(() => null)
      ]);

      const unpaidBills = billsRes.data?.data?.items || [];
      const sessions = sessionsRes.data?.data?.items || [];
      const uniqueSessions = dedupeSessionsByPc(sessions);

      setBills(unpaidBills);
      setActiveSessions(uniqueSessions.filter(s => s.status === 1 || s.status === 'Active'));
      setReservations(reservationsList);
      if (summaryRes?.data?.data) {
        setSummary(summaryRes.data.data);
      }

      if (autoSelectPcId) {
        // Try to find in unpaid bills first
        const bill = unpaidBills.find(b => b.pcId === autoSelectPcId);
        if (bill) {
          setSelectedItem({ type: 'bill', id: bill.id });
        } else {
          // Try to find in active sessions
          const session = uniqueSessions.find(s => s.pcId === autoSelectPcId && (s.status === 1 || s.status === 'Active'));
          if (session) {
             setSelectedItem({ type: 'session', id: session.billId });
          }
        }
      }

    } catch (err) {
      console.error("Failed to load Billing Counter data:", err);
    } finally {
      setIsLoading(false);
    }
  }, [targetBranchId, isSuperAdmin]);

  useEffect(() => {
    setIsLoading(true);
    fetchDashboardData();
  }, [fetchDashboardData]);

  // Safety net: real-time hub events normally keep this list fresh, but if one is ever
  // missed (dropped connection, etc.) this guarantees the Active Sessions list self-heals
  // within 15s instead of showing stale elapsed times indefinitely.
  //
  // Also refreshes whichever bill is currently open. A Head-Office-routed action (discount,
  // payment) reports back on the branch's own timing, not a fixed few seconds - the earlier
  // one-shot polls after a discount gave up after 9s and left the screen showing the
  // pre-discount total forever if the branch took any longer than that. This keeps checking
  // for as long as a bill stays open, the same way the operator's own screen is never stale
  // because it reads the real thing directly.
  useEffect(() => {
    const interval = setInterval(() => {
      fetchDashboardData();
      if (selectedItem?.id) fetchBillDetails(selectedItem.id);
    }, 10000);
    return () => clearInterval(interval);
  }, [fetchDashboardData, selectedItem]);

  // ── 2. Real-Time Hubs ──
  useEffect(() => {
    if (!connected || !targetBranchId) return;

    // Listen to billing updates
    const unsubBilling = subscribe(SIGNALR_HUBS.BILLING, 'BillingUpdated', (billId) => {
      fetchDashboardData();
      if (selectedItem?.id === billId || selectedBillData?.id === billId) {
        fetchBillDetails(billId);
      }
    });

    // Listen to PC Status changes
    const unsubPcStatus = subscribe(SIGNALR_HUBS.PC_STATUS, 'PcStatusChanged', () => {
      fetchDashboardData();
    });

    // Super Admin changed a Pricing Profile — refetch instantly
    const unsubPricing = subscribe(SIGNALR_HUBS.PC_STATUS, 'PricingProfileUpdated', () => {
      fetchDashboardData();
    });

    // Listen to Reservation updates
    const unsubReservations = subscribe(SIGNALR_HUBS.RESERVATIONS, 'ReservationUpdated', () => {
      fetchDashboardData();
    });

    // Listen to Dashboard summary updates
    const unsubSummary = subscribe(SIGNALR_HUBS.DASHBOARD, 'DashboardUpdated', (newSummary) => {
      setSummary(newSummary);
    });

    // Listen to Notification Hub for Walkin Requests
    const unsubNotification = subscribe(SIGNALR_HUBS.NOTIFICATION, 'Alert', (alert) => {
      const type = alert.type || alert.Type;
      if (type === 'WalkinSessionRequest') {
        setPendingWalkins(prev => {
          const exists = prev.find(p => p.pcId === alert.pcId);
          if (exists) return prev;
          return [...prev, alert];
        });
      }
    });

    return () => {
      unsubBilling();
      unsubPcStatus();
      unsubPricing();
      unsubReservations();
      unsubSummary();
      unsubNotification();
    };
  }, [connected, subscribe, SIGNALR_HUBS, targetBranchId, fetchDashboardData, selectedItem, selectedBillData]);

  // ── 3. Fetch Selected Bill Details ──
  const fetchBillDetails = async (billId) => {
    if (!billId) return;
    try {
      const { data } = await api.get(`/bills/${billId}`);
      setSelectedBillData(data.data);
    } catch (err) {
      console.error("Failed to load bill details", err);
      setSelectedBillData(null);
    }
  };

  useEffect(() => {
    if (selectedItem?.id) {
      fetchBillDetails(selectedItem.id);
    } else {
      setSelectedBillData(null);
    }
  }, [selectedItem]);

  // ── 4. Handlers ──
  const handlePaymentSuccess = () => {
    fetchDashboardData();
    if (selectedItem?.id) {
      fetchBillDetails(selectedItem.id);
    }
  };

  const handleApproveWalkin = async (req) => {
    try {
      // Use the PC's real assigned rate (Branch-Wise Pricing Profile), not a guessed flat rate.
      const { data: pcInfo } = await api.get(`/public/pcs/${req.pcId}`);
      const ratePerHour = pcInfo?.data?.ratePerHour ?? 0;

      const payload = {
        pcId: req.pcId,
        customerName: req.customerName,
        durationMinutes: req.duration,
        packageName: req.packageName || 'Walk-in Session',
        expectedAmount: (req.duration / 60) * ratePerHour,
        notes: 'Walk-in approved from dashboard'
      };
      const res = await api.post('/sessions/start', payload);
      if (res.data.success) {
        toast.success('Walk-in Approved and Session Started!');
        setPendingWalkins(prev => prev.filter(p => p.pcId !== req.pcId));
        fetchDashboardData();
      }
    } catch (err) {
      toast.error(err.response?.data?.error || 'Error approving walkin');
    }
  };

  const handleDeclineWalkin = async (pcId) => {
    try {
      await api.post(`/public/pcs/${pcId}/decline-walkin`);
      setPendingWalkins(prev => prev.filter(p => p.pcId !== pcId));
      toast.success('Walk-in declined');
    } catch (err) {
      toast.error(err.response?.data?.error || 'Error declining walkin');
    }
  };

  if (isSuperAdmin && !activeBranch) {
    return (
      <div className="flex flex-col items-center justify-center min-h-[60vh] text-center">
        <Receipt className="w-12 h-12 text-text-3 mb-4" />
        <h2 className="text-xl font-heading font-bold text-text mb-2">Select a Branch</h2>
        <p className="text-text-2">You must select a branch to view the Billing Counter.</p>
      </div>
    );
  }

  return (
    <div className="h-full flex flex-col">
      <PageHeader
        title="Billing Counter"
        subtitle="Process split payments and manage active session bills"
        icon="M9 5H7a2 2 0 00-2 2v12a2 2 0 002 2h10a2 2 0 002-2V7a2 2 0 00-2-2h-2M9 5a2 2 0 002 2h2a2 2 0 002-2M9 5a2 2 0 012-2h2a2 2 0 012 2m-3 7h3m-3 4h3m-6-4h.01M9 16h.01"
        badge="LIVE"
      />

      <div className="grid grid-cols-3 gap-2 mt-4">
        <div className="bg-bg-2 border border-border rounded-xl p-2.5 px-3 shadow-sm">
          <div className="text-[10px] text-text-2 mb-0.5 uppercase tracking-wide">Active PCs</div>
          <div className="font-mono text-base font-bold text-accent">{summary?.totalActivePcs || 0}</div>
        </div>
        <div className="bg-bg-2 border border-border rounded-xl p-2.5 px-3 shadow-sm">
          <div className="text-[10px] text-text-2 mb-0.5 uppercase tracking-wide">Today Bills</div>
          <div className="font-mono text-base font-bold text-text">{summary?.todayBillsCount || 0}</div>
        </div>
        <div className="bg-bg-2 border border-border rounded-xl p-2.5 px-3 shadow-sm">
          <div className="text-[10px] text-text-2 mb-0.5 uppercase tracking-wide">Today Revenue</div>
          <div className="font-mono text-[13px] font-bold text-accent">₹{summary?.totalRevenueToday?.toLocaleString() || '0'}</div>
        </div>
      </div>

      {pendingWalkins.length > 0 && (
        <div className="mt-4 flex flex-col gap-3">
          {pendingWalkins.map((req, idx) => (
            <div key={idx} className="bg-accent/10 border border-accent/40 rounded-xl p-4 flex items-center justify-between shadow-[0_0_15px_rgba(220,38,38,0.15)] animate-pulse-glow">
               <div>
                 <div className="flex items-center gap-2 mb-1">
                   <div className="w-2 h-2 rounded-full bg-accent animate-ping" />
                   <h3 className="font-heading font-bold text-lg text-white uppercase tracking-wider">Walk-in Request</h3>
                 </div>
                 <p className="text-sm font-body text-text-2">
                   <strong className="text-text">{req.customerName}</strong> requested <strong className="text-text">{req.packageName || `${req.duration} min`}</strong> on PC: <strong className="font-mono text-accent">{req.pcId.split('-')[0]}</strong>
                 </p>
               </div>
               <div className="flex gap-3">
                 <button 
                   onClick={() => handleDeclineWalkin(req.pcId)} 
                   className="px-4 py-2 bg-bg border border-border text-text-2 hover:text-white hover:bg-bg-3 rounded-md transition-colors font-semibold uppercase tracking-wider text-sm"
                 >
                   Decline
                 </button>
                 <button 
                   onClick={() => handleApproveWalkin(req)} 
                   className="px-6 py-2 bg-accent hover:bg-accent-dark text-white border border-accent/50 shadow-[0_0_10px_rgba(220,38,38,0.3)] rounded-md transition-all font-bold uppercase tracking-wider text-sm flex items-center gap-2"
                 >
                   Approve & Start
                 </button>
               </div>
            </div>
          ))}
        </div>
      )}

      <div className="flex flex-col lg:flex-row gap-4 mt-4 flex-1 min-h-0">

        {/* Left Column: Bills & Lists */}
        <div className="w-full lg:w-[22%] flex flex-col h-full bg-bg-2 border border-border rounded-xl p-3 shadow-lg overflow-y-auto">
          {isLoading ? (
            <div className="flex items-center justify-center flex-1">
              <div className="w-8 h-8 rounded-full border-2 border-accent border-t-transparent animate-spin" />
            </div>
          ) : (
            <ActiveBillsList
              bills={bills}
              activeSessions={activeSessions}
              reservations={reservations}
              selectedId={selectedItem?.id}
              onSelect={setSelectedItem}
            />
          )}
        </div>

        {/* Middle Column: Current Bill (Primary Focus) */}
        <div className="w-full lg:w-[44%] h-full overflow-y-auto">
          {selectedBillData ? (
            <BillDetailsPanel
              bill={selectedBillData}
              onBillUpdate={(updated) => {
                // From Head Office this is not the updated bill - applyDiscount/processPayment
                // routed to the branch instead, so what comes back is a queue receipt
                // ({ queued: true, message, ... }) with no bill fields at all. Refetching with
                // its non-existent `id` silently did nothing, which is why the discount badge
                // showed "applied" while the total on screen never actually moved: the branch
                // had not carried it out yet, and nothing ever asked it again once it had.
                if (updated?.queued) {
                  toast.success(updated.message || 'Sent to the branch. It applies this within a few seconds.');
                  const billId = selectedItem?.id;
                  if (billId) {
                    // The branch's own confirmation does not push anything back to this screen
                    // (Head Office only learns of it on the branch's next sync), so this polls
                    // for the real total rather than waiting for a push that never comes.
                    setTimeout(() => fetchBillDetails(billId), 4000);
                    setTimeout(() => fetchBillDetails(billId), 9000);
                  }
                  return;
                }
                fetchBillDetails(updated.id);
                fetchDashboardData();
              }}
              onPaymentSuccess={() => {
                // Same behaviour for every role that can complete a payment here - operator,
                // admin and Super Admin all render this exact component, so fixing it once
                // here covers all three rather than needing three separate patches.
                setSelectedItem(null);
                fetchDashboardData();
                navigate('/app/sessions');
              }}
              defaultPaymentMethod={selectedBillData.pcId === autoSelectPcId ? state?.autoSelectPaymentMethod : undefined}
            />
          ) : (
            <div className="bg-bg-2 border border-border rounded-xl p-6 text-center h-full flex items-center justify-center">
              <p className="text-text-3">Select a bill to view details</p>
            </div>
          )}
        </div>

        {/* Right Column: Add Items */}
        <div className="w-full lg:w-[34%] h-full overflow-y-auto">
          <BillingAddItemsPanel
            bill={selectedBillData}
            onOrderPlaced={(newBillId) => {
              fetchDashboardData();
              if (selectedBillData?.id) {
                fetchBillDetails(selectedBillData.id);
              } else if (newBillId) {
                setSelectedItem({ type: 'bill', id: newBillId });
              }
            }}
          />
        </div>

      </div>
    </div>
  );
}
