import { memo, useState, useEffect, useId } from 'react';
import { Infinity as InfinityIcon } from 'lucide-react';
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

// ── Tile size steps — driven by the zoom control on SessionsPage ──
export const TILE_SIZES = ['sm', 'md', 'lg', 'xl'];
const SIZE_STYLES = {
  sm: { glyph: 'w-10 h-10', infinity: 'w-4 h-4', name: 'text-xs',   status: 'text-[9px]',  dot: 'w-1.5 h-1.5', gap: 'gap-1',   py: 'py-3'  },
  md: { glyph: 'w-16 h-16', infinity: 'w-6 h-6', name: 'text-base', status: 'text-[10px]', dot: 'w-2 h-2',     gap: 'gap-2',   py: 'py-5'  },
  lg: { glyph: 'w-24 h-24', infinity: 'w-9 h-9', name: 'text-xl',   status: 'text-xs',     dot: 'w-2.5 h-2.5', gap: 'gap-2.5', py: 'py-7'  },
  xl: { glyph: 'w-32 h-32', infinity: 'w-12 h-12', name: 'text-2xl', status: 'text-sm',    dot: 'w-3 h-3',     gap: 'gap-3',   py: 'py-9'  },
};

// ── Glossy monitor glyph: bezel + screen + diagonal light sheen + stand,
// colored via the wrapping text-color class so it tracks PC status ──
function MonitorGlyph({ className = '', sizeClass = 'w-16 h-16', glow = false, children }) {
  const clipId = useId();
  return (
    <svg
      viewBox="0 0 64 56"
      className={`${sizeClass} ${className} ${glow ? 'drop-shadow-[0_0_8px_currentColor]' : ''}`}
      fill="none"
    >
      <rect x="3" y="3" width="58" height="38" rx="7" fill="currentColor" opacity="0.16" />
      <rect x="7" y="7" width="50" height="30" rx="4" fill="currentColor" opacity="0.85" />
      <clipPath id={clipId}>
        <rect x="7" y="7" width="50" height="30" rx="4" />
      </clipPath>
      <polygon points="7,37 36,7 57,7 28,37" fill="#fff" opacity="0.14" clipPath={`url(#${clipId})`} />
      <rect x="27" y="41" width="10" height="7" fill="currentColor" opacity="0.55" />
      <rect x="17" y="49" width="30" height="4" rx="2" fill="currentColor" opacity="0.55" />
      {children}
    </svg>
  );
}

function fmtDuration(ms) {
  const totalSec = Math.max(0, Math.floor(ms / 1000));
  const h = Math.floor(totalSec / 3600);
  const m = Math.floor((totalSec % 3600) / 60);
  const s = totalSec % 60;
  if (h > 0) return `${h}h ${m}m`;
  if (m > 0) return `${m}m ${s}s`;
  return `${s}s`;
}

const PcTile = memo(({ pc, walkinReq, isSelected, onSelect, onQuickStart, onRefresh, size = 'md' }) => {
  const toast = useToast();
  const [isDragOver, setIsDragOver] = useState(false);
  const [isTransferring, setIsTransferring] = useState(false);
  const [now, setNow] = useState(Date.now());
  const sizeStyle = SIZE_STYLES[size] || SIZE_STYLES.md;

  const hasReservation = pc.nextReservationTime && new Date(pc.nextReservationTime) > new Date();
  const style = walkinReq ? PENDING_STYLE : (hasReservation ? STATUS_STYLES.Reserved : (STATUS_STYLES[pc.state] || DEFAULT_STYLE));
  const isIdle = pc.state === 'Idle' && !walkinReq && !hasReservation;
  const isActive = pc.state === 'Active';
  const isPayAsYouGo = isActive && !pc.sessionEndTime;
  const hasPlanTime = isActive && !!pc.sessionEndTime;

  useEffect(() => {
    if (!isActive) return;
    const id = setInterval(() => setNow(Date.now()), 1000);
    return () => clearInterval(id);
  }, [isActive]);

  const timerLabel = hasPlanTime
    ? fmtDuration(new Date(pc.sessionEndTime).getTime() - now)
    : isPayAsYouGo && pc.sessionStartTime
      ? fmtDuration(now - new Date(pc.sessionStartTime).getTime())
      : null;

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
      title={walkinReq ? `${pc.name}: Walk-in pending` : hasReservation ? `${pc.name}: RESERVED at ${new Date(pc.nextReservationTime).toLocaleTimeString('en-IN', { hour: '2-digit', minute: '2-digit' })}` : isPayAsYouGo ? `${pc.name}: ${style.label} (Pay-As-You-Go, ${timerLabel} elapsed)` : hasPlanTime ? `${pc.name}: ${style.label} (${timerLabel} left)` : `${pc.name}: ${style.label}`}
      className={`group relative flex flex-col items-center justify-center ${sizeStyle.gap} rounded-xl ${sizeStyle.py} px-2 select-none transition-all
        ${isSelected ? 'brightness-150 scale-105' : 'hover:brightness-125 hover:scale-105'}
        ${isDragOver ? 'bg-pc-active/5' : ''}
        ${isActive ? 'cursor-grab active:cursor-grabbing' : 'cursor-pointer'}
      `}
    >
      {isTransferring && (
        <div className="absolute inset-0 z-10 flex items-center justify-center bg-bg/80 backdrop-blur-sm rounded-xl">
          <span className="w-4 h-4 border-2 border-pc-active border-t-transparent rounded-full animate-spin" />
        </div>
      )}

      <div className={`relative flex items-center justify-center ${(walkinReq || pc.state === 'AwaitingBilling') ? 'animate-pulse' : ''}`}>
        <MonitorGlyph className={style.icon} sizeClass={sizeStyle.glyph} glow={isActive || isSelected}>
          {isPayAsYouGo && (
            <foreignObject x="16" y="12" width="32" height="24">
              <div className="flex items-center justify-center w-full h-full">
                <InfinityIcon className={`${sizeStyle.infinity} text-neon-purple`} strokeWidth={2.5} />
              </div>
            </foreignObject>
          )}
        </MonitorGlyph>
      </div>

      <span className={`font-heading font-bold text-text ${sizeStyle.name} tracking-wider truncate max-w-full`}>{pc.name}</span>
      <span className={`flex items-center gap-1.5 ${sizeStyle.status} font-mono font-bold uppercase tracking-widest ${style.icon}`}>
        <span className={`${sizeStyle.dot} rounded-full ${style.dot}`} />
        {timerLabel || style.label}
      </span>
    </motion.button>
  );
});

PcTile.displayName = 'PcTile';
export default PcTile;
