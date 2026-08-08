import { useState, useEffect, useCallback, useRef } from 'react';
import { ShieldCheck, AlertTriangle, FileText, CheckCircle, Lock, Monitor, Utensils, Clock, Printer, Download, Wrench, Clock as ClockIcon } from 'lucide-react';
import { printBill } from '../../utils/printBill';
import { useAuth } from '../../contexts/AuthContext';
import { useBranch } from '../../contexts/BranchContext';
import api from '../../config/api';
import PageHeader from '../../components/layout/PageHeader';
import { useSocket } from '../../contexts/SocketContext';
import { createReport, addStatGrid, addTable, save, ROW_TINT_RED, ROW_TINT_GREEN } from '../../utils/pdfReport';
import EodPaymentSummaryBar from '../../components/eod/EodPaymentSummaryBar';
import { getBranchMaintenanceLogs } from '../../api/maintenanceLogs.api';

export default function EodDashboardPage() {
  const { isSuperAdmin, user } = useAuth();
  const { activeBranch } = useBranch();
  const { subscribe, connected, SIGNALR_HUBS } = useSocket();

  const [targetDate, setTargetDate] = useState(new Date().toISOString().split('T')[0]); // YYYY-MM-DD
  const [summaryBarHeight, setSummaryBarHeight] = useState(140);
  const [report, setReport] = useState(null);
  const [validation, setValidation] = useState(null);
  const [isHistorical, setIsHistorical] = useState(false);
  const [isLoading, setIsLoading] = useState(true);
  const [isFinalizing, setIsFinalizing] = useState(false);
  const [error, setError] = useState(null);

  const [pcs, setPcs] = useState([]);
  const [allBills, setAllBills] = useState([]);
  const [maintenanceLogs, setMaintenanceLogs] = useState([]);
  const [selectedPcId, setSelectedPcId] = useState(null);
  const [lastUpdated, setLastUpdated] = useState(Date.now());
  const [isUpdating, setIsUpdating] = useState(false);

  const targetBranchId = isSuperAdmin ? activeBranch?.id : user?.branchId;
  const isFetchingRef = useRef(false);

  const fetchEodData = useCallback(async () => {
    if (isSuperAdmin && !targetBranchId) {
      setIsLoading(false);
      return;
    }

    if (isFetchingRef.current) return;
    isFetchingRef.current = true;

    setIsUpdating(true);
    setError(null);

    try {
      // First try to fetch historical snapshot
      try {
        const { data: historyData } = await api.get('/eod/history', {
          params: { date: targetDate, branchId: targetBranchId }
        });

        setReport(historyData.data.data); // historyData.data is EodSnapshotDto, .data is EodReportDto
        setIsHistorical(true);
      } catch (historyErr) {
        if (historyErr.response?.status === 404) {
          // No snapshot exists. It is either today or an unfinalized past date.
          setIsHistorical(false);
          setValidation(null);

          // Fetch Preview
          const { data: previewData } = await api.get('/eod/preview', {
            params: { date: targetDate, branchId: targetBranchId }
          });
          setReport(previewData.data);

          // Fetch Validation Status
          const { data: validationData } = await api.get('/eod/validation', {
            params: { date: targetDate, branchId: targetBranchId }
          });
          setValidation(validationData.data);
        } else {
          throw historyErr;
        }
      }

      // Also fetch range-report to get allBills and PCs for PC-Wise Grid, and maintenance logs
      const [pcsRes, billsRes] = await Promise.all([
        api.get('/pcs', { params: { branchId: targetBranchId } }),
        api.get('/eod/range-report', {
          params: {
            startDate: `${targetDate}T00:00:00Z`,
            endDate: `${targetDate}T23:59:59Z`,
            branchId: targetBranchId
          }
        })
      ]);
      setPcs(pcsRes.data?.data || []);
      setAllBills(billsRes.data?.data?.allBills || []);

      // Fetch maintenance logs separately so it doesn't break EOD if it fails
      try {
        const maintenanceRes = await getBranchMaintenanceLogs(targetBranchId, 30);
        // Show maintenance logs that were ACTIVE on the target date:
        // - Marked on or before the target date
        // - Either not resolved yet, OR resolved on/after the target date
        //   (">=", not ">": a PC marked and restored on the same day must still show up)
        const logsActiveOnDate = (maintenanceRes.data || []).filter(log => {
          const markedDate = new Date(log.markedAt).toISOString().split('T')[0];
          const resolvedDate = log.resolvedAt ? new Date(log.resolvedAt).toISOString().split('T')[0] : null;

          const markedOnOrBefore = markedDate <= targetDate;
          const notResolvedOrResolvedAfter = !resolvedDate || resolvedDate >= targetDate;

          return markedOnOrBefore && notResolvedOrResolvedAfter;
        });
        setMaintenanceLogs(logsActiveOnDate);
      } catch (err) {
        console.error('Failed to fetch maintenance logs:', err);
        setMaintenanceLogs([]);
      }

      setLastUpdated(Date.now());

    } catch (err) {
      setError(err.response?.data?.error || err.response?.data?.message || 'Failed to fetch EOD data.');
    } finally {
      setIsLoading(false);
      setIsUpdating(false);
      isFetchingRef.current = false;
    }
  }, [targetDate, targetBranchId, isSuperAdmin]);

  useEffect(() => {
    fetchEodData();
  }, [fetchEodData]);

  // Real-time EOD updates via SignalR + aggressive polling
  useEffect(() => {
    if (!connected || isHistorical) return;

    // Immediate refresh on changes
    const unsubCash = subscribe(SIGNALR_HUBS.CASH, 'CashRegisterUpdated', () => {
      console.log('💰 Cash updated - refetching EOD');
      fetchEodData();
    });
    const unsubBill = subscribe(SIGNALR_HUBS.BILLING, 'BillUpdated', () => {
      console.log('📄 Bill updated - refetching EOD');
      fetchEodData();
    });
    const unsubSession = subscribe(SIGNALR_HUBS.SESSIONS, 'SessionUpdated', () => {
      console.log('⏱️ Session updated - refetching EOD');
      fetchEodData();
    });

    // Safety-net polling; SignalR events above already push immediate refreshes
    const pollInterval = setInterval(() => {
      fetchEodData();
    }, 20000);

    return () => {
      unsubCash();
      unsubBill();
      unsubSession();
      clearInterval(pollInterval);
    };
  }, [connected, subscribe, SIGNALR_HUBS.CASH, SIGNALR_HUBS.BILLING, SIGNALR_HUBS.SESSIONS, fetchEodData, isHistorical]);

  const handleFinalize = async () => {
    if (!window.confirm("Are you sure? This will generate a permanent immutable snapshot for this date. It cannot be undone.")) return;

    setIsFinalizing(true);
    try {
      await api.post('/eod/finalize', { date: targetDate });
      await fetchEodData(); // Re-fetch to show historical locked view
    } catch (err) {
      setError(err.response?.data?.error || err.response?.data?.message || 'Failed to finalize EOD.');
    } finally {
      setIsFinalizing(false);
    }
  };

  const handleDownloadPdf = () => {
    if (!report) return;
    const title = 'End of Day Report';
    const subtitle = `${activeBranch?.name || 'All Branches'}  •  ${targetDate}  •  ${isHistorical ? 'Finalized (Immutable)' : 'Live Preview'}`;
    const { doc } = createReport({ title, subtitle });
    let y = 90;

    y = addStatGrid(doc, y, [
      { label: 'Total Net Revenue', value: `Rs ${report.revenue.netRevenue}` },
      { label: 'Gaming Revenue', value: `Rs ${report.revenue.totalGamingRevenue}` },
      { label: 'Food Revenue', value: `Rs ${report.revenue.totalFoodRevenue}` },
      { label: 'Discounts Applied', value: `Rs ${report.revenue.totalDiscounts}` },
    ]);
    y += 10;

    y = addTable(doc, y, {
      title, subtitle,
      heading: 'Cash Lifecycle Summary',
      head: ['Metric', 'Amount'],
      body: [
        ['Opening Balance Total', `Rs ${report.cash.totalOpeningBalance}`],
        ['Cash Sales + Wallet TopUps', `Rs ${report.cash.totalCashSales}`],
        ['Petty Expenses', `-Rs ${report.cash.totalPettyExpenses}`],
        ['Expected Drawer Total', `Rs ${report.cash.expectedCashInDrawer}`],
        ['Physically Counted', `Rs ${report.cash.actualPhysicalCashCounted}`],
        ['Total Difference', `Rs ${report.cash.totalDiscrepancy}`],
      ],
    });

    const creditsPending = (report.creditLogs?.filter(c => c.status?.toLowerCase() === 'pending')
      .reduce((acc, c) => acc + c.creditAmount, 0) || 0).toFixed(2);
    const overallEndTotal = (report.paymentMethods.totalCash + report.paymentMethods.totalOnline + report.paymentMethods.totalWalletDeductions + report.paymentMethods.totalWalletTopUps).toFixed(2);

    y = addTable(doc, y, {
      title, subtitle,
      heading: 'Overall Collection & Operations',
      head: ['Metric', 'Value'],
      body: [
        ['Cash', `Rs ${report.paymentMethods.totalCash}`],
        ['Online', `Rs ${report.paymentMethods.totalOnline}`],
        ['Wallet Deductions (Gaming/Food)', `Rs ${report.paymentMethods.totalWalletDeductions}`],
        ['Wallet Top-Ups (Cash Collected)', `Rs ${report.paymentMethods.totalWalletTopUps}`],
        ['Credits Pending', `-Rs ${creditsPending}`],
        ['Overall End Total', `Rs ${overallEndTotal}`],
        ['Total Sessions', String(report.operations.totalSessions)],
        ['Total Food Orders', String(report.operations.totalFoodOrders)],
      ],
    });

    const pcRows = (pcs || []).map(pc => {
      const pcBills = allBills?.filter(b => b.pcId === pc.id) || [];
      const total = pcBills.reduce((sum, b) => sum + (b.totalRevenue || 0), 0);
      return [pc.name || pc.pcName || pc.pcNumber, String(pcBills.length), `Rs ${total.toFixed(2)}`];
    }).filter(row => row[1] !== '0');
    if (pcRows.length) {
      y = addTable(doc, y, {
        title, subtitle,
        heading: 'PC-Wise Breakdown',
        head: ['PC', 'Bills', 'Total Revenue'],
        body: pcRows,
      });
    }

    y = addTable(doc, y, {
      title, subtitle,
      heading: `Complete Billing Audit Logs (${targetDate})`,
      head: ['Date', 'PC Number', 'Start Time', 'End Time', 'Customer', 'Payment', 'Gaming', 'Food', 'Discount', 'Total', 'Note', 'Operator'],
      body: (allBills || []).map(b => [
        new Date(b.date).toLocaleDateString(),
        b.pcName || '-',
        b.sessionStartTime ? new Date(b.sessionStartTime).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit', hour12: true }) : '-',
        b.sessionEndTime ? new Date(b.sessionEndTime).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit', hour12: true }) : '-',
        b.customer, b.paymentType,
        `Rs ${b.gamingRevenue.toFixed(2)}`, `Rs ${b.foodRevenue.toFixed(2)}`,
        b.discount > 0 ? `-Rs ${b.discount.toFixed(2)}` : '-', `Rs ${b.totalRevenue.toFixed(2)}`,
        b.sessionNotes || '-', b.operator
      ]),
    });

    const eodCreditRows = report.creditLogs || [];
    y = addTable(doc, y, {
      title, subtitle,
      heading: `Credit Audit Logs (${targetDate})`,
      head: ['Date Created', 'Customer', 'PC', 'Original Bill', 'Initial Paid', 'Amount Due', 'Status', 'Date Cleared'],
      body: eodCreditRows.map(c => [
        new Date(c.createdAt).toLocaleString(), c.customerName, c.pcNumber,
        `Rs ${c.originalBillAmount.toFixed(2)}`, `Rs ${c.amountPaidInitially.toFixed(2)}`, `Rs ${c.creditAmount.toFixed(2)}`,
        c.status, c.clearedAt ? new Date(c.clearedAt).toLocaleString() : '-'
      ]),
      rowColor: (rowIndex) => eodCreditRows[rowIndex]?.status?.toLowerCase() === 'cleared' ? ROW_TINT_GREEN : ROW_TINT_RED,
    });

    const maintenanceRows = maintenanceLogs || [];
    if (maintenanceRows.length) {
      y = addTable(doc, y, {
        title, subtitle,
        heading: `Maintenance Logs (${targetDate})`,
        head: ['PC', 'Marked At', 'Marked By', 'Reason', 'Duration', 'Status', 'Resolved At', 'Resolution Notes'],
        body: maintenanceRows.map(log => [
          log.pcName || '-',
          log.markedAt ? new Date(log.markedAt).toLocaleString() : '-',
          log.operatorName || '-',
          log.reason || '-',
          log.durationMinutes > 0
            ? `${Math.floor(log.durationMinutes / 60)}h ${log.durationMinutes % 60}m`
            : (log.isResolved ? '< 1m' : '-'),
          log.isResolved ? 'Resolved' : 'Active',
          log.resolvedAt ? new Date(log.resolvedAt).toLocaleString() : '-',
          log.resolutionNotes || '-'
        ]),
        rowColor: (rowIndex) => maintenanceRows[rowIndex]?.isResolved ? ROW_TINT_GREEN : ROW_TINT_RED,
      });
    }

    save(doc, `Apple_Esports_EOD_${targetDate}.pdf`);
  };

  if (isSuperAdmin && !activeBranch) {
    return (
      <div className="flex flex-col items-center justify-center min-h-[60vh] text-center">
        <Lock className="w-12 h-12 text-text-3 mb-4" />
        <h2 className="text-xl font-heading font-bold text-text mb-2">Select a Branch</h2>
        <p className="text-text-2">You must select a branch to view EOD Reports.</p>
      </div>
    );
  }

  return (
    <>
    <div
      className="h-full flex flex-col max-w-6xl mx-auto space-y-6 overflow-y-auto"
      style={{ paddingBottom: report ? summaryBarHeight + 24 : 40 }}
    >
      <div className="flex justify-between items-center bg-bg-2 p-6 rounded-xl border border-border">
        <PageHeader
          title="End of Day Dashboard"
          subtitle={isHistorical ? 'Immutable Financial Snapshot' : 'Live Preview & Real-Time Updates'}
          icon="M9 17v-2m3 2v-4m3 4v-6m2 10H7a2 2 0 01-2-2V5a2 2 0 012-2h5.586a1 1 0 01.707.293l5.414 5.414a1 1 0 01.293.707V19a2 2 0 01-2 2z"
        />
        <div className="flex flex-col items-end gap-2">
          <div className="flex items-center gap-2">
            <input
              type="date"
              value={targetDate}
              onChange={(e) => setTargetDate(e.target.value)}
              className="bg-bg-3 border border-border rounded-lg px-4 py-2 text-text outline-none focus:border-accent"
            />
            <button
              onClick={handleDownloadPdf}
              disabled={isLoading || !report}
              className="btn-secondary py-2 px-3 flex items-center gap-1.5 text-xs font-bold disabled:opacity-50 disabled:cursor-not-allowed"
            >
              <Download className="w-3.5 h-3.5" /> Download PDF
            </button>
          </div>
          <div className="flex items-center gap-3">
            {isHistorical && (
              <span className="bg-neon-green/10 text-neon-green px-3 py-1 rounded border border-neon-green/30 text-xs font-bold uppercase tracking-widest flex items-center gap-2">
                <ShieldCheck className="w-4 h-4" /> Finalized
              </span>
            )}
          </div>
        </div>
      </div>

      {/* PC-Wise Personalised Billing Section */}
      <div className="card bg-bg-2 border border-border p-6 rounded-xl">
        <h2 className="font-heading font-extrabold text-sm uppercase tracking-wider text-text flex items-center gap-2 mb-6">
          <Monitor className="w-4.5 h-4.5 text-neon-blue" />
          PC-Wise Personalised Billing
        </h2>

        {/* PC Grid */}
        <div className="grid grid-cols-3 md:grid-cols-4 lg:grid-cols-8 gap-3 mb-6">
          {pcs.map(pc => {
            const pcBills = allBills?.filter(b => b.pcId === pc.id) || [];
            const pcDayTotal = pcBills.reduce((sum, b) => sum + (b.totalRevenue || 0), 0);
            const pcBillCount = pcBills.length;
            const hasEarnings = pcDayTotal > 0;

            return (
              <button
                key={pc.id}
                onClick={() => setSelectedPcId(pc.id)}
                className={`p-3 rounded-lg border flex flex-col items-center justify-center transition-all relative ${
                  selectedPcId === pc.id 
                    ? 'bg-neon-blue/20 border-neon-blue shadow-[0_0_15px_rgba(0,240,255,0.3)]' 
                    : hasEarnings 
                      ? 'bg-bg-3 border-neon-green/30 hover:border-neon-green/60' 
                      : 'bg-bg-3 border-border hover:border-neon-blue/50'
                }`}
              >
                {pcBillCount > 0 && (
                  <span className="absolute -top-1.5 -right-1.5 bg-accent text-white text-[9px] font-bold w-5 h-5 rounded-full flex items-center justify-center shadow-md">
                    {pcBillCount}
                  </span>
                )}
                <div className={`w-8 h-8 rounded-full flex items-center justify-center mb-1.5 ${
                  selectedPcId === pc.id ? 'bg-neon-blue/20' : hasEarnings ? 'bg-neon-green/10' : 'bg-bg-2'
                }`}>
                  <Monitor className={`w-4 h-4 ${
                    selectedPcId === pc.id ? 'text-neon-blue' : hasEarnings ? 'text-neon-green' : 'text-text-3'
                  }`} />
                </div>
                <div className="font-heading font-bold text-[10px] text-text truncate w-full text-center mb-1">
                  {pc.name || pc.pcName || pc.pcNumber}
                </div>
                <div className={`font-mono font-bold text-xs ${
                  hasEarnings ? 'text-neon-green' : 'text-text-3'
                }`}>
                  ₹{pcDayTotal.toFixed(0)}
                </div>
              </button>
            );
          })}
        </div>

        {/* Selected PC Details */}
        {selectedPcId && (
          <div className="space-y-6 animate-fade-in border-t border-border/50 pt-6">
            {(() => {
              const pcBills = allBills?.filter(b => 
                selectedPcId === 'walkin' ? (!b.pcId) : (b.pcId === selectedPcId)
              ) || [];
              
              const pcTotalGaming = pcBills.reduce((sum, d) => sum + d.gamingRevenue, 0);
              const pcTotalFood = pcBills.reduce((sum, d) => sum + d.foodRevenue, 0);
              const pcTotalDiscount = pcBills.reduce((sum, d) => sum + d.discount, 0);
              const pcTotalNet = pcBills.reduce((sum, d) => sum + d.totalRevenue, 0);

              return (
                <>
                  <div className="grid grid-cols-2 md:grid-cols-5 gap-4">
                    <div className="bg-bg-3 p-4 rounded-lg border border-border flex flex-col justify-center">
                      <div className="text-[10px] text-text-3 font-bold uppercase tracking-wider">Total Bills</div>
                      <div className="text-xl font-mono font-bold text-text">{pcBills.length}</div>
                    </div>
                    <div className="bg-bg-3 p-4 rounded-lg border border-border flex flex-col justify-center">
                      <div className="text-[10px] text-text-3 font-bold uppercase tracking-wider">Net Gaming</div>
                      <div className="text-xl font-mono font-bold text-neon-blue">₹{pcTotalGaming.toFixed(2)}</div>
                    </div>
                    <div className="bg-bg-3 p-4 rounded-lg border border-border flex flex-col justify-center">
                      <div className="text-[10px] text-text-3 font-bold uppercase tracking-wider">Net Food</div>
                      <div className="text-xl font-mono font-bold text-accent">₹{pcTotalFood.toFixed(2)}</div>
                    </div>
                    <div className="bg-bg-3 p-4 rounded-lg border border-border flex flex-col justify-center">
                      <div className="text-[10px] text-text-3 font-bold uppercase tracking-wider">Discounts</div>
                      <div className="text-xl font-mono font-bold text-neon-orange">₹{pcTotalDiscount.toFixed(2)}</div>
                    </div>
                    <div className="bg-bg-3 p-4 rounded-lg border border-border flex flex-col justify-center relative overflow-hidden">
                      <div className="absolute top-0 right-0 w-16 h-16 bg-neon-green/10 rounded-full blur-xl" />
                      <div className="text-[10px] text-text-3 font-bold uppercase tracking-wider relative">Net Revenue</div>
                      <div className="text-xl font-mono font-bold text-neon-green relative">₹{pcTotalNet.toFixed(2)}</div>
                    </div>
                  </div>

                  <div className="overflow-x-auto mt-4">
                    {pcBills.length === 0 ? (
                      <div className="text-center text-text-3 text-xs italic py-8 border border-dashed border-border rounded-lg">
                        No billing records found for this PC.
                      </div>
                    ) : (
                      <table className="w-full text-left border-collapse text-xs whitespace-nowrap">
                        <thead>
                          <tr className="border-b border-border text-text-3 uppercase tracking-wider font-bold text-[10px]">
                            <th className="py-3 px-4">Date</th>
                            <th className="py-3 px-4">PC Number</th>
                            <th className="py-3 px-4">Start Time</th>
                            <th className="py-3 px-4">End Time</th>
                            <th className="py-3 px-4">Customer</th>
                            <th className="py-3 px-4 text-center">Payment</th>
                            <th className="py-3 px-4 text-right">Gaming</th>
                            <th className="py-3 px-4 text-right">Food</th>
                            <th className="py-3 px-4 text-right">Discount</th>
                            <th className="py-3 px-4 text-right">Total</th>
                            <th className="py-3 px-4">Note</th>
                            <th className="py-3 px-4">Operator</th>
                            <th className="py-3 px-4 text-center">Print</th>
                          </tr>
                        </thead>
                        <tbody className="divide-y divide-border/40 font-mono">
                          {pcBills.map(bill => {
                            const startStr = bill.sessionStartTime ? new Date(bill.sessionStartTime).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit', hour12: true }) : '-';
                            const endStr = bill.sessionEndTime ? new Date(bill.sessionEndTime).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit', hour12: true }) : '-';

                            return (
                              <tr key={bill.billId} className="hover:bg-bg-3/40 transition-colors">
                                <td className="py-3 px-4 text-text-2">{new Date(bill.date).toLocaleDateString()}</td>
                                <td className="py-3 px-4 text-text font-bold">{bill.pcName || '-'}</td>
                                <td className="py-3 px-4 text-text-2">{startStr}</td>
                                <td className="py-3 px-4 text-text-2">{endStr}</td>
                                <td className="py-3 px-4 text-text-2 font-sans">{bill.customer}</td>
                                <td className="py-3 px-4 text-center text-text-3 uppercase">{bill.paymentType}</td>
                                <td className="py-3 px-4 text-right text-text">₹{bill.gamingRevenue.toFixed(2)}</td>
                                <td className="py-3 px-4 text-right text-text">₹{bill.foodRevenue.toFixed(2)}</td>
                                <td className="py-3 px-4 text-right text-neon-red">{bill.discount > 0 ? `-₹${bill.discount.toFixed(2)}` : '-'}</td>
                                <td className="py-3 px-4 text-right text-neon-green font-bold">₹{bill.totalRevenue.toFixed(2)}</td>
                                <td className="py-3 px-4 text-text-3 text-[10px] whitespace-pre-wrap">{bill.sessionNotes || '-'}</td>
                                <td className="py-3 px-4 text-text-2">{bill.operator}</td>
                                <td className="py-3 px-4 text-center">
                                  <button
                                    onClick={() => printBill(bill.billId || bill.id, bill)}
                                    className="p-1.5 bg-bg-3 hover:bg-accent hover:text-bg transition-colors rounded-lg text-text-2 tooltip-trigger"
                                    title="Print Bill"
                                  >
                                    <Printer className="w-4 h-4" />
                                  </button>
                                </td>
                              </tr>
                            );
                          })}
                        </tbody>
                      </table>
                    )}
                  </div>
                </>
              );
            })()}
          </div>
        )}
      </div>

      {error && (
        <div className="bg-neon-red/10 border border-neon-red/30 text-neon-red p-4 rounded-xl flex items-center gap-3">
          <AlertTriangle className="w-5 h-5 shrink-0" />
          <p>{error}</p>
        </div>
      )}

      {isLoading ? (
        <div className="flex justify-center items-center min-h-[40vh]">
          <div className="w-8 h-8 rounded-full border-2 border-accent border-t-transparent animate-spin" />
        </div>
      ) : report ? (
        <>
          {/* Validation Panel (Only if not historical) */}
          {!isHistorical && validation && (
            <div className={`p-6 rounded-xl border ${validation.isReady ? 'bg-neon-green/5 border-neon-green/20' : 'bg-neon-red/5 border-neon-red/20'}`}>
              <h3 className={`text-sm uppercase font-bold tracking-widest mb-4 flex items-center gap-2 ${validation.isReady ? 'text-neon-green' : 'text-neon-red'}`}>
                {validation.isReady ? <CheckCircle className="w-5 h-5" /> : <AlertTriangle className="w-5 h-5" />}
                Financial Validation Status
              </h3>
              
              {validation.isReady ? (
                <p className="text-text-2 text-sm">All shifts are closed. All registers verified. Financials are balanced. You may proceed to finalize.</p>
              ) : (
                <ul className="space-y-2">
                  {validation.blockers.map((blocker, idx) => (
                    <li key={idx} className="text-sm text-neon-red flex items-start gap-2">
                      <span className="text-neon-red/50 mt-0.5">•</span>
                      {blocker}
                    </li>
                  ))}
                </ul>
              )}
            </div>
          )}

          {/* Revenue & Operations Summary Grid */}
          <div className="grid grid-cols-1 md:grid-cols-4 gap-4">
            <div className="bg-bg-2 p-5 rounded-xl border border-border shadow-lg">
              <div className="text-text-3 text-xs uppercase font-bold tracking-widest mb-1">Total Net Revenue</div>
              <div className="text-3xl font-mono font-bold text-accent">₹{report.revenue.netRevenue}</div>
            </div>
            <div className="bg-bg-2 p-5 rounded-xl border border-border shadow-lg">
              <div className="text-text-3 text-xs uppercase font-bold tracking-widest mb-1">Gaming Revenue</div>
              <div className="text-2xl font-mono font-bold text-text">₹{report.revenue.totalGamingRevenue}</div>
            </div>
            <div className="bg-bg-2 p-5 rounded-xl border border-border shadow-lg">
              <div className="text-text-3 text-xs uppercase font-bold tracking-widest mb-1">Food Revenue</div>
              <div className="text-2xl font-mono font-bold text-text">₹{report.revenue.totalFoodRevenue}</div>
            </div>
            <div className="bg-bg-2 p-5 rounded-xl border border-border shadow-lg">
              <div className="text-text-3 text-xs uppercase font-bold tracking-widest mb-1">Discounts Applied</div>
              <div className="text-2xl font-mono font-bold text-text">₹{report.revenue.totalDiscounts}</div>
            </div>
          </div>

          {/* Operations Overview */}
          <div className="bg-bg-2 rounded-xl border border-border shadow-lg p-6">
            <h3 className="text-sm uppercase font-bold text-text-2 tracking-widest mb-6 border-b border-border pb-3 flex items-center gap-2">
              <FileText className="w-4 h-4" /> Operations Overview
            </h3>
            <div className="grid grid-cols-2 md:grid-cols-4 gap-4">
              <div className="bg-bg-3 p-3 rounded-lg border border-border text-center">
                <div className="text-2xl font-bold text-text">{report.operations.totalSessions}</div>
                <div className="text-[10px] uppercase font-bold text-text-3 tracking-widest mt-1">Sessions</div>
              </div>
              <div className="bg-bg-3 p-3 rounded-lg border border-border text-center">
                <div className="text-2xl font-bold text-text">{report.operations.totalFoodOrders}</div>
                <div className="text-[10px] uppercase font-bold text-text-3 tracking-widest mt-1">Food Orders</div>
              </div>
            </div>
          </div>

          {/* ── Complete Billing Audit Logs ── */}
          <div className="card bg-bg-2 border border-border p-6 rounded-xl shadow-lg mt-8">
            <div className="flex justify-between items-center mb-6">
              <h2 className="font-heading font-extrabold text-sm uppercase tracking-wider text-text flex items-center gap-2">
                <Clock className="w-4.5 h-4.5 text-accent" />
                Complete Billing Audit Logs ({targetDate})
              </h2>
            </div>

            <div className="overflow-x-auto">
              {!allBills || allBills.length === 0 ? (
                <div className="text-center text-text-3 text-xs italic py-8 border border-dashed border-border rounded-lg">
                  No bills found for the selected date.
                </div>
              ) : (
                <table className="w-full text-left border-collapse text-xs whitespace-nowrap">
                  <thead>
                    <tr className="border-b border-border text-text-3 uppercase tracking-wider font-bold text-[10px]">
                      <th className="py-3 px-4">Date</th>
                      <th className="py-3 px-4">PC Number</th>
                      <th className="py-3 px-4">Start Time</th>
                      <th className="py-3 px-4">End Time</th>
                      <th className="py-3 px-4">Customer</th>
                      <th className="py-3 px-4 text-center">Payment</th>
                      <th className="py-3 px-4 text-right">Gaming</th>
                      <th className="py-3 px-4 text-right">Food</th>
                      <th className="py-3 px-4 text-right">Discount</th>
                      <th className="py-3 px-4 text-right">Total</th>
                      <th className="py-3 px-4">Note</th>
                      <th className="py-3 px-4">Operator</th>
                      <th className="py-3 px-4 text-center">Print</th>
                    </tr>
                  </thead>
                  <tbody className="divide-y divide-border/40 font-mono">
                    {allBills.map(bill => (
                      <tr key={bill.billId} className="hover:bg-bg-3/40 transition-colors">
                        <td className="py-3 px-4 text-text-2">
                          {new Date(bill.date).toLocaleDateString()}
                        </td>
                        <td className="py-3 px-4 text-text font-bold">{bill.pcName || '-'}</td>
                        <td className="py-3 px-4 text-text-2">
                          {bill.sessionStartTime ? new Date(bill.sessionStartTime).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit', hour12: true }) : '-'}
                        </td>
                        <td className="py-3 px-4 text-text-2">
                          {bill.sessionEndTime ? new Date(bill.sessionEndTime).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit', hour12: true }) : '-'}
                        </td>
                        <td className="py-3 px-4 text-text-2 font-sans">{bill.customer}</td>
                        <td className="py-3 px-4 text-center">
                          {bill.paymentType?.toUpperCase() === 'CREDIT' ? (
                            <div className="flex flex-col items-center gap-1">
                              <span className="text-text-3 font-bold uppercase text-[10px]">Credit</span>
                              {bill.creditStatus?.toLowerCase() === 'cleared' ? (
                                <>
                                  <span className="text-neon-green text-[9px] bg-neon-green/10 px-1.5 py-0.5 rounded border border-neon-green/20 uppercase tracking-wider font-bold">Cleared</span>
                                  <span className="text-text-3 text-[9px]">Total Paid: ₹{bill.totalRevenue.toFixed(2)}</span>
                                </>
                              ) : (
                                <>
                                  <span className="text-neon-red text-[9px] bg-neon-red/10 px-1.5 py-0.5 rounded border border-neon-red/20 uppercase tracking-wider font-bold">
                                    ₹{(bill.creditAmount || 0).toFixed(2)} Pending
                                  </span>
                                  <span className="text-text-3 text-[9px]">Upfront: ₹{(bill.amountPaidInitially || 0).toFixed(2)}</span>
                                </>
                              )}
                            </div>
                          ) : (
                            <span className="text-text-3 uppercase">{bill.paymentType}</span>
                          )}
                        </td>
                        <td className="py-3 px-4 text-right text-text">₹{bill.gamingRevenue.toFixed(2)}</td>
                        <td className="py-3 px-4 text-right text-text">₹{bill.foodRevenue.toFixed(2)}</td>
                        <td className="py-3 px-4 text-right text-neon-red">{bill.discount > 0 ? `-₹${bill.discount.toFixed(2)}` : '-'}</td>
                        <td className="py-3 px-4 text-right text-neon-green font-bold">₹{bill.totalRevenue.toFixed(2)}</td>
                        <td className="py-3 px-4 text-text-3 text-[10px] whitespace-pre-wrap">{bill.sessionNotes || '-'}</td>
                        <td className="py-3 px-4 text-neon-blue font-bold">{bill.operator}</td>
                        <td className="py-3 px-4 text-center">
                          <button
                            onClick={() => printBill(bill.billId || bill.id, bill)}
                            className="p-1.5 bg-bg-3 hover:bg-accent hover:text-bg transition-colors rounded-lg text-text-2 tooltip-trigger"
                            title="Print Bill"
                          >
                            <Printer className="w-4 h-4" />
                          </button>
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              )}
            </div>
          </div>

          {/* ── Credit Audit Logs ── */}
          <div className="card bg-bg-2 border border-border p-6 rounded-xl shadow-lg mt-8">
            <div className="flex justify-between items-center mb-6">
              <h2 className="font-heading font-extrabold text-sm uppercase tracking-wider text-text flex items-center gap-2">
                <Clock className="w-4.5 h-4.5 text-accent" />
                Credit Audit Logs ({targetDate})
              </h2>
            </div>

            <div className="overflow-x-auto">
              {!report.creditLogs || report.creditLogs.length === 0 ? (
                <div className="text-center text-text-3 text-xs italic py-8 border border-dashed border-border rounded-lg">
                  No credit records found for the selected date.
                </div>
              ) : (
                <table className="w-full text-left border-collapse text-xs whitespace-nowrap">
                  <thead>
                    <tr className="border-b border-border text-text-3 uppercase tracking-wider font-bold text-[10px]">
                      <th className="py-3 px-4">Date Created</th>
                      <th className="py-3 px-4">Customer</th>
                      <th className="py-3 px-4">PC</th>
                      <th className="py-3 px-4 text-right">Original Bill</th>
                      <th className="py-3 px-4 text-right">Initial Paid</th>
                      <th className="py-3 px-4 text-right">Amount Due</th>
                      <th className="py-3 px-4 text-center">Status</th>
                      <th className="py-3 px-4">Date Cleared</th>
                    </tr>
                  </thead>
                  <tbody className="divide-y divide-border/40 font-mono">
                    {report.creditLogs.map(credit => (
                      <tr key={credit.creditId} className="hover:bg-bg-3/40 transition-colors">
                        <td className="py-3 px-4 text-text-2 flex items-center gap-1">
                          {new Date(credit.createdAt).toLocaleString()}
                        </td>
                        <td className="py-3 px-4 text-neon-blue font-bold">
                          {credit.customerName}
                          <div className="text-[10px] text-text-3 font-sans font-normal">{credit.customerPhone}</div>
                        </td>
                        <td className="py-3 px-4 text-text-2">{credit.pcNumber}</td>
                        <td className="py-3 px-4 text-right text-text">₹{credit.originalBillAmount.toFixed(2)}</td>
                        <td className="py-3 px-4 text-right text-text">₹{credit.amountPaidInitially.toFixed(2)}</td>
                        <td className="py-3 px-4 text-right text-neon-red font-bold">₹{credit.creditAmount.toFixed(2)}</td>
                        <td className="py-3 px-4 text-center">
                          {credit.status.toLowerCase() === 'cleared' ? (
                            <span className="text-neon-green font-bold uppercase tracking-wider text-[10px] bg-neon-green/10 px-2 py-1 rounded border border-neon-green/20">Cleared</span>
                          ) : (
                            <span className="text-neon-red font-bold uppercase tracking-wider text-[10px] bg-neon-red/10 px-2 py-1 rounded border border-neon-red/20">Pending</span>
                          )}
                        </td>
                        <td className="py-3 px-4 text-text-3">
                          {credit.clearedAt ? new Date(credit.clearedAt).toLocaleString() : '-'}
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              )}
            </div>
          </div>

          {/* ── Maintenance Audit Logs ── */}
          <div className="card bg-bg-2 border border-border p-6 rounded-xl shadow-lg mt-8">
            <div className="flex justify-between items-center mb-6">
              <h2 className="font-heading font-extrabold text-sm uppercase tracking-wider text-text flex items-center gap-2">
                <Wrench className="w-4.5 h-4.5 text-neon-orange" />
                Maintenance Logs (Last 30 Days)
              </h2>
            </div>

            <div className="overflow-x-auto">
              {!maintenanceLogs || maintenanceLogs.length === 0 ? (
                <div className="text-center text-text-3 text-xs italic py-8 border border-dashed border-border rounded-lg">
                  No maintenance records found.
                </div>
              ) : (
                <table className="w-full text-left border-collapse text-xs whitespace-nowrap">
                  <thead>
                    <tr className="border-b border-border text-text-3 uppercase tracking-wider font-bold text-[10px]">
                      <th className="py-3 px-4">PC</th>
                      <th className="py-3 px-4">Marked Date & Time</th>
                      <th className="py-3 px-4">Marked By</th>
                      <th className="py-3 px-4">Reason</th>
                      <th className="py-3 px-4">Duration</th>
                      <th className="py-3 px-4">Status</th>
                      <th className="py-3 px-4">Resolved Date & Time</th>
                      <th className="py-3 px-4">Resolution Notes</th>
                    </tr>
                  </thead>
                  <tbody className="divide-y divide-border/40 font-mono">
                    {maintenanceLogs.map(log => (
                      <tr key={log.id} className={`hover:bg-bg-3/40 transition-colors ${log.isResolved ? 'opacity-75' : ''}`}>
                        <td className="py-3 px-4 text-text font-bold">{log.pcName}</td>
                        <td className="py-3 px-4 text-text-2">
                          {log.markedAt ? new Date(log.markedAt).toLocaleString() : '-'}
                        </td>
                        <td className="py-3 px-4 text-neon-blue font-bold">{log.operatorName}</td>
                        <td className="py-3 px-4 text-text-2">{log.reason}</td>
                        <td className="py-3 px-4 text-text-2">
                          {log.durationMinutes > 0
                            ? `${Math.floor(log.durationMinutes / 60)}h ${log.durationMinutes % 60}m`
                            : (log.isResolved ? '< 1m' : '-')}
                        </td>
                        <td className="py-3 px-4 text-center">
                          {log.isResolved ? (
                            <span className="text-neon-green font-bold uppercase tracking-wider text-[10px] bg-neon-green/10 px-2 py-1 rounded border border-neon-green/20">Resolved</span>
                          ) : (
                            <span className="text-neon-orange font-bold uppercase tracking-wider text-[10px] bg-neon-orange/10 px-2 py-1 rounded border border-neon-orange/20">Active</span>
                          )}
                        </td>
                        <td className="py-3 px-4 text-text-3">
                          {log.resolvedAt ? new Date(log.resolvedAt).toLocaleString() : '-'}
                        </td>
                        <td className="py-3 px-4 text-text-3 text-[10px]">{log.resolutionNotes || '-'}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              )}
            </div>
          </div>

          {/* Finalize Button */}
          {!isHistorical && isSuperAdmin && (
            <div className="mt-8">
              <button
                onClick={handleFinalize}
                disabled={!validation?.isReady || isFinalizing}
                className="w-full py-5 rounded-xl font-bold uppercase tracking-widest text-sm transition-all bg-accent hover:bg-accent-hover text-white shadow-lg shadow-accent/20 flex justify-center items-center gap-3 disabled:opacity-50 disabled:cursor-not-allowed"
              >
                {isFinalizing ? (
                  <div className="w-5 h-5 rounded-full border-2 border-white border-t-transparent animate-spin" />
                ) : (
                  <>
                    <ShieldCheck className="w-5 h-5" />
                    Finalize EOD & Create Immutable Snapshot
                  </>
                )}
              </button>
            </div>
          )}
        </>
      ) : null}
    </div>
    {report && (
      <EodPaymentSummaryBar
        report={report}
        targetDate={targetDate}
        height={summaryBarHeight}
        onHeightChange={setSummaryBarHeight}
      />
    )}
    </>
  );
}
