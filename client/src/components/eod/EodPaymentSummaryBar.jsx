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

  const pm = report.paymentMethods;
  const rec = report.reconciliation ?? {};
  const n = (v) => Number(v ?? 0);

  // Every total on this bar is now worked out on the server and simply printed here.
  //
  // It used to be assembled in this file out of whatever figures were to hand, and it was wrong
  // in two ways at once. It added wallet top-ups AND wallet deductions into one total, which
  // counts the same rupee twice - Rs 500 topped up and Rs 90 later played reported Rs 590 of
  // takings on a day Rs 500 arrived. And it put the whole top-up figure into the CASH row
  // regardless of how it was paid, so a Rs 500 UPI top-up appeared as Rs 500 of notes and the
  // operator counting the drawer came up short by exactly that.
  //
  // Money arithmetic does not belong in a display component. There is one answer and the
  // server works it out; this shows it.
  const collected = n(pm.totalCollected);

  // Cash actually in hand: notes taken for bills, plus notes taken for top-ups, less what went
  // out of the drawer. Top-ups paid by UPI are excluded, which is the whole point.
  const cashIn = n(pm.totalCash) + n(pm.totalWalletTopUpsCash);
  const cashOut = n(report.cash.totalPettyExpenses) + n(report.cash.totalOwnerWithdrawals);
  const cashNet = cashIn - cashOut;

  const onlineIn = n(pm.totalOnline) + n(pm.totalWalletTopUpsOnline);
  const walletDeductionsTotal = n(pm.totalWalletDeductions);

  const creditsPending = report.creditLogs?.filter(c => c.status?.toLowerCase() === 'pending').reduce((acc, c) => acc + n(c.creditAmount), 0) || 0;

  const difference = n(rec.difference);
  const balances = Math.abs(difference) < 0.01;

  // The drawer's count and its difference are null until somebody counts it, and null is not
  // zero here - see CashSummaryDto. Kept as null rather than coerced, so the rows below can say
  // "not counted yet" instead of printing an empty drawer.
  const counted = report.cash.actualPhysicalCashCounted;
  const discrepancy = report.cash.totalDiscrepancy;
  const earlierDifference = Number(report.cash.differencesFoundEarlier ?? 0);

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
          {activeTab === 'payment'
            ? `Collected: ₹${collected.toFixed(2)}`
            : `In drawer (expected): ₹${n(report.cash.expectedCashInDrawer).toFixed(2)}`}
        </div>
      </div>

      <div className="flex-1 overflow-y-auto px-4 py-2">
        {activeTab === 'payment' ? (
          <div className="grid grid-cols-1 md:grid-cols-2 gap-6">
            {/* What was billed, and why that differs from what was taken.
                Every line here is a real reason the two sides can legitimately disagree —
                discounts, a customer who left owing money, an old debt paid off today. None
                of them used to be shown, so the two halves of this bar simply did not add up
                and there was no way to tell whether that was normal or a fault. */}
            <div className="overflow-x-auto">
              <table className="w-full text-left border-collapse text-[11px] whitespace-nowrap">
                <thead>
                  <tr className="border-b border-border text-text-3 uppercase tracking-wider font-bold text-[9px]">
                    <th className="py-1.5 pr-4">What was billed</th>
                    <th className="py-1.5 pr-4 text-right">Amount</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-border/40 font-mono">
                  <tr>
                    <td className="py-1.5 pr-4 text-text-2 font-sans">Gaming</td>
                    <td className="py-1.5 pr-4 text-right text-text">₹{n(report.revenue.totalGamingRevenue).toFixed(2)}</td>
                  </tr>
                  <tr>
                    <td className="py-1.5 pr-4 text-text-2 font-sans">Food</td>
                    <td className="py-1.5 pr-4 text-right text-text">₹{n(report.revenue.totalFoodRevenue).toFixed(2)}</td>
                  </tr>
                  <tr>
                    <td className="py-1.5 pr-4 text-text-2 font-sans">Less discounts</td>
                    <td className="py-1.5 pr-4 text-right text-neon-red">- ₹{n(rec.discounts).toFixed(2)}</td>
                  </tr>
                  <tr>
                    <td className="py-1.5 pr-4 text-text-2 font-sans">Less credit given today</td>
                    <td className="py-1.5 pr-4 text-right text-neon-red">- ₹{n(rec.creditGivenToday).toFixed(2)}</td>
                  </tr>
                  <tr>
                    <td className="py-1.5 pr-4 text-text-2 font-sans">Plus old credit collected today</td>
                    <td className="py-1.5 pr-4 text-right text-neon-green">+ ₹{n(rec.creditClearedToday).toFixed(2)}</td>
                  </tr>
                  <tr className="border-t border-border">
                    <td className="py-1.5 pr-4 text-text font-sans font-bold">Should have been taken</td>
                    <td className="py-1.5 pr-4 text-right text-text font-bold">₹{n(rec.shouldHaveBeenCollected).toFixed(2)}</td>
                  </tr>
                  <tr>
                    <td className="py-1.5 pr-4 text-text-2 font-sans">Actually settled</td>
                    <td className="py-1.5 pr-4 text-right text-text">₹{n(rec.actuallySettled).toFixed(2)}</td>
                  </tr>
                </tbody>
              </table>

              {/* Shown whether or not it is zero. A difference nobody is told about is how a
                  real shortfall survives a week of End of Days. */}
              <div className={`flex justify-between items-center text-xs px-3 py-2 rounded-lg border mt-2 ${
                balances
                  ? 'bg-neon-blue/10 border-neon-blue/30'
                  : 'bg-neon-red/10 border-neon-red/40'
              }`}>
                <span className={`font-bold uppercase tracking-widest text-[10px] ${balances ? 'text-neon-blue' : 'text-neon-red'}`}>
                  {balances ? '✓ Bills and payments agree' : 'Unexplained difference'}
                </span>
                <span className={`font-mono font-bold ${balances ? 'text-neon-blue' : 'text-neon-red'}`}>
                  {balances ? '₹0.00' : `${difference > 0 ? '+' : '-'}₹${Math.abs(difference).toFixed(2)}`}
                </span>
              </div>
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
                    <td className="py-1.5 pr-4 text-text-2 font-sans">
                      Cash
                      <span className="text-text-3 text-[9px] ml-1">(bills + top-ups paid in notes)</span>
                    </td>
                    <td className="py-1.5 pr-4 text-right text-neon-green">₹{cashIn.toFixed(2)}</td>
                    <td className="py-1.5 pr-4 text-right text-neon-red">₹{cashOut.toFixed(2)}</td>
                    <td className="py-1.5 pr-4 text-right text-text font-bold">₹{cashNet.toFixed(2)}</td>
                  </tr>
                  <tr>
                    <td className="py-1.5 pr-4 text-text-2 font-sans">
                      Online
                      <span className="text-text-3 text-[9px] ml-1">(bills + top-ups paid by UPI)</span>
                    </td>
                    <td className="py-1.5 pr-4 text-right text-neon-green">₹{onlineIn.toFixed(2)}</td>
                    <td className="py-1.5 pr-4 text-right text-text-3">₹0.00</td>
                    <td className="py-1.5 pr-4 text-right text-text font-bold">₹{onlineIn.toFixed(2)}</td>
                  </tr>
                </tbody>
              </table>

              <div className="flex justify-between items-center text-xs bg-neon-blue/10 px-3 py-2 rounded-lg border border-neon-blue/30 mt-2">
                <span className="font-bold text-neon-blue uppercase tracking-widest text-[10px]">Money collected today</span>
                <span className="font-mono font-bold text-base text-neon-blue">₹{collected.toFixed(2)}</span>
              </div>

              {/* Kept off the total on purpose, and said so on screen rather than left for
                  somebody to work out. This is members spending balance they paid for when
                  they topped up - possibly weeks ago. It is revenue, but no money arrives
                  today, and adding it to the takings counts the same rupee a second time. */}
              <div className="flex justify-between items-center text-xs bg-bg-3 px-3 py-2 rounded-lg border border-border mt-1.5">
                <span className="font-bold text-text-2 uppercase tracking-widest text-[10px]">
                  Paid from wallets
                  <span className="normal-case tracking-normal text-text-3 font-normal ml-1">— already collected, not new money</span>
                </span>
                <span className="font-mono font-bold text-neon-purple">₹{walletDeductionsTotal.toFixed(2)}</span>
              </div>

              {/* Free credit handed out on top-ups. Real money the owner is giving away to buy
                  loyalty, and it appeared nowhere on this screen at all — while quietly
                  inflating the takings figure, because the top-up total it was buried inside
                  was being counted as cash through the door. */}
              {n(pm.totalWalletBonusGiven) > 0 && (
                <div className="flex justify-between items-center text-xs bg-bg-3 px-3 py-2 rounded-lg border border-border mt-1.5">
                  <span className="font-bold text-text-2 uppercase tracking-widest text-[10px]">
                    Bonus given away
                    <span className="normal-case tracking-normal text-text-3 font-normal ml-1">— free credit, not income</span>
                  </span>
                  <span className="font-mono font-bold text-neon-orange">₹{n(pm.totalWalletBonusGiven).toFixed(2)}</span>
                </div>
              )}
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

              {/* Only when there was one. A handover that balanced has nothing to say here, and a
                  row reading "- ₹0" invites the reader to look for a problem that is not there.

                  It is shown BEFORE the expected total because that is where it belongs in the
                  arithmetic: opening plus takings, less what was spent and less what went astray
                  earlier, is what the drawer should hold now. Leaving it out is what made the
                  column fail to add up. */}
              {earlierDifference !== 0 && (
                <div className="flex justify-between items-center">
                  <span className="text-text-2">
                    {earlierDifference < 0 ? 'Missing at an earlier handover' : 'Extra at an earlier handover'}
                  </span>
                  <span className={`font-mono ${earlierDifference < 0 ? 'text-neon-red' : 'text-neon-orange'}`}>
                    {earlierDifference < 0 ? '- ' : '+ '}₹{Math.abs(earlierDifference).toFixed(2)}
                  </span>
                </div>
              )}

              <div className="flex justify-between items-center border-t border-border pt-1.5">
                <span className="font-bold text-text">Expected Drawer Total</span>
                <span className="font-mono font-bold text-accent">₹{report.cash.expectedCashInDrawer}</span>
              </div>

              {/* "Not counted yet" rather than ₹0. Zero reads as an empty drawer - the whole day's
                  takings gone - when the truth is that nobody has looked in it yet. */}
              <div className="flex justify-between items-center">
                <span className="font-bold text-text">Physically Counted</span>
                {counted === null || counted === undefined ? (
                  <span className="text-text-3 text-[11px] italic">not counted yet</span>
                ) : (
                  <span className="font-mono font-bold text-text">₹{counted}</span>
                )}
              </div>

              <div className="flex justify-between items-center bg-bg-3 px-3 py-1.5 rounded-lg border border-border mt-1">
                <span className="font-bold text-text uppercase tracking-widest text-[10px]">
                  {counted === null || counted === undefined ? 'Difference' : 'Total Difference'}
                </span>
                {discrepancy === null || discrepancy === undefined ? (
                  <span className="text-text-3 text-[11px] italic">unknown until it is counted</span>
                ) : (
                  <span className={`font-mono font-bold ${Number(discrepancy) === 0 ? 'text-neon-blue' : 'text-neon-red'}`}>
                    ₹{discrepancy}
                  </span>
                )}
              </div>
            </div>

            {/* Overall Collection & Business */}
            <div className="space-y-1.5 text-xs">
              <div className="flex justify-between items-center">
                <span className="text-text-2">Cash against bills</span>
                <span className="font-mono text-text">₹{n(pm.totalCash).toFixed(2)}</span>
              </div>
              <div className="flex justify-between items-center">
                <span className="text-text-2">Online against bills</span>
                <span className="font-mono text-text">₹{n(pm.totalOnline).toFixed(2)}</span>
              </div>
              <div className="flex justify-between items-center">
                <span className="text-text-2">Wallet top-ups — in notes</span>
                <span className="font-mono text-neon-green">+ ₹{n(pm.totalWalletTopUpsCash).toFixed(2)}</span>
              </div>
              <div className="flex justify-between items-center">
                <span className="text-text-2">Wallet top-ups — by UPI</span>
                <span className="font-mono text-neon-green">+ ₹{n(pm.totalWalletTopUpsOnline).toFixed(2)}</span>
              </div>

              <div className="flex justify-between items-center bg-neon-blue/10 px-3 py-1.5 rounded-lg border border-neon-blue/30 mt-1">
                <span className="font-bold text-neon-blue uppercase tracking-widest text-[10px]">Money collected today</span>
                <span className="font-mono font-bold text-base text-neon-blue">₹{collected.toFixed(2)}</span>
              </div>

              {/* Below the total, not inside it. Both of these are real and both are things an
                  owner wants to see, and neither is money that arrived today: wallet spending
                  was paid for at top-up time, and pending credit is money still owed. The old
                  version added wallet spending into the total and subtracted nothing for
                  credit, so the headline figure was larger than the day had ever produced. */}
              <div className="flex justify-between items-center pt-1.5 mt-1.5 border-t border-border">
                <span className="text-text-2">
                  Paid from wallets
                  <span className="text-text-3 text-[10px] ml-1">— collected earlier</span>
                </span>
                <span className="font-mono text-neon-purple">₹{walletDeductionsTotal.toFixed(2)}</span>
              </div>
              <div className="flex justify-between items-center">
                <span className="text-text-2">
                  Still owed to us
                  <span className="text-text-3 text-[10px] ml-1">— credit pending</span>
                </span>
                <span className="font-mono text-neon-red">₹{creditsPending.toFixed(2)}</span>
              </div>
            </div>
          </div>
        )}
      </div>
    </div>
  );
}
