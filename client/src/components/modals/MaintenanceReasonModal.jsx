import React, { useState } from 'react';
import { Wrench, X } from 'lucide-react';

export function MaintenanceReasonModal({ isOpen, pcNumber, onConfirm, onCancel, isLoading }) {
  const [reason, setReason] = useState('');

  const handleConfirm = () => {
    if (!reason.trim()) {
      alert('Please enter a reason for marking this PC as maintenance');
      return;
    }
    onConfirm(reason.trim());
    setReason('');
  };

  const handleCancel = () => {
    setReason('');
    onCancel();
  };

  if (!isOpen) return null;

  return (
    <div className="fixed inset-0 bg-black/60 backdrop-blur-sm flex items-center justify-center z-50 p-4">
      <div className="bg-bg-2 border border-border rounded-xl shadow-2xl max-w-md w-full overflow-hidden animate-[fadeIn_0.15s_ease-out]">
        {/* Header */}
        <div className="bg-bg-3 border-b border-border px-6 py-4 flex items-center justify-between">
          <div className="flex items-center gap-3">
            <div className="p-2 bg-neon-orange/10 rounded-lg">
              <Wrench className="w-5 h-5 text-neon-orange" />
            </div>
            <div>
              <h2 className="text-sm font-heading font-bold text-text uppercase tracking-wider">Mark PC for Maintenance</h2>
              <p className="text-[10px] text-text-3 mt-1">PC: <span className="font-mono font-bold text-neon-blue">{pcNumber}</span></p>
            </div>
          </div>
          <button
            onClick={handleCancel}
            disabled={isLoading}
            className="text-text-3 hover:text-text transition-colors disabled:opacity-50"
          >
            <X className="w-5 h-5" />
          </button>
        </div>

        {/* Content */}
        <div className="p-6 space-y-4">
          <div>
            <label className="block text-[10px] font-mono font-semibold text-text-2 uppercase tracking-wider mb-3">
              Reason for Maintenance <span className="text-neon-red">*</span>
            </label>
            <textarea
              value={reason}
              onChange={(e) => setReason(e.target.value)}
              placeholder="e.g., Monitor not working, Keyboard malfunction, Power issue, Display flickering..."
              className="w-full px-4 py-3 bg-bg-3 border border-border rounded-lg text-sm text-text placeholder-text-3 focus:border-neon-orange focus:outline-none transition-colors resize-none"
              rows="5"
              disabled={isLoading}
              autoFocus
            />
            <p className="text-[9px] text-text-3 mt-2">Provide a clear description of the issue for technician reference.</p>
          </div>
        </div>

        {/* Footer */}
        <div className="bg-bg-3 border-t border-border px-6 py-4 flex gap-3 justify-end">
          <button
            onClick={handleCancel}
            disabled={isLoading}
            className="px-5 py-2 text-sm font-bold text-text-2 bg-bg-2 border border-border rounded-lg hover:bg-border/30 transition-colors disabled:opacity-50 uppercase tracking-wider"
          >
            Cancel
          </button>
          <button
            onClick={handleConfirm}
            disabled={isLoading || !reason.trim()}
            className="px-5 py-2 text-sm font-bold text-white bg-neon-orange rounded-lg hover:bg-neon-orange/90 transition-colors disabled:opacity-50 flex items-center gap-2 uppercase tracking-wider flex-shrink-0"
          >
            {isLoading && (
              <span className="inline-block w-4 h-4 border-2 border-white border-t-transparent rounded-full animate-spin" />
            )}
            {isLoading ? 'Marking...' : 'Mark for Maintenance'}
          </button>
        </div>
      </div>
    </div>
  );
}
