import { useEffect, useRef, useState, useCallback } from 'react';
import { ScrollText, GripHorizontal, Loader2 } from 'lucide-react';
import { SESSION_LOG_EVENT } from '../../utils/sessionLog';
import { useActivityLog } from '../../contexts/ActivityLogContext';
import { getRecentActivities } from '../../api/sessions.api';

const MAX_ENTRIES = 100;
const MIN_HEIGHT = 90;
const MAX_HEIGHT = 400;

const TYPE_COLORS = {
  success: 'text-pc-active',
  error: 'text-neon-red',
  warn: 'text-neon-orange',
  info: 'text-text-2',
};

function fmtTime(ts) {
  return new Date(ts).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit', second: '2-digit', hour12: false });
}

// ── Fixed-to-viewport activity log strip (Pancafe-style terminal log), pinned
// to the bottom of the screen with a drag handle to resize its height ──
export default function SessionActivityLog({ height, onHeightChange }) {
  const { entries, addEntry, addEntries } = useActivityLog();
  const scrollRef = useRef(null);
  const resizing = useRef(false);
  const [loading, setLoading] = useState(true);

  // Load historical activities on mount
  useEffect(() => {
    const loadHistoricalActivities = async () => {
      try {
        const recentActivities = await getRecentActivities(100);
        if (recentActivities.length > 0) {
          addEntries(recentActivities);
        }
      } catch (err) {
        console.error('Failed to load historical activities:', err);
      } finally {
        setLoading(false);
      }
    };
    loadHistoricalActivities();
  }, [addEntries]);

  // Listen for new events
  useEffect(() => {
    const handler = (e) => {
      addEntry(e.detail);
    };
    window.addEventListener(SESSION_LOG_EVENT, handler);
    return () => window.removeEventListener(SESSION_LOG_EVENT, handler);
  }, [addEntry]);

  useEffect(() => {
    if (scrollRef.current) {
      scrollRef.current.scrollTop = scrollRef.current.scrollHeight;
    }
  }, [entries]);

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

      <div className="px-3 py-1 border-b border-border bg-bg-3 flex items-center gap-1.5 text-[10px] font-mono font-bold uppercase tracking-widest text-text-3 flex-shrink-0">
        <ScrollText className="w-3 h-3" />
        Activity Log
      </div>

      <div ref={scrollRef} className="flex-1 overflow-y-auto px-3 py-1.5 font-mono text-[11px] leading-5">
        {loading ? (
          <div className="flex items-center gap-2 text-text-3">
            <Loader2 className="w-3 h-3 animate-spin" />
            <span className="italic">Loading activity log...</span>
          </div>
        ) : entries.length === 0 ? (
          <div className="text-text-3 italic">No activity yet.</div>
        ) : (
          entries.map((entry, i) => (
            <div key={entry.id || i} className={TYPE_COLORS[entry.type] || TYPE_COLORS.info}>
              <span className="text-text-3">{entry.timestamp ? fmtTime(entry.timestamp) : fmtTime(entry.createdAt || new Date().toISOString())}-&gt;</span>
              {entry.message || entry.description}
              {entry.amount && <span className="text-neon-orange ml-1">(₹{entry.amount})</span>}
            </div>
          ))
        )}
      </div>
    </div>
  );
}
