import { useState, useEffect } from 'react';
import { motion } from 'framer-motion';
import {
  AlertTriangle, Banknote, Package, Loader2, ArrowRight,
  UserX, ScanLine, CheckCircle2,
} from 'lucide-react';
import api from '../../config/api';

/**
 * Shown when an operator logs in and finds a shift somebody else never closed.
 *
 * The owner's instruction: "B will close A's shift and count all the things and then B will log
 * in." So the counting happens here, before anything else, and the server has not issued this
 * operator a shift at all — which is why this cannot be skipped by refreshing past it. There is
 * no close button and no escape key for the same reason as the gap question: an answer that can
 * be dismissed will be.
 *
 * The count is blind. Nothing on this screen says what the system expects to be in the drawer or
 * on the shelf until after the figures have been submitted and written down. Showing them first
 * turns a count into a tick-box, and a tick-box is how a shortfall silently becomes the wrong
 * person's fault — the one thing this whole flow exists to stop.
 */

function howLong(minutes) {
  if (minutes < 60) return `${minutes} minutes`;
  const h = Math.floor(minutes / 60);
  const m = minutes % 60;
  const hours = `${h} hour${h === 1 ? '' : 's'}`;
  return m === 0 ? hours : `${hours} ${m} minutes`;
}

function whenIst(iso) {
  if (!iso) return '';
  return new Date(iso).toLocaleString('en-IN', {
    day: '2-digit', month: 'short', hour: '2-digit', minute: '2-digit', hour12: true,
  });
}

const rupees = (n) => `₹${Number(n || 0).toLocaleString('en-IN', { minimumFractionDigits: 2, maximumFractionDigits: 2 })}`;

