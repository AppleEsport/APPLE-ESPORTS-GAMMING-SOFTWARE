import { useCallback, useEffect, useRef, useState } from 'react';
import { Wallet, GripHorizontal } from 'lucide-react';

const MIN_HEIGHT = 56;
const MAX_HEIGHT = 420;

const TABS = [
  { key: 'payment', label: 'Payment Method Summary' },
  { key: 'cash', label: 'Cash & Collection' },
];

// ── Fixed-to-viewport financial summary strip for the EOD dashboard, mirroring
// SessionActivityLog's pinned-bottom / drag-resizable pattern ──
export default function EodPaymentSummaryBar({ report, targetDate, height, onHeightChange }) {
  const resizing = useRef(false);
  const [activeTab, setActiveTab] = useState('payment');

  const handlePointerDown = useCallback((e) => {
    e.preventDefault();
    resizing.current = true;
    document.body.style.cursor = 'ns-resize';
    document.body.style.userSelect = 'none';
  }, []);

  useEffect(() => {
    const handleMove = (e) => {
      if (!resizing.current) return;
      const clientY = e.touches ? e.touches[0].clientY : e.clientY;
      const next = Math.min(MAX_HEIGHT, Math.max(MIN_HEIGHT, window.innerHeight - clientY));
      onHeightChange?.(next);
    };
    const handleUp = () => {
      resizing.current = false;
      document.body.style.cursor = '';
      document.body.style.userSelect = '';
    };
    window.addEventListener('mousemove', handleMove);
    window.addEventListener('mouseup', handleUp);
    window.addEventListener('touchmove', handleMove);
    window.addEventListener('touchend', handleUp);
    return () => {
      window.removeEventListener('mousemove', handleMove);
      window.removeEventListener('mouseup', handleUp);
      window.removeEventListener('touchmove', handleMove);
      window.removeEventListener('touchend', handleUp);
    };
  }, [onHeightChange]);

  if (!report) return null;

  const cashTotal = report.paymentMethods.totalCash + report.paymentMethods.totalWalletTopUps - report.cash.totalPettyExpenses - report.cash.totalOwnerWithdrawals;
  const onlineTotal = report.paymentMethods.totalOnline;
  const walletDeductionsTotal = report.paymentMethods.totalWalletDeductions;
  const grandTotal = cashTotal + onlineTotal + walletDeductionsTotal;
  const creditsPending = report.creditLogs?.filter(c => c.status?.toLowerCase() === 'pending').reduce((acc, c) => acc + c.creditAmount, 0) || 0;
  const overallEndTotal = report.paymentMethods.totalCash + report.paymentMethods.totalOnline + report.paymentMethods.totalWalletDeductions + report.paymentMethods.totalWalletTopUps;

  return (
    <div
      style={{ height }}
      className="fixed bottom-0 left-0 lg:left-[var(--sidebar-offset,240px)] right-0 z-30 bg-bg-2 border-t border-border flex flex-col shadow-[0_-4px_12px_rgba(0,0,0,0.25)] transition-[left] duration-200 ease-in-out"
    >
      {/* Drag handle */}
      <div
        onMouseDown={handlePointerDown}
        onTouchStart={handlePointerDown}
        className="flex items-center justify-center h-2.5 cursor-ns-resize hover:bg-bg-3 transition-colors flex-shrink-0"
      >
        <GripHorizontal className="w-4 h-4 text-text-3" />
      </div>

      <div className="px-3 py-1 border-b border-border bg-bg-3 flex items-center justify-between flex-shrink-0">
        <div className="flex items-center gap-3">
          <div className="flex items-center gap-1.5 text-[10px] font-mono font-bold uppercase tracking-widest text-text-3">
            <Wallet className="w-3 h-3" />
            {targetDate}
          </div>
          <div className="flex items-center gap-1">
            {TABS.map(tab => (
              <button
                key={tab.key}
                onClick={() => setActiveTab(tab.key)}
                className={`px-2.5 py-1 rounded text-[10px] font-bold uppercase tracking-widest transition-colors ${
                  activeTab === tab.key
                    ? 'bg-accent text-white'
                    : 'text-text-3 hover:text-text-2 hover:bg-bg-2'
                }`}
              >
                {tab.label}
              </button>
            ))}
          </div>
        </div>
        <div className="font-mono text-xs font-bold text-neon-blue">
          {activeTab === 'payment' ? `Total: ₹${grandTotal.toFixed(2)}` : `End Total: ₹${overallEndTotal.toFixed(2)}`}
        </div>
      </div>

      <div className="flex-1 overflow-y-auto px-4 py-2">
        {activeTab === 'payment' ? (
          <div className="grid grid-cols-1 md:grid-cols-2 gap-6">
            <div className="overflow-x-auto">
              <table className="w-full text-left border-collapse text-[11px] whitespace-nowrap">
                <thead>
                  <tr className="border-b border-border text-text-3 uppercase tracking-wider font-bold text-[9px]">
                    <th className="py-1.5 pr-4">Revenue Category</th>
                    <th className="py-1.5 pr-4 text-right">Income</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-border/40 font-mono">
                  <tr>
                    <td className="py-1.5 pr-4 text-text-2 font-sans">Gaming Revenue</td>
                    <td className="py-1.5 pr-4 text-right text-text">₹{report.revenue.totalGamingRevenue.toFixed(2)}</td>
                  </tr>
                  <tr>
                    <td className="py-1.5 pr-4 text-text-2 font-sans">Food Revenue</td>
                    <td className="py-1.5 pr-4 text-right text-text">₹{report.revenue.totalFoodRevenue.toFixed(2)}</td>
                  </tr>
                  <tr>
                    <td className="py-1.5 pr-4 text-text-2 font-sans">Wallet Top-Ups</td>
                    <td className="py-1.5 pr-4 text-right text-text">₹{report.paymentMethods.totalWalletTopUps.toFixed(2)}</td>
                  </tr>
                </tbody>
              </table>
            </div>

            <div className="overflow-x-auto">
              <table className="w-full text-left border-collapse text-[11px] whitespace-nowrap">
                <thead>
                  <tr className="border-b border-border text-text-3 uppercase tracking-wider font-bold text-[9px]">
                    <th className="py-1.5 pr-4">Payment Method</th>
                    <th className="py-1.5 pr-4 text-right">Income</th>
                    <th className="py-1.5 pr-4 text-right">Expense</th>
                    <th className="py-1.5 pr-4 text-right">Total</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-border/40 font-mono">
                  <tr>
                    <td className="py-1.5 pr-4 text-text-2 font-sans">Cash</td>
                    <td className="py-1.5 pr-4 text-right text-neon-green">
                      ₹{(report.paymentMethods.totalCash + report.paymentMethods.totalWalletTopUps).toFixed(2)}
                    </td>
                    <td className="py-1.5 pr-4 text-right text-neon-red">
                      ₹{(report.cash.totalPettyExpenses + report.cash.totalOwnerWithdrawals).toFixed(2)}
                    </td>
                    <td className="py-1.5 pr-4 text-right text-text font-bold">₹{cashTotal.toFixed(2)}</td>
                  </tr>
                  <tr>
                    <td className="py-1.5 pr-4 text-text-2 font-sans">Online</td>
                    <td className="py-1.5 pr-4 text-right text-neon-green">₹{onlineTotal.toFixed(2)}</td>
                    <td className="py-1.5 pr-4 text-right text-text-3">₹0.00</td>
                    <td className="py-1.5 pr-4 text-right text-text font-bold">₹{onlineTotal.toFixed(2)}</td>
                  </tr>
                  <tr>
                    <td className="py-1.5 pr-4 text-text-2 font-sans">Wallet Deductions (Gaming/Food)</td>
                    <td className="py-1.5 pr-4 text-right text-neon-green">₹{walletDeductionsTotal.toFixed(2)}</td>
                    <td className="py-1.5 pr-4 text-right text-text-3">₹0.00</td>
                    <td className="py-1.5 pr-4 text-right text-text font-bold">₹{walletDeductionsTotal.toFixed(2)}</td>
                  </tr>
                </tbody>
              </table>

              <div className="flex justify-between items-center text-xs bg-bg-3 px-3 py-2 rounded-lg border border-border mt-2">
                <span className="font-bold text-text uppercase tracking-widest text-[10px]">Discounts Applied</span>
                <span className="font-mono font-bold text-neon-red">-₹{report.revenue.totalDiscounts.toFixed(2)}</span>
              </div>
              <div className="flex justify-between items-center text-xs bg-neon-blue/10 px-3 py-2 rounded-lg border border-neon-blue/30 mt-1.5">
                <span className="font-bold text-neon-blue uppercase tracking-widest text-[10px]">Total Amount</span>
                <span className="font-mono font-bold text-base text-neon-blue">₹{grandTotal.toFixed(2)}</span>
              </div>
            </div>
          </div>
        ) : (
          <div className="grid grid-cols-1 md:grid-cols-2 gap-6">
            {/* Cash Lifecycle Summary */}
            <div className="space-y-1.5 text-xs">
              <div className="flex justify-between items-center">
                <span className="text-text-2">Opening Balance Total</span>
                <span className="font-mono text-text">₹{report.cash.totalOpeningBalance}</span>
              </div>
              <div className="flex justify-between items-center">
                <span className="text-text-2">Cash Sales + Wallet TopUps</span>
                <span className="font-mono text-neon-green">+ ₹{report.cash.totalCashSales}</span>
              </div>
              <div className="flex justify-between items-center">
                <span className="text-text-2">Petty Expenses</span>
                <span className="font-mono text-neon-red">- ₹{report.cash.totalPettyExpenses}</span>
              </div>
              <div className="flex justify-between items-center border-t border-border pt-1.5">
                <span className="font-bold text-text">Expected Drawer Total</span>
                <span className="font-mono font-bold text-accent">₹{report.cash.expectedCashInDrawer}</span>
              </div>
              <div className="flex justify-between items-center">
                <span className="font-bold text-text">Physically Counted</span>
                <span className="font-mono font-bold text-text">₹{report.cash.actualPhysicalCashCounted}</span>
              </div>
              <div className="flex justify-between items-center bg-bg-3 px-3 py-1.5 rounded-lg border border-border mt-1">
                <span className="font-bold text-text uppercase tracking-widest text-[10px]">Total Difference</span>
                <span className={`font-mono font-bold ${report.cash.totalDiscrepancy === 0 ? 'text-neon-blue' : 'text-neon-red'}`}>
                  ₹{report.cash.totalDiscrepancy}
                </span>
              </div>
            </div>

            {/* Overall Collection & Business */}
            <div className="space-y-1.5 text-xs">
              <div className="flex justify-between items-center">
                <span className="text-text-2">Cash</span>
                <span className="font-mono text-text">₹{report.paymentMethods.totalCash}</span>
              </div>
              <div className="flex justify-between items-center">
                <span className="text-text-2">Online</span>
                <span className="font-mono text-text">₹{report.paymentMethods.totalOnline}</span>
              </div>
              <div className="flex justify-between items-center">
                <span className="text-text-2">Wallet Deductions (Gaming/Food)</span>
                <span className="font-mono text-neon-purple">₹{report.paymentMethods.totalWalletDeductions}</span>
              </div>
              <div className="flex justify-between items-center">
                <span className="text-text-2">Wallet Top-Ups (Cash Collected)</span>
                <span className="font-mono text-neon-green">+ ₹{report.paymentMethods.totalWalletTopUps}</span>
              </div>
              <div className="flex justify-between items-center">
                <span className="text-text-2">Credits Pending</span>
                <span className="font-mono text-neon-red">-₹{creditsPending.toFixed(2)}</span>
              </div>
              <div className="flex justify-between items-center bg-neon-blue/10 px-3 py-1.5 rounded-lg border border-neon-blue/30 mt-1">
                <span className="font-bold text-neon-blue uppercase tracking-widest text-[10px]">Overall End Total</span>
                <span className="font-mono font-bold text-base text-neon-blue">₹{overallEndTotal.toFixed(2)}</span>
              </div>
            </div>
          </div>
        )}
      </div>
    </div>
  );
}
