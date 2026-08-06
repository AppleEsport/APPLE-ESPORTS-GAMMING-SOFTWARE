import { createContext, useContext, useState, useEffect } from 'react';

const ActivityLogContext = createContext();

export function ActivityLogProvider({ children }) {
  const [entries, setEntries] = useState([]);

  // Load from localStorage on mount
  useEffect(() => {
    try {
      const stored = localStorage.getItem('sessionActivityLog');
      if (stored) {
        setEntries(JSON.parse(stored));
      }
    } catch (err) {
      console.error('Failed to load activity log from localStorage:', err);
    }
  }, []);

  // Persist to localStorage whenever entries change
  useEffect(() => {
    try {
      localStorage.setItem('sessionActivityLog', JSON.stringify(entries.slice(-200))); // Keep last 200
    } catch (err) {
      console.error('Failed to save activity log to localStorage:', err);
    }
  }, [entries]);

  const addEntry = (entry) => {
    setEntries(prev => [...prev.slice(-(200 - 1)), { ...entry, id: Date.now() }]);
  };

  const addEntries = (newEntries) => {
    setEntries(prev => [...new Set([...prev, ...newEntries].map(e => JSON.stringify(e))).values()].map(e => JSON.parse(e)).sort((a, b) => new Date(a.timestamp) - new Date(b.timestamp)).slice(-200));
  };

  const clearEntries = () => {
    setEntries([]);
    localStorage.removeItem('sessionActivityLog');
  };

  return (
    <ActivityLogContext.Provider value={{ entries, addEntry, addEntries, clearEntries }}>
      {children}
    </ActivityLogContext.Provider>
  );
}

export function useActivityLog() {
  const context = useContext(ActivityLogContext);
  if (!context) {
    throw new Error('useActivityLog must be used within ActivityLogProvider');
  }
  return context;
}
