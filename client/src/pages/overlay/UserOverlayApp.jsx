import React, { useState, useEffect, useRef } from 'react';
import { useParams, Routes, Route, Navigate, useNavigate } from 'react-router-dom';
import { OverlaySocketProvider } from '../../contexts/OverlaySocketContext';
import OverlayNavBar from './components/OverlayNavBar';
import SessionInfoScreen from './screens/SessionInfoScreen';
import FoodOrderScreen from './screens/FoodOrderScreen';
import TimeExtensionScreen from './screens/TimeExtensionScreen';
import CallOperatorScreen from './screens/CallOperatorScreen';
import CurrentBillScreen from './screens/CurrentBillScreen';
import OverlayMemberLoginScreen from './screens/OverlayMemberLoginScreen';
import PcLockScreen from './components/PcLockScreen';
import { Minimize2, Maximize2 } from 'lucide-react';
import { useOverlaySocket } from '../../contexts/OverlaySocketContext';
import WalletApprovalModal from './components/WalletApprovalModal';

// Tells the native shell hosting this page (desktop-client's MainForm) how much of the
// screen it should actually cover right now - see MainForm.cs's ApplyOverlayLayout. Only
// meaningful inside that WebView2 host; a plain browser tab (support, testing, mock mode)
// has no such bridge, and this must never throw there.
function postToHost(payload) {
  try { window.chrome?.webview?.postMessage(JSON.stringify(payload)); } catch { /* not hosted */ }
}

// Drag-to-move for the floating widget (bubble or expanded panel). The native window is what
// actually moves - see MainForm.cs's DragFloatingWindow - because WebView2's own content sits
// in a separate child window that a plain WM_NCHITTEST/HTCAPTION trick played by the host Form
// never sees, so the move has to be driven from in here instead.
function useDragToMoveHost() {
  const drag = useRef({ active: false, moved: false, x: 0, y: 0 });

  const onPointerDown = (e) => {
    drag.current = { active: true, moved: false, x: e.clientX, y: e.clientY };
    e.currentTarget.setPointerCapture?.(e.pointerId);
  };
  const onPointerMove = (e) => {
    const st = drag.current;
    if (!st.active) return;
    const dx = e.clientX - st.x;
    const dy = e.clientY - st.y;
    if (dx === 0 && dy === 0) return;
    if (Math.abs(dx) > 3 || Math.abs(dy) > 3) st.moved = true;
    st.x = e.clientX;
    st.y = e.clientY;
    postToHost({ type: 'overlay-drag', dx, dy });
  };
  const onPointerUp = () => { drag.current.active = false; };

  return { onPointerDown, onPointerMove, onPointerUp, wasDragged: () => drag.current.moved };
}

