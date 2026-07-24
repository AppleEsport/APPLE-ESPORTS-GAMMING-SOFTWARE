import { useState, useEffect, useCallback } from 'react';
import { useNavigate } from 'react-router-dom';
import { Lock, AlertTriangle, Calculator, LogOut, ArrowLeft } from 'lucide-react';
import { useAuth } from '../../contexts/AuthContext';
import { useBranch } from '../../contexts/BranchContext';
import api from '../../config/api';
import PageHeader from '../../components/layout/PageHeader';
import DenominationCounter from '../../components/cash/DenominationCounter';
import { generateIdempotencyKey } from '../../utils/idempotency';

export default function CashDeskPage() {
  const navigate = useNavigate();
  const { isSuperAdmin, user, logout } = useAuth();
  const { activeBranch, switchBranch } = useBranch();

  const [register, setRegister] = useState(null);
  const [isLoading, setIsLoading] = useState(true);
  const [error, setError] = useState(null);

  const [isLocking, setIsLocking] = useState(false);
  const [isClosing, setIsClosing] = useState(false);
  const [isCancelling, setIsCancelling] = useState(false);

  const targetBranchId = isSuperAdmin ? activeBranch?.id : user?.branchId;

  const fetchActiveRegister = useCallback(async () => {
    if (isSuperAdmin && !targetBranchId) {
      setRegister(null);
      setIsLoading(false);
      return;
    }

    try {
      setError(null);
      const { data } = await api.get('/cash/active', { params: { branchId: targetBranchId } });
      setRegister(data.data);
    } catch (err) {
      if (err.response?.status === 404) {
        setRegister(null);
      } else {
        setError(err.response?.data?.error || err.response?.data?.message || 'Failed to fetch cash register');
      }
    } finally {
      setIsLoading(false);
    }
  }, [targetBranchId, isSuperAdmin]);

  useEffect(() => {
    setIsLoading(true);
    fetchActiveRegister();
  }, [fetchActiveRegister]);

  const handleStartVerification = async () => {
    setIsLocking(true);
    try {
      await api.post('/cash-desk/verify-start', {}, {
        headers: { 'X-Idempotency-Key': generateIdempotencyKey() }
      });
      await fetchActiveRegister();
    } catch (err) {
      setError(err.response?.data?.error || err.response?.data?.message || 'Failed to lock register for verification.');
    } finally {
      setIsLocking(false);
    }
  };

  const handleCloseShift = async () => {
    setIsClosing(true);
    try {
      await api.post(`/cash-desk/close/${register.id}`, {}, {
        headers: { 'X-Idempotency-Key': generateIdempotencyKey() }
      });

      if (isSuperAdmin) {
        // Super Admin/Admin have no personal shift to end — just return to the
        // All Branches view instead of logging out.
        switchBranch(null);
        navigate('/app/dashboard');
      } else {
        // Operator's shift is over — log them out.
        logout();
      }
    } catch (err) {
      setError(err.response?.data?.error || err.response?.data?.message || 'Failed to close shift.');
      setIsClosing(false);
    }
  };

  const handleBackButton = async () => {
    if (register?.status !== 'Verifying') {
      navigate(-1);
      return;
    }

    setIsCancelling(true);
    try {
      // Undo the lock — register goes back to Open, shift is untouched.
      await api.post(`/cash-desk/cancel-verification/${register.id}`, {}, {
        headers: { 'X-Idempotency-Key': generateIdempotencyKey() }
      });
      navigate(-1);
    } catch (err) {
      setError(err.response?.data?.error || err.response?.data?.message || 'Failed to cancel verification.');
      setIsCancelling(false);
    }
  };

  if (isSuperAdmin && !activeBranch) {
    return (
      <div className="flex flex-col items-center justify-center min-h-[60vh] text-center">
        <Lock className="w-12 h-12 text-text-3 mb-4" />
        <h2 className="text-xl font-heading font-bold text-text mb-2">Select a Branch</h2>
        <p className="text-text-2">You must select a branch to access the Cash Register.</p>
      </div>
    );
  }

  if (isLoading) {
    return (
      <div className="flex justify-center items-center min-h-[60vh]">
        <div className="w-8 h-8 rounded-full border-2 border-accent border-t-transparent animate-spin" />
      </div>
    );
  }

  // 1. If no active register, nothing to close.
  if (!register) {
    return (
      <div className="flex flex-col items-center justify-center min-h-[60vh] text-center">
        <AlertTriangle className="w-12 h-12 text-neon-orange mb-4" />
        <h2 className="text-xl font-heading font-bold text-text mb-2">No Active Shift</h2>
        <p className="text-text-2">There is no active cash register open for this shift.</p>
      </div>
    );
  }

  return (
    <div className="h-full flex flex-col max-w-4xl mx-auto">
      <div className="mb-6 flex items-center gap-4">
        {register.status !== 'Verified' && (
          <button
            onClick={handleBackButton}
            disabled={isCancelling}
            className="p-2 hover:bg-bg-3 rounded-lg transition-colors text-text-3 hover:text-text shrink-0"
            title={register.status === 'Verifying' ? 'Cancel verification and go back' : 'Go back'}
          >
            <ArrowLeft className="w-5 h-5" />
          </button>
        )}
        <div className="flex-1">
          <PageHeader
            title="Cash Register"
            subtitle="End of Shift Reconciliation"
            icon="M12 15v2m-6 4h12a2 2 0 002-2v-6a2 2 0 00-2-2H6a2 2 0 00-2 2v6a2 2 0 002 2zm10-10V7a4 4 0 00-8 0v4h8z"
            badge="SECURE"
          />
        </div>
      </div>

      {error && (
        <div className="bg-neon-red/10 border border-neon-red/30 text-neon-red p-3 rounded-xl mb-4 flex items-center gap-2 text-sm">
          <AlertTriangle className="w-4 h-4" /> {error}
        </div>
      )}

      {/* STEP 1: Lock Register */}
      {register.status === 'Open' && (
        <div className="flex-1 flex flex-col items-center justify-center border border-border bg-bg-2 rounded-xl p-8 text-center">
          <div className="w-20 h-20 bg-neon-orange/10 text-neon-orange rounded-full flex items-center justify-center mb-6">
            <Lock className="w-10 h-10" />
          </div>
          <h2 className="text-2xl font-heading font-bold uppercase tracking-widest text-text mb-3">
            Initiate Snapshot Lock
          </h2>
          <p className="text-text-2 text-sm max-w-md mb-8 leading-relaxed">
            Starting verification will lock the active Cash Register. New payments, inward cash, and adjustments will be temporarily blocked to prevent reconciliation drift.
          </p>
          <button
            onClick={handleStartVerification}
            disabled={isLocking}
            className="btn-primary w-full max-w-sm py-4 shadow-lg shadow-accent/20 flex justify-center items-center"
          >
            {isLocking ? (
              <div className="w-5 h-5 rounded-full border-2 border-current border-t-transparent animate-spin" />
            ) : (
              'Lock Register & Start Count'
            )}
          </button>
        </div>
      )}

      {/* STEP 2: Denomination Verification */}
      {register.status === 'Verifying' && (
        <div className="flex-1 flex flex-col min-h-0">
          <div className="bg-neon-orange/10 border border-neon-orange/30 text-neon-orange p-3 rounded-xl mb-4 flex items-center gap-2 text-xs font-bold uppercase tracking-wider">
            <Lock className="w-4 h-4 shrink-0" /> Snapshot Locked. Register is immune to external cash flow changes.
          </div>
          <DenominationCounter 
            expectedTotal={register.expectedDrawerCash} 
            onVerified={fetchActiveRegister} 
          />
        </div>
      )}

      {/* STEP 3: Finalize Shift */}
      {register.status === 'Verified' && (
        <div className="flex-1 flex flex-col items-center justify-center border border-accent/30 bg-bg-2 rounded-xl p-8 text-center shadow-[0_0_30px_rgba(255,51,102,0.1)]">
          <div className="w-20 h-20 bg-accent/10 text-accent rounded-full flex items-center justify-center mb-6">
            <Calculator className="w-10 h-10" />
          </div>
          <h2 className="text-2xl font-heading font-bold uppercase tracking-widest text-text mb-3">
            Verification Complete
          </h2>
          <div className="bg-bg-3 border border-border rounded-xl p-6 w-full max-w-md mb-8 text-left">
            <div className="flex justify-between items-center mb-3">
              <span className="text-text-3 text-xs font-bold uppercase tracking-wider">Expected Drawer</span>
              <span className="text-text font-mono font-bold">₹{register.expectedDrawerCash}</span>
            </div>
            <div className="flex justify-between items-center mb-3">
              <span className="text-text-3 text-xs font-bold uppercase tracking-wider">Counted Total</span>
              <span className="text-text font-mono font-bold">₹{register.physicalCashCounted}</span>
            </div>
            <div className="flex justify-between items-center border-t border-border pt-3">
              <span className="text-text-3 text-xs font-bold uppercase tracking-wider">Discrepancy</span>
              <span className={`font-mono font-bold ${register.cashDifference === 0 ? 'text-neon-blue' : 'text-neon-red'}`}>
                ₹{register.cashDifference}
              </span>
            </div>
          </div>
          <button
            onClick={handleCloseShift}
            disabled={isClosing}
            className="w-full max-w-md py-4 rounded-xl font-bold uppercase tracking-widest text-sm transition-all bg-accent hover:bg-accent-hover text-white shadow-lg shadow-accent/20 flex justify-center items-center gap-2"
          >
            {isClosing ? (
              <div className="w-5 h-5 rounded-full border-2 border-white border-t-transparent animate-spin" />
            ) : (
              <><LogOut className="w-5 h-5" /> Finalize & Close Shift</>
            )}
          </button>
        </div>
      )}
    </div>
  );
}
