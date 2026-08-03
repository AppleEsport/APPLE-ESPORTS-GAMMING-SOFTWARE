import { memo, useState } from 'react';
import { Monitor } from 'lucide-react';
import { motion } from 'framer-motion';
import api from '../../config/api';
import { useToast } from '../ui/Toast';

// ── Pancafe-style status colors: icon + name + status dot only, no inline
// timers/charges/buttons — click a tile to load its detail panel ──
const STATUS_STYLES = {
  Idle:            { icon: 'text-pc-idle',     dot: 'bg-pc-idle',     border: 'border-pc-idle/40',     label: 'FREE' },
  Active:          { icon: 'text-pc-active',   dot: 'bg-pc-active',   border: 'border-pc-active/50',   label: 'OCCUPIED' },
  Reserved:        { icon: 'text-pc-reserved', dot: 'bg-pc-reserved', border: 'border-pc-reserved/50',  label: 'RESERVED' },
  AwaitingBilling: { icon: 'text-neon-orange', dot: 'bg-neon-orange', border: 'border-neon-orange/50',  label: 'BILLING' },
  UnderMaintenance:{ icon: 'text-pc-offline',  dot: 'bg-pc-offline',  border: 'border-pc-offline/40',   label: 'MAINT' },
  Expired:         { icon: 'text-text-3',      dot: 'bg-text-3',      border: 'border-border',          label: 'EXPIRED' },
};
const DEFAULT_STYLE = { icon: 'text-text-3', dot: 'bg-pc-offline', border: 'border-pc-offline/40', label: 'OFFLINE' };
const PENDING_STYLE = { icon: 'text-accent', dot: 'bg-accent', border: 'border-accent/50', label: 'PENDING' };

const PcTile = memo(({ pc, walkinReq, isSelected, onSelect, onQuickStart, onRefresh }) => {
  const toast = useToast();
  const [isDragOver, setIsDragOver] = useState(false);
  const [isTransferring, setIsTransferring] = useState(false);

  const style = walkinReq ? PENDING_STYLE : (STATUS_STYLES[pc.state] || DEFAULT_STYLE);
  const isIdle = pc.state === 'Idle' && !walkinReq;
  const isActive = pc.state === 'Active';

  const handleDoubleClick = () => {
    if (isIdle) onQuickStart?.(pc);
  };

  // Drag idle tiles accept a dropped active session for transfer
  const handleDragOver = (e) => {
    if (!isIdle) return;
    e.preventDefault();
    e.dataTransfer.dropEffect = 'move';
  };
  const handleDragEnter = (e) => {
    if (!isIdle) return;
    e.preventDefault();
    setIsDragOver(true);
  };
  const handleDragLeave = (e) => {
    e.preventDefault();
    setIsDragOver(false);
  };
  const handleDrop = async (e) => {
    if (!isIdle) return;
    e.preventDefault();
    setIsDragOver(false);
    try {
      const data = JSON.parse(e.dataTransfer.getData('text/plain'));
      if (!data.sessionId || data.sourcePcId === pc.id) return;
      setIsTransferring(true);
      await api.post(`/sessions/${data.sessionId}/transfer`, { targetPcId: pc.id });
      toast.success(`Session transferred to ${pc.name}!`);
      onRefresh?.();
    } catch (err) {
      toast.error(err.response?.data?.error || err.response?.data?.message || 'Failed to transfer session');
    } finally {
      setIsTransferring(false);
    }
  };

  return (
    <motion.button
      type="button"
      initial={{ opacity: 0, scale: 0.95 }}
      animate={{ opacity: 1, scale: 1 }}
      onClick={() => onSelect?.(pc)}
      onDoubleClick={handleDoubleClick}
      draggable={isActive}
      onDragStart={isActive ? (e) => {
        e.dataTransfer.setData('text/plain', JSON.stringify({ sessionId: pc.activeSessionId, sourcePcId: pc.id }));
        e.dataTransfer.effectAllowed = 'move';
      } : undefined}
      onDragOver={handleDragOver}
      onDragEnter={handleDragEnter}
      onDragLeave={handleDragLeave}
      onDrop={handleDrop}
      title={walkinReq ? `${pc.name}: Walk-in pending` : `${pc.name}: ${style.label}`}
      className={`relative flex flex-col items-center justify-center gap-1.5 rounded-lg border bg-bg-2 py-3 px-1.5 select-none transition-colors
        ${isSelected ? 'border-accent ring-2 ring-accent/40' : `${style.border} hover:brightness-125`}
        ${isDragOver ? 'border-pc-active bg-pc-active/5' : ''}
        ${isActive ? 'cursor-grab active:cursor-grabbing' : 'cursor-pointer'}
      `}
    >
      {isTransferring && (
        <div className="absolute inset-0 z-10 flex items-center justify-center bg-bg/80 backdrop-blur-sm rounded-lg">
          <span className="w-4 h-4 border-2 border-pc-active border-t-transparent rounded-full animate-spin" />
        </div>
      )}

      <div className={`flex items-center justify-center w-10 h-10 rounded-lg bg-bg-3 border border-border ${(walkinReq || pc.state === 'AwaitingBilling') ? 'animate-pulse' : ''}`}>
        <Monitor className={`w-5 h-5 ${style.icon}`} strokeWidth={1.75} />
      </div>

      <span className="font-heading font-bold text-text text-[11px] tracking-wider truncate max-w-full">{pc.name}</span>
      <span className={`flex items-center gap-1 text-[8px] font-mono font-bold uppercase tracking-widest ${style.icon}`}>
        <span className={`w-1.5 h-1.5 rounded-full ${style.dot}`} />
        {style.label}
      </span>
    </motion.button>
  );
});

PcTile.displayName = 'PcTile';
export default PcTile;
