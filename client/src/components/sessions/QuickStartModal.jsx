import { useState } from 'react';
import { motion, AnimatePresence } from 'framer-motion';
import { Zap, X } from 'lucide-react';
import { useAuth } from '../../contexts/AuthContext';
import { useBranch } from '../../contexts/BranchContext';
import { startSessionRemoteAware } from '../../api/branchCommands.api';
import { logActivity } from '../../utils/sessionLog';
import { useToast } from '../ui/Toast';

// ── Double-click on an idle PC tile → skip the full plan form, just ask a
// name and start an open-ended (Pay-As-You-Go) session immediately ──
export default function QuickStartModal({ pc, onClose, onActionSuccess }) {
  const { isSuperAdmin, user } = useAuth();
  const { activeBranch } = useBranch();
  const toast = useToast();
  const [customerName, setCustomerName] = useState('');
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState(null);

  if (!pc) return null;

  const handleStart = async (e) => {
    e.preventDefault();
    if (!customerName.trim()) {
      setError('Customer name is required.');
      return;
    }
    setLoading(true);
    setError(null);
    try {
      await startSessionRemoteAware(activeBranch?.id, pc.id, {
        pcId: pc.id,
        customerName: customerName.trim(),
        customerType: 'Walk-in',
        durationMinutes: 0,
        packageName: 'Quick Start (Pay-As-You-Go)',
        expectedAmount: 0,
        isOverride: isSuperAdmin,
        operatorId: user?.id,
        memberId: null,
      });
      logActivity(`${pc.name}: Quick-started for ${customerName.trim()}.`, 'success');
      toast.success(`Session started on ${pc.name}!`);
      onActionSuccess?.();
    } catch (err) {
      setError(err.response?.data?.error || err.response?.data?.message || 'Failed to start session');
    } finally {
      setLoading(false);
    }
  };

  return (
    <AnimatePresence>
      <div className="fixed inset-0 z-50 flex items-center justify-center p-4 bg-black/60 backdrop-blur-sm">
        <motion.div
          initial={{ opacity: 0, scale: 0.96, y: 10 }}
          animate={{ opacity: 1, scale: 1, y: 0 }}
          exit={{ opacity: 0, scale: 0.96, y: 10 }}
          className="w-full max-w-xs bg-bg-2 border border-border rounded-xl shadow-2xl overflow-hidden"
        >
          <div className="px-4 py-3 border-b border-border bg-bg-3 flex items-center justify-between">
            <h2 className="font-heading font-bold text-text uppercase tracking-wider text-sm flex items-center gap-2">
              <Zap className="w-4 h-4 text-pc-active" />
              Quick Start — {pc.name}
            </h2>
            <button onClick={onClose} className="p-1 text-text-3 hover:text-text rounded transition-colors">
              <X className="w-4 h-4" />
            </button>
          </div>
          <form onSubmit={handleStart} className="p-4 space-y-3">
            <p className="text-text-3 text-[10px] font-mono">Pay-As-You-Go session — billed live by elapsed time.</p>
            {error && (
              <div className="p-2.5 bg-neon-red/10 border border-neon-red/20 rounded text-neon-red text-xs">
                {error}
              </div>
            )}
            <input
              type="text"
              value={customerName}
              onChange={(e) => setCustomerName(e.target.value)}
              placeholder="Customer name..."
              autoFocus
              className="w-full bg-bg-3 border border-border rounded px-3 py-2 text-sm text-text placeholder-text-3 focus:border-pc-active focus:outline-none transition-colors"
            />
            <button
              type="submit"
              disabled={loading}
              className="w-full py-2.5 rounded border border-pc-active/50 bg-pc-active/10 text-pc-active font-heading font-bold uppercase tracking-widest text-sm hover:bg-pc-active/20 transition-colors flex items-center justify-center gap-2 disabled:opacity-50"
            >
              {loading
                ? <span className="w-4 h-4 border-2 border-pc-active border-t-transparent rounded-full animate-spin" />
                : <Zap className="w-4 h-4" />
              }
              {loading ? 'Starting...' : 'Start Now'}
            </button>
          </form>
        </motion.div>
      </div>
    </AnimatePresence>
  );
}