// Extracted inner content to consume socket context
function OverlayContent({ isMinimized, setIsMinimized }) {
  const { sessionData, sessionLoading } = useOverlaySocket();
  const bubbleDrag = useDragToMoveHost();
  const panelDrag = useDragToMoveHost();

  // Show full-screen lock screen if there's no active session or time has run out
  const isTimeUp = sessionData && sessionData.remainingTime !== null && sessionData.remainingTime <= 0;
  const isSessionEnded = sessionData && sessionData.sessionStatus !== 'active';
  const showLockScreen = !sessionLoading && (!sessionData || isTimeUp || isSessionEnded);

  // Every fresh session starts minimised, so a customer gets the whole screen to actually
  // play rather than the widget defaulting open over a chunk of it - they can still tap it to
  // check their time or order food. Keyed on sessionId so it fires exactly once per session
  // rather than re-minimising every time a silent refetch touches sessionData.
  const lastMinimizedSessionId = useRef(null);
  useEffect(() => {
    if (sessionData?.sessionStatus === 'active' && sessionData.sessionId !== lastMinimizedSessionId.current) {
      lastMinimizedSessionId.current = sessionData.sessionId;
      setIsMinimized(true);
    }
  }, [sessionData?.sessionId, sessionData?.sessionStatus, setIsMinimized]);

  // Tells MainForm how big a window to actually be: full screen for the walk-in/member gate
  // and the locked "session ended" screen (both render themselves fixed inset-0 and need the
  // window to actually be the screen for that to mean anything), otherwise just enough to
  // cover the small floating widget - see the IsUserPc branch of MainForm's constructor.
  const layoutMode = showLockScreen ? 'full' : isMinimized ? 'bubble' : 'panel';
  useEffect(() => { postToHost({ type: 'overlay-layout', mode: layoutMode }); }, [layoutMode]);

  if (showLockScreen) {
    return (
      <>
        <PcLockScreen />
        <WalletApprovalModal />
      </>
    );
  }

  // When minimized, we only show a tiny floating widget that can be expanded - or dragged
  // anywhere on screen. A tap (pointer down+up with no real movement) expands it; anything
  // that actually moves is a drag instead, so the two can share one small target.
  if (isMinimized) {
    return (
      <>
        <div className="fixed inset-0 z-50 flex items-center justify-center">
          <div
            onPointerDown={bubbleDrag.onPointerDown}
            onPointerMove={bubbleDrag.onPointerMove}
            onPointerUp={(e) => {
              bubbleDrag.onPointerUp(e);
              if (!bubbleDrag.wasDragged()) setIsMinimized(false);
            }}
            className="w-14 h-14 bg-bg-2/90 backdrop-blur-xl border border-accent/50 rounded-full flex items-center justify-center text-accent shadow-[0_0_15px_rgba(220,38,38,0.3)] hover:bg-accent/20 transition-all cursor-move touch-none select-none"
          >
            <Maximize2 className="w-5 h-5 pointer-events-none" />
          </div>
        </div>
        <WalletApprovalModal />
      </>
    );
  }

  // Expanded quick-panel - a small movable floating widget now, not a permanently docked
  // right-hand strip. Drag the header to move it; the native window resizes to fit either
  // state and keeps wherever the customer last left it (see MainForm.cs's ApplyOverlayLayout).
  return (
    <>
      <div className="fixed inset-0 flex flex-col bg-bg-2/95 backdrop-blur-xl border border-border/60 rounded-2xl shadow-2xl shadow-black/80 z-50 overflow-hidden font-body text-text">

      {/* Header Strip - drag handle */}
      <div
        onPointerDown={panelDrag.onPointerDown}
        onPointerMove={panelDrag.onPointerMove}
        onPointerUp={panelDrag.onPointerUp}
        className="h-14 bg-black/40 border-b border-border/50 flex items-center justify-between px-5 shrink-0 cursor-move touch-none select-none"
      >
        <div className="flex items-center gap-3 pointer-events-none">
          <div className="w-2.5 h-2.5 rounded-full bg-neon-green animate-pulse shadow-[0_0_8px_#22d3a6]" />
          <span className="font-heading font-bold tracking-widest uppercase text-accent">Apple Esports</span>
        </div>
        <button
          onClick={() => setIsMinimized(true)}
          className="text-text-3 hover:text-accent transition-colors p-1.5 hover:bg-accent/10 rounded-md"
          title="Minimize Overlay"
        >
          <Minimize2 className="w-5 h-5" />
        </button>
      </div>

      {/* Content Area */}
      <div className="flex-1 overflow-y-auto relative bg-bg/50 scrollbar-thin">
        <Routes>
          <Route path="/" element={<SessionInfoScreen />} />
          <Route path="/login" element={<OverlayMemberLoginScreen />} />
          <Route path="/food" element={<FoodOrderScreen />} />
          <Route path="/extend" element={<TimeExtensionScreen />} />
          <Route path="/call" element={<CallOperatorScreen />} />
          <Route path="/bill" element={<CurrentBillScreen />} />
          <Route path="*" element={<Navigate to="" replace />} />
        </Routes>
      </div>

      {/* Navigation Bar */}
      <div className="shrink-0 h-16 border-t border-border/50 bg-bg-3/80">
        <OverlayNavBar />
      </div>
    </div>
    <WalletApprovalModal />
    </>
  );
}

export default function UserOverlayApp() {
  const { pcId } = useParams();
  const [isMinimized, setIsMinimized] = useState(true);

  return (
    <OverlaySocketProvider pcId={pcId} isMinimized={isMinimized}>
      <OverlayContent isMinimized={isMinimized} setIsMinimized={setIsMinimized} />
    </OverlaySocketProvider>
  );
}
