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

  /**
   * Whether the log should follow new activity down the page.
   *
   * True while the reader is at the bottom, which is nearly always - a log that pins itself to
   * the newest line is the whole point of this strip. It goes false the moment they scroll up,
   * because at that point they are reading something and following along would take it away
   * from them.
   *
   * Tracked as the reader scrolls rather than worked out when a new entry arrives, and it has to
   * be: the effect below runs after React has already painted the new line, so by then
   * scrollHeight has grown and there is no way left to tell where the reader was standing
   * beforehand. This ref is that memory.
   */
  const followNewEntries = useRef(true);

  /**
   * A few pixels of tolerance, because "at the bottom" is rarely exact. Fractional scroll
   * positions from display scaling, a partially visible last row, and the browser's own rounding
   * all mean scrollTop + clientHeight lands just short of scrollHeight while looking flush to
   * the eye. Demanding equality would read as "scrolled up" at the bottom of the list and the
   * log would stop following for no visible reason.
   */
  const AT_BOTTOM_TOLERANCE_PX = 32;

  const handleScroll = useCallback(() => {
    const el = scrollRef.current;
    if (!el) return;
    followNewEntries.current =
      el.scrollTop + el.clientHeight >= el.scrollHeight - AT_BOTTOM_TOLERANCE_PX;
  }, []);

  // Follow the newest line, unless the reader has deliberately scrolled up.
  //
  // This used to jump to the bottom on every new entry, unconditionally. Scrolling up to read an
  // earlier line worked for as long as the shop was quiet and was snatched away the instant
  // anything happened - which on a busy evening is a few seconds. It reads as "scrolling is
  // broken", and reporting it that way was correct: scrolling worked, it was just being undone.
  useEffect(() => {
    if (!followNewEntries.current) return;
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

      <div ref={scrollRef} onScroll={handleScroll} className="flex-1 overflow-y-auto px-3 py-1.5 font-mono text-[11px] leading-5">
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