export default function ShiftTakeoverModal({ pending, onCompleted }) {
  // "count" until the figures are in, then "reason" once the comparison comes back. A handover
  // interrupted half way resumes at whichever stage the server says it is at, never back at the
  // count — that figure is already on record.
  const [stage, setStage] = useState(pending.stage || 'count');
  const [comparison, setComparison] = useState(pending.comparison || null);

  const [cash, setCash] = useState('');
  const [stock, setStock] = useState({});
  const [reason, setReason] = useState('');
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState('');

  const items = pending.stockItems || [];
  const hasDrawer = pending.hasOpenDrawer !== false;

  useEffect(() => {
    if (pending.stage === 'reason' && pending.comparison) {
      setStage('reason');
      setComparison(pending.comparison);
    }
  }, [pending]);

  const everyItemCounted = items.every((i) => stock[i.id] !== undefined && stock[i.id] !== '');
  const canSubmitCount = (!hasDrawer || cash !== '') && everyItemCounted;

  const submitCount = async () => {
    if (!canSubmitCount) return;
    setSaving(true);
    setError('');
    try {
      const { data } = await api.post('/shift-takeover/count', {
        countedCash: hasDrawer ? Number(cash) : 0,
        stockCounts: items.map((i) => ({ inventoryId: i.id, counted: Number(stock[i.id]) })),
      });
      const result = data.data;
      setComparison(result.comparison);

      if (result.completed) {
        onCompleted(result.shiftId);
        return;
      }
      setStage('reason');
      setSaving(false);
    } catch (err) {
      setError(err.response?.data?.error || 'Could not save that count. Try again.');
      setSaving(false);
    }
  };

  const submitReason = async () => {
    if (!reason.trim()) return;
    setSaving(true);
    setError('');
    try {
      const { data } = await api.post('/shift-takeover/confirm', { reason: reason.trim() });
      onCompleted(data.data.shiftId);
    } catch (err) {
      setError(err.response?.data?.error || 'Could not finish the handover. Try again.');
      setSaving(false);
    }
  };

  const cashDifference = Number(comparison?.cashDifference || 0);
  const stockDifferences = comparison?.stockDifferences || [];

  return (
    <div className="fixed inset-0 z-[210] flex items-center justify-center p-4 bg-black/90 backdrop-blur-lg">
      <motion.div
        initial={{ opacity: 0, scale: 0.94, y: 16 }}
        animate={{ opacity: 1, scale: 1, y: 0 }}
        transition={{ duration: 0.28, ease: 'easeOut' }}
        className="w-full max-w-lg bg-bg-2 border border-border rounded-2xl shadow-2xl overflow-hidden flex flex-col"
        style={{ maxHeight: '92vh' }}
      >
        {/* Header */}
        <div className="px-7 py-6 border-b border-border bg-bg-3 flex-shrink-0">
          <div className="flex items-start gap-3">
            <div className="w-10 h-10 rounded-full bg-neon-orange/10 border border-neon-orange/40 flex items-center justify-center flex-shrink-0">
              {stage === 'count'
                ? <UserX className="w-5 h-5 text-neon-orange" />
                : <AlertTriangle className="w-5 h-5 text-neon-orange" />}
            </div>
            <div>
              <h2 className="font-heading font-bold text-text text-xl">
                {stage === 'count'
                  ? `${pending.outgoingOperatorName}'s shift was never closed`
                  : 'What you found does not match'}
              </h2>
              <p className="text-text-2 text-sm mt-1.5 leading-relaxed">
                {stage === 'count' ? (
                  <>
                    It has been open since{' '}
                    <strong className="text-text">{whenIst(pending.startedAt)}</strong> and nothing
                    has happened on it for{' '}
                    <strong className="text-text">{howLong(pending.unattendedMinutes)}</strong>.
                    Count what is actually here, and their shift will be closed with your count
                    before yours starts.
                  </>
                ) : (
                  <>
                    Your count is saved and cannot be changed. Tell the owner what you think
                    happened, and you can get to work.
                  </>
                )}
              </p>
              {stage === 'count' && pending.alsoClosing > 0 && (
                <p className="text-text-3 text-[11px] mt-2 leading-relaxed">
                  {pending.alsoClosing} other shift{pending.alsoClosing === 1 ? ' was' : 's were'} also
                  left open here. There is one drawer, so you count it once and they all close.
                </p>
              )}
            </div>
          </div>
        </div>

        {/* Body */}
        <div className="flex-1 overflow-y-auto px-7 py-6 space-y-5">
          {stage === 'count' && (
            <>
              <div className="p-3 bg-accent/5 border border-accent/25 rounded-lg text-text-2 text-xs flex items-start gap-2">
                <ScanLine className="w-3.5 h-3.5 mt-0.5 shrink-0 text-accent" />
                <p>
                  Count first, then you will be shown what the system expected. Any money missing
                  is recorded against <strong className="text-text">{pending.outgoingOperatorName}</strong>'s
                  shift, not yours — you start from what is actually in the drawer.
                </p>
              </div>

              {hasDrawer ? (
                <div className="space-y-2">
                  <label className="text-xs uppercase tracking-wider font-bold text-text-2 flex items-center gap-1.5">
                    <Banknote className="w-3.5 h-3.5" /> Cash in the drawer
                  </label>
                  <div className="relative">
                    <span className="absolute left-4 top-1/2 -translate-y-1/2 font-mono text-text-3 text-xl">₹</span>
                    <input
                      type="number"
                      min="0"
                      step="0.01"
                      placeholder="0.00"
                      value={cash}
                      onChange={(e) => setCash(e.target.value)}
                      className="w-full bg-bg-3 border border-border text-text font-mono text-2xl rounded-xl py-4 pl-12 pr-4 focus:border-accent focus:ring-1 focus:ring-accent transition-all outline-none"
                      autoFocus
                    />
                  </div>
                </div>
              ) : (
                <div className="p-3 bg-bg-3 border border-border rounded-lg text-text-3 text-xs">
                  No drawer was open on that shift, so there is no cash to count.
                </div>
              )}

              {items.length > 0 && (
                <div className="space-y-2">
                  <label className="text-xs uppercase tracking-wider font-bold text-text-2 flex items-center gap-1.5">
                    <Package className="w-3.5 h-3.5" /> Stock on the shelf
                  </label>
                  <div className="space-y-2 max-h-56 overflow-y-auto pr-1">
                    {items.map((item) => (
                      <div
                        key={item.id}
                        className="flex items-center gap-3 p-3 rounded-lg border border-border bg-bg-3"
                      >
                        <div className="flex-1 min-w-0">
                          <span className="block text-sm font-semibold text-text truncate">{item.name}</span>
                          {item.category && (
                            <span className="block text-[10px] text-text-3 font-mono mt-0.5">{item.category}</span>
                          )}
                        </div>
                        <input
                          type="number"
                          min="0"
                          placeholder="—"
                          value={stock[item.id] ?? ''}
                          onChange={(e) => setStock((prev) => ({ ...prev, [item.id]: e.target.value }))}
                          className="w-20 bg-bg border border-border text-text font-mono text-sm rounded-lg py-1.5 px-2 text-center focus:outline-none focus:ring-1 focus:border-accent focus:ring-accent/30"
                        />
                      </div>
                    ))}
                  </div>
                  {!everyItemCounted && (
                    <p className="text-text-3 text-[11px]">
                      Every item needs a number, including the ones at zero.
                    </p>
                  )}
                </div>
              )}
            </>
          )}

          {stage === 'reason' && comparison && (
            <>
              {hasDrawer && (
                <div className="bg-bg-3 rounded-xl border border-border p-4 space-y-2">
                  <div className="flex justify-between items-center text-xs">
                    <span className="text-text-3">
                      What {comparison.outgoingOperatorName} should have left
                    </span>
                    <span className="font-mono font-bold text-text">{rupees(comparison.expectedCash)}</span>
                  </div>
                  <div className="flex justify-between items-center text-xs">
                    <span className="text-text-3">What you counted</span>
                    <span className="font-mono font-bold text-text">{rupees(comparison.countedCash)}</span>
                  </div>
                  <div className="flex justify-between items-center text-sm pt-2 border-t border-border">
                    <span className="text-text-2 font-bold">
                      {cashDifference === 0 ? 'Difference' : cashDifference < 0 ? 'Missing' : 'Extra'}
                    </span>
                    <span className={`font-mono font-bold ${
                      cashDifference === 0 ? 'text-neon-green'
                        : cashDifference < 0 ? 'text-neon-red' : 'text-neon-orange'
                    }`}>
                      {cashDifference === 0 ? 'None' : rupees(Math.abs(cashDifference))}
                    </span>
                  </div>
                </div>
              )}

              {stockDifferences.length > 0 && (
                <div className="bg-bg-3 rounded-xl border border-border p-4 space-y-2">
                  <div className="text-[10px] text-text-3 uppercase tracking-wider font-bold flex items-center gap-1.5">
                    <Package className="w-3 h-3" /> Stock that did not match
                  </div>
                  {stockDifferences.map((d) => (
                    <div key={d.inventoryId} className="flex justify-between items-center text-xs">
                      <span className="text-text-2 truncate pr-3">{d.itemName}</span>
                      <span className="font-mono text-text-3 whitespace-nowrap">
                        {d.counted} counted, {d.expected} expected{' '}
                        <span className={d.difference < 0 ? 'text-neon-red' : 'text-neon-orange'}>
                          ({d.difference > 0 ? '+' : ''}{d.difference})
                        </span>
                      </span>
                    </div>
                  ))}
                </div>
              )}

              <div className="space-y-2">
                <label className="text-xs uppercase tracking-wider font-bold text-neon-orange flex items-center gap-1.5">
                  <AlertTriangle className="w-3 h-3" /> What do you think happened?
                </label>
                <textarea
                  rows={3}
                  value={reason}
                  onChange={(e) => setReason(e.target.value)}
                  maxLength={500}
                  placeholder="e.g. two crates of drinks went out this evening and were never rung up"
                  className="w-full bg-bg-3 border border-neon-orange/40 text-text text-sm rounded-xl p-3 focus:border-neon-orange outline-none focus:ring-1 focus:ring-neon-orange/30 resize-none"
                  autoFocus
                />
                <p className="text-text-3 text-[11px] leading-relaxed">
                  This goes to the owner with both figures. The difference is recorded against{' '}
                  {comparison.outgoingOperatorName}'s shift — you are starting from what is
                  actually in the drawer.
                </p>
              </div>
            </>
          )}

          {error && <p className="text-neon-red text-xs">{error}</p>}
        </div>

        {/* Action */}
        <div className="px-7 pb-7 pt-1 flex-shrink-0">
          {stage === 'count' ? (
            <button
              onClick={submitCount}
              disabled={!canSubmitCount || saving}
              className="btn-primary w-full flex items-center justify-center gap-2 disabled:opacity-40 disabled:cursor-not-allowed"
            >
              {saving ? <Loader2 className="w-5 h-5 animate-spin" /> : (
                <>
                  <CheckCircle2 className="w-4 h-4" />
                  Save my count
                  <ArrowRight className="w-4 h-4" />
                </>
              )}
            </button>
          ) : (
            <button
              onClick={submitReason}
              disabled={!reason.trim() || saving}
              className="btn-primary w-full flex items-center justify-center gap-2 disabled:opacity-40 disabled:cursor-not-allowed"
            >
              {saving ? <Loader2 className="w-5 h-5 animate-spin" /> : 'Close their shift and start mine'}
            </button>
          )}
          <p className="text-text-3 text-[11px] text-center mt-3">
            Your shift starts once this is done. Nothing else works until then.
          </p>
        </div>
      </motion.div>
    </div>
  );
}
