import { useState, useEffect, useCallback } from 'react';
import { useNavigate, useSearchParams } from 'react-router-dom';
import { motion, AnimatePresence } from 'framer-motion';
import { Shield, User, MapPin, Loader2, KeyRound, WifiOff, Eye, EyeOff, ChevronDown } from 'lucide-react';
import { useAuth } from '../../contexts/AuthContext';
import { sessionBelongsOnPortal } from '../../config/portalAccess';
import api from '../../config/api';

export default function LoginPage() {
  const { loginAdmin, loginOperator, isAuthenticated, isSuperAdmin, user, clearSession } = useAuth();
  const navigate = useNavigate();
  const [searchParams] = useSearchParams();
  const reason = searchParams.get('reason');

  const [activeTab, setActiveTab] = useState('operator');
  const [branches, setBranches] = useState([]);
  const [loadingBranches, setLoadingBranches] = useState(true);
  
  // Set when the branch system itself cannot be reached. This is a fault to report, not a
  // mode to log in through: the dashboard is served BY the branch server, so if it is
  // unreachable there is nothing behind this screen to let anyone into.
  const [branchUnreachable, setBranchUnreachable] = useState(false);

  // Form State
  const [email, setEmail] = useState('');
  const [username, setUsername] = useState('');
  const [password, setPassword] = useState('');
  const [selectedBranch, setSelectedBranch] = useState('');
  const [error, setError] = useState('');
  const [isLoading, setIsLoading] = useState(false);
  const [showPassword, setShowPassword] = useState(false);

  // A session already in place is only honoured if it belongs on THIS portal. Anything else
  // is signed out, so the person choosing "Operator" gets an operator login rather than
  // inheriting whoever used this PC last — which on a shared counter machine could hand a
  // customer a Super Admin dashboard without them typing a password.
  useEffect(() => {
    if (!isAuthenticated) return;

    if (sessionBelongsOnPortal(user, 'operator')) {
      navigate('/app/sessions', { replace: true });
    } else {
      clearSession();
    }
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [isAuthenticated, user]);

  // Fetch active branches for Operator dropdown
  const fetchBranches = useCallback(async () => {
    try {
      setLoadingBranches(true);
      setBranchUnreachable(false);
      setSelectedBranch(''); // Reset branch selection on mount
      setUsername(''); // Reset username
      setPassword(''); // Reset password
      const res = await api.get('/auth/branches');
      setBranches(res.data?.data || []);
    } catch (err) {
      // No response at all means the branch server is not answering - it is stopped, or
      // this PC cannot reach it over the shop's network. Either way it is the same fault,
      // and the operator needs to be told which, not shown a way around it.
      if (err.message === 'Network Error' || !err.response) {
        setBranchUnreachable(true);
      }
    } finally {
      setLoadingBranches(false);
    }
  }, []);

  useEffect(() => { fetchBranches(); }, [fetchBranches]);

  const handleAdminSubmit = async (e) => {
    e.preventDefault();
    if (!email || !password) {
      setError('Please enter email and password.');
      return;
    }

    try {
      setError('');
      setIsLoading(true);
      const userData = await loginAdmin(email, password);
      // Navigate based on the role in the RETURNED userData, not from context state
      // (context state may not have updated yet due to React batching)
      const role = userData?.role || userData?.Role || '';
      if (role === 'super_admin' || role.toLowerCase().includes('admin')) {
        navigate('/app/sessions', { replace: true });
      } else {
        navigate('/app/sessions', { replace: true });
      }
    } catch (err) {
      setError(err.message || 'Invalid admin credentials');
    } finally {
      setIsLoading(false);
    }
  };

  const handleOperatorSubmit = async (e) => {
    e.preventDefault();
    
    if (!username || !password || !selectedBranch) {
      setError('Please select a branch and enter credentials.');
      return;
    }

    try {
      setError('');
      setIsLoading(true);
      await loginOperator(selectedBranch, username.trim(), password.trim());
      // Navigation handled by useEffect
    } catch (err) {
      setError(err.message || 'Invalid operator credentials');
    } finally {
      setIsLoading(false);
    }
  };

  return (
    <div className="min-h-screen flex items-center justify-center p-4 overflow-hidden relative bg-bg">
      {/* Background glow effects */}
      <div className="absolute top-1/2 left-1/2 -translate-x-1/2 -translate-y-1/2 w-[600px] h-[600px] rounded-full blur-[120px] pointer-events-none bg-accent/20" />

      <motion.div
        initial={{ opacity: 0, y: 20 }}
        animate={{ opacity: 1, y: 0 }}
        transition={{ duration: 0.5, ease: "easeOut" }}
        className="card w-full max-w-md relative z-10 shadow-2xl shadow-black/50 border-border/60 backdrop-blur-xl p-8 bg-bg-2/80"
      >
        <div className="text-center mb-8">
          <motion.img
            initial={{ scale: 0.9 }}
            animate={{ scale: 1 }}
            transition={{ duration: 0.5 }}
            src="/logo.png"
            alt="Apple Esports"
            className="h-20 w-auto mx-auto mb-4 drop-shadow-[0_0_15px_rgba(220,38,38,0.5)]"
          />
          <h1 className="font-heading text-3xl font-bold mb-1 tracking-wide text-text">APPLE ESPORTS</h1>
          <p className="text-accent text-[11px] font-mono tracking-[0.2em] uppercase">
            Enterprise ERP System
          </p>
        </div>

        {reason === 'forced_logout' && (
          <div className="mb-6 p-3 bg-neon-red/10 border border-neon-red/30 rounded text-neon-red text-xs text-center">
            Your session was terminated by an administrator.
          </div>
        )}

        <AnimatePresence mode="wait">
          {branchUnreachable ? (
            <motion.div
              key="unreachable"
              initial={{ opacity: 0, x: -20 }}
              animate={{ opacity: 1, x: 0 }}
              exit={{ opacity: 0, x: 20 }}
              className="space-y-6"
            >
              {/* No login is offered here on purpose. The dashboard is served by the branch
                  server, so if it cannot be reached there is nothing to log in to - and a
                  PIN box that let someone "in" anyway would be telling the operator the
                  shop is working when it is not. */}
              <div className="p-4 bg-neon-red/10 border border-neon-red/30 rounded-lg flex items-start gap-3">
                <WifiOff className="w-5 h-5 text-neon-red flex-shrink-0 mt-0.5" />
                <div className="text-xs text-text-2 leading-relaxed">
                  <p className="text-neon-red font-semibold mb-1">Cannot reach the branch system</p>
                  <p>
                    This is the counter PC in your shop, not the internet - losing internet
                    does not cause this, and the shop can trade without it.
                  </p>
                </div>
              </div>

              <div className="text-xs text-text-2 leading-relaxed space-y-2">
                <p className="text-text font-semibold">What to check</p>
                <ul className="list-disc list-inside space-y-1 text-text-3">
                  <li>On the counter PC: is <span className="font-mono text-text-2">Apple Esports</span> running?</li>
                  <li>On a gaming PC: is it still connected to the shop network?</li>
                  <li>If the counter PC was just switched on, give it a minute to start.</li>
                </ul>
              </div>

              <button
                type="button"
                onClick={fetchBranches}
                disabled={loadingBranches}
                className="btn-primary w-full flex items-center justify-center gap-2"
              >
                {loadingBranches ? <Loader2 className="w-5 h-5 animate-spin" /> : 'Try again'}
              </button>
            </motion.div>
          ) : (
            <motion.div
              key="online"
              initial={{ opacity: 0, x: 20 }}
              animate={{ opacity: 1, x: 0 }}
              exit={{ opacity: 0, x: -20 }}
            >
              <form onSubmit={handleOperatorSubmit} className="space-y-4">
                <div>
                  <label className="block text-xs text-text-2 mb-1.5 ml-1">Branch Location</label>
                  <div className="relative">
                    <MapPin className="absolute left-3 top-1/2 -translate-y-1/2 w-4 h-4 text-text-3 pointer-events-none z-10" />
                    <select
                      value={selectedBranch}
                      onChange={(e) => setSelectedBranch(e.target.value)}
                      className="input w-full pl-10 pr-10 appearance-none bg-bg-3 cursor-pointer"
                      disabled={loadingBranches}
                    >
                      <option value="">Select Branch...</option>
                      {branches.map(b => (
                        <option key={b.id} value={b.id}>{b.name}</option>
                      ))}
                    </select>
                    {/* Caret, not an arrow. This was a download arrow, which reads as "save
                        this" rather than "open the list" — and no other select in the app
                        uses one, so it looked like a different kind of control entirely. */}
                    {loadingBranches ? (
                      <Loader2 className="absolute right-3 top-1/2 -translate-y-1/2 w-4 h-4 text-text-3 animate-spin pointer-events-none z-10" />
                    ) : (
                      <ChevronDown className="absolute right-3 top-1/2 -translate-y-1/2 w-4 h-4 text-text-3 pointer-events-none z-10" />
                    )}
                  </div>
                </div>
                
                <div>
                  <label className="block text-xs text-text-2 mb-1.5 ml-1">Username</label>
                  <div className="relative">
                    <User className="absolute left-3 top-1/2 -translate-y-1/2 w-4 h-4 text-text-3" />
                    <input 
                      type="text" 
                      value={username}
                      onChange={(e) => setUsername(e.target.value)}
                      className="input w-full pl-10"
                      placeholder="Enter operator username"
                    />
                  </div>
                </div>

                <div>
                  <label className="block text-xs text-text-2 mb-1.5 ml-1">Password</label>
                  <div className="relative">
                    <KeyRound className="absolute left-3 top-1/2 -translate-y-1/2 w-4 h-4 text-text-3" />
                    <input 
                      type={showPassword ? "text" : "password"} 
                      value={password}
                      onChange={(e) => setPassword(e.target.value)}
                      className="input w-full pl-10 pr-10"
                      placeholder="••••••••"
                    />
                    <button
                      type="button"
                      onClick={() => setShowPassword(!showPassword)}
                      className="absolute right-3 top-1/2 -translate-y-1/2 text-text-3 hover:text-text transition-colors"
                      tabIndex="-1"
                    >
                      {showPassword ? <EyeOff className="w-4 h-4" /> : <Eye className="w-4 h-4" />}
                    </button>
                  </div>
                  <div className="flex justify-center mt-1">
                    <button 
                      type="button" 
                      onClick={() => navigate('/forgot-password')} 
                      className="text-accent hover:text-red-400 transition-colors text-[10px] uppercase tracking-wider"
                    >
                      Forgot Password?
                    </button>
                  </div>
                </div>

                {error && <p className="text-neon-red text-xs mt-2 text-center">{error}</p>}

                <button 
                  type="submit" 
                  disabled={isLoading}
                  className="btn-primary w-full mt-6 relative overflow-hidden group flex items-center justify-center gap-2"
                >
                  {isLoading ? <Loader2 className="w-5 h-5 animate-spin" /> : 'Login as Operator'}
                </button>
              </form>
            </motion.div>
          )}
        </AnimatePresence>

        <div className="mt-8 text-center text-[10px] text-text-3 font-mono">
          <p>Branch-specific Access Only</p>
          <p className="mt-1">Activity is monitored per SOP guidelines</p>
          <div className="mt-4 pt-4 border-t border-border/30">
            <button
              onClick={() => navigate('/')}
              className="text-accent hover:text-white transition-colors uppercase tracking-widest text-[10px]"
            >
              ← Change Role
            </button>
          </div>
        </div>
      </motion.div>
    </div>
  );
}
