import { useState } from 'react';
import { motion } from 'framer-motion';
import { AlertTriangle, Loader2, Store, Zap, WifiOff } from 'lucide-react';
import api from '../../config/api';

/**
 * Asked when an operator logs back in to a shift that was left open with a hole in it.
 *
 * The system can see the hole. It cannot tell whether the shop was shut for the night or the
 * power went out, and guessing either way is wrong: assume a fault and the owner gets a
 * power-cut email most mornings somebody simply closed up; assume a normal close and a real
 * overnight outage is never reported at all.
 *
 * So it asks the one person who knows, and nothing reaches the owner until they answer.
 *
 * Deliberately impossible to dismiss: no close button, no click-outside, no escape key. An
 * answer that can be skipped will be skipped, and then the whole mechanism is decoration.
 */

const REASONS = [
  {
    value: 0,
    label: 'The shop was closed',
    detail: 'Nothing was wrong. Nobody was using the system.',
    icon: Store,
    tone: 'text-neon-green border-neon-green/40 bg-neon-green/5',
  },
  {
    value: 1,
    label: 'The power went off',
    detail: 'Everything stopped. Customers could not play.',
    icon: Zap,
    tone: 'text-neon-red border-neon-red/40 bg-neon-red/5',
  },
  {
    value: 2,
    label: 'The internet went down',
    detail: 'The shop kept working, but nothing reached head office.',
    icon: WifiOff,
    tone: 'text-neon-orange border-neon-orange/40 bg-neon-orange/5',
  },
];

function howLong(minutes) {
  if (minutes < 60) return `${minutes} minutes`;
  const h = Math.floor(minutes / 60);
  const m = minutes % 60;
  const hours = `${h} hour${h === 1 ? '' : 's'}`;
  return m === 0 ? hours : `${hours} ${m} minutes`;
}

export default function ShiftGapModal({ shiftId, unattendedMinutes, onAnswered }) {
  const [reason, setReason] = useState(null);
  const [note, setNote] = useState('');
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState('');

  const submit = async () => {
    if (reason === null) return;
    setSaving(true);
    setError('');
    try {
      await api.post('/shift-gap/explain', {
        shiftId,
        unattendedMinutes,
        reason,
        note: note.trim() || null,
      });
      sessionStorage.removeItem('pendingShiftGap');
      onAnswered();
    } catch (err) {
      // Left on screen on failure. Clearing it would lose the answer and the question.
      setError(err.response?.data?.error || 'Could not save that. Try again.');
      setSaving(false);
    }
  };

  return (
    <div className="fixed inset-0 z-[200] flex items-center justify-center p-4 bg-black/90 backdrop-blur-lg">
      <motion.div
        initial={{ opacity: 0, scale: 0.94, y: 16 }}
        animate={{ opacity: 1, scale: 1, y: 0 }}
        transition={{ duration: 0.28, ease: 'easeOut' }}
        className="w-full max-w-lg bg-bg-2 border border-border rounded-2xl shadow-2xl overflow-hidden"
      >
        <div className="px-7 py-6 border-b border-border bg-bg-3">
          <div className="flex items-start gap-3">
            <div className="w-10 h-10 rounded-full bg-neon-orange/10 border border-neon-orange/40 flex items-center justify-center flex-shrink-0">
              <AlertTriangle className="w-5 h-5 text-neon-orange" />
            </div>
            <div>
              <h2 className="font-heading font-bold text-text text-xl">What happened?</h2>
              <p className="text-text-2 text-sm mt-1.5 leading-relaxed">
                Your last shift was never finished, and the system was not used for{' '}
                <strong className="text-text">{howLong(unattendedMinutes)}</strong>.
                Tell us why, and you can carry on.
              </p>
            </div>
          </div>
        </div>

        <div className="px-7 py-6 space-y-3">
          {REASONS.map(({ value, label, detail, icon: Icon, tone }) => {
            const picked = reason === value;
            return (
              <button
                key={value}
                type="button"
                onClick={() => setReason(value)}
                className={`w-full text-left flex items-start gap-3 p-4 rounded-xl border transition-all ${
                  picked ? tone : 'border-border bg-bg-3 hover:border-border-light text-text-2'
                }`}
              >
                <Icon className={`w-5 h-5 mt-0.5 flex-shrink-0 ${picked ? '' : 'text-text-3'}`} />
                <span>
                  <span className={`block text-sm font-bold ${picked ? '' : 'text-text'}`}>{label}</span>
                  <span className="block text-text-3 text-[11px] mt-1 leading-relaxed">{detail}</span>
                </span>
              </button>
            );
          })}

          <div className="pt-1">
            <label className="block text-text-3 text-[11px] uppercase tracking-wider mb-2">
              Anything to add? (not required)
            </label>
            <input
              type="text"
              value={note}
              onChange={(e) => setNote(e.target.value)}
              maxLength={200}
              placeholder="e.g. power came back around 8pm"
              className="input w-full text-sm"
            />
          </div>

          {error && <p className="text-neon-red text-xs">{error}</p>}
        </div>

        <div className="px-7 pb-7">
          <button
            onClick={submit}
            disabled={reason === null || saving}
            className="btn-primary w-full flex items-center justify-center gap-2 disabled:opacity-40 disabled:cursor-not-allowed"
          >
            {saving ? <Loader2 className="w-5 h-5 animate-spin" /> : 'Save and carry on'}
          </button>
          <p className="text-text-3 text-[11px] text-center mt-3">
            This has to be answered before you can use the system.
          </p>
        </div>
      </motion.div>
    </div>
  );
}
