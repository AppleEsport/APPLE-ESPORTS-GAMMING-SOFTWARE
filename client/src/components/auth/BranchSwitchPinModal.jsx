import React, { useState, useEffect, useCallback } from 'react';
import { ShieldAlert, X } from 'lucide-react';
import api from '../../config/api';
import { useToast } from '../ui/Toast';

// Access itself was never the gap here - BranchIsolationAttribute already lets an Admin reach
// any branch, same as Super Admin. This exists purely so a branch switch leaves an
// accountability record (Audit Trail: who, which branch, when) instead of happening silently.
// Super Admin's own switching is untouched - this only gates Admin.
export default function BranchSwitchPinModal({ isOpen, branch, onClose, onConfirmed }) {
  const [pin, setPin] = useState('');
  const [loading, setLoading] = useState(false);
  const toast = useToast();

  useEffect(() => {
    if (isOpen) setPin('');
  }, [isOpen]);

  const handleSubmit = useCallback(async (currentPin) => {
    const submitPin = currentPin || pin;
    if (!submitPin) return;

    setLoading(true);
    try {
      await api.post('/auth/branches/switch-confirm', {
        accessPin: submitPin,
        branchId: branch?.id ?? null,
      });
      onConfirmed();
    } catch (err) {
      toast.error(err.response?.data?.error || 'Invalid PIN');
      setPin('');
    } finally {
      setLoading(false);
    }
  }, [pin, branch, onConfirmed, toast]);

  const appendPin = useCallback((num) => {
    setPin(p => {
      const newPin = p.length < 6 ? p + num : p;
      if (newPin.length === 4 || newPin.length === 6) {
        setTimeout(() => handleSubmit(newPin), 50);
      }
      return newPin;
    });
  }, [handleSubmit]);

  const removePin = useCallback(() => setPin(p => p.slice(0, -1)), []);

  useEffect(() => {
    if (!isOpen) return;
    const handleKeyDown = (e) => {
      if (loading) return;
      if (e.key === 'Escape') onClose();
      else if (e.key === 'Backspace') removePin();
      else if (e.key === 'Enter') handleSubmit();
      else if (/^[0-9]$/.test(e.key)) appendPin(e.key);
    };
    window.addEventListener('keydown', handleKeyDown);
    return () => window.removeEventListener('keydown', handleKeyDown);
  }, [isOpen, loading, appendPin, removePin, handleSubmit, onClose]);

  if (!isOpen) return null;

  return (
    <div className="fixed inset-0 z-[100] flex items-center justify-center bg-black/80 backdrop-blur-sm p-4">
      <div className="bg-bg-2 border border-border rounded-xl w-full max-w-xs shadow-2xl overflow-hidden">
        <div className="flex items-center justify-between p-4 border-b border-border">
          <div className="flex items-center gap-2">
            <ShieldAlert className="w-5 h-5 text-accent" />
            <h3 className="font-heading font-bold tracking-wide text-accent text-sm">CONFIRM PIN</h3>
          </div>
          <button onClick={onClose} className="text-text-3 hover:text-text p-1">
            <X className="w-5 h-5" />
          </button>
        </div>

        <div className="p-6">
          <p className="text-xs text-text-2 text-center mb-6">
            Enter your PIN to switch to{' '}
            <span className="font-semibold text-text">{branch?.name || 'All Branches (Global)'}</span>
          </p>

          <div className="flex justify-center mb-6">
            <div className="flex gap-3">
              {Array.from({ length: Math.max(pin.length, 4) }).map((_, i) => (
                <div
                  key={i}
                  className={`w-4 h-4 rounded-full border-2 transition-all ${
                    i < pin.length ? 'bg-accent border-accent shadow-[0_0_10px_rgba(220,38,38,0.5)]' : 'bg-bg-3 border-border'
                  }`}
                />
              ))}
            </div>
          </div>

          <div className="grid grid-cols-3 gap-3">
            {[1, 2, 3, 4, 5, 6, 7, 8, 9].map(num => (
              <button
                key={num}
                type="button"
                disabled={loading}
                onClick={() => appendPin(num.toString())}
                className="aspect-square bg-bg-3 hover:bg-bg border border-border hover:border-accent/50 rounded-lg text-xl font-mono text-text transition-all active:scale-95 disabled:opacity-50"
              >
                {num}
              </button>
            ))}
            <button
              type="button"
              disabled={loading}
              onClick={() => setPin('')}
              className="aspect-square bg-bg-3 hover:bg-neon-red/10 border border-border hover:border-neon-red/50 rounded-lg text-xs font-bold text-neon-red transition-all active:scale-95 disabled:opacity-50"
            >
              CLEAR
            </button>
            <button
              type="button"
              disabled={loading}
              onClick={() => appendPin('0')}
              className="aspect-square bg-bg-3 hover:bg-bg border border-border hover:border-accent/50 rounded-lg text-xl font-mono text-text transition-all active:scale-95 disabled:opacity-50"
            >
              0
            </button>
            <button
              type="button"
              disabled={loading}
              onClick={removePin}
              className="aspect-square bg-bg-3 hover:bg-bg border border-border hover:border-accent/50 rounded-lg flex items-center justify-center text-text transition-all active:scale-95 disabled:opacity-50"
            >
              <svg className="w-6 h-6" fill="none" stroke="currentColor" viewBox="0 0 24 24">
                <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M12 14l2-2m0 0l2-2m-2 2l-2-2m2 2l2 2M3 12l6.414 6.414a2 2 0 001.414.586H19a2 2 0 002-2V7a2 2 0 00-2-2h-8.172a2 2 0 00-1.414.586L3 12z" />
              </svg>
            </button>
          </div>
        </div>
      </div>
    </div>
  );
}
