import PcTile from './PcTile';

// ── Grid column counts per tile size — bigger tiles need fewer columns ──
const GRID_COLS = {
  sm: 'grid-cols-4 sm:grid-cols-6 md:grid-cols-7 xl:grid-cols-9',
  md: 'grid-cols-3 sm:grid-cols-4 md:grid-cols-5 xl:grid-cols-6',
  lg: 'grid-cols-2 sm:grid-cols-3 md:grid-cols-4 xl:grid-cols-5',
  xl: 'grid-cols-2 sm:grid-cols-2 md:grid-cols-3 xl:grid-cols-4',
};

export default function PcGrid({ pcs, walkinRequests, selectedPcId, onSelectPc, onQuickStart, onRefresh, size = 'md' }) {
  if (!pcs || pcs.length === 0) {
    return (
      <div className="flex flex-col items-center justify-center p-12 text-center bg-bg-2 border border-border rounded-lg">
        <p className="text-text-2 text-lg">No PCs detected for this branch.</p>
        <p className="text-text-3 text-sm mt-2">Add PCs via the Super Admin Settings.</p>
      </div>
    );
  }

  return (
    <div className={`grid ${GRID_COLS[size] || GRID_COLS.md} gap-3`}>
      {pcs.map((pc) => {
        const walkinReq = walkinRequests?.find(r => r.pcId === pc.name || r.pcId === pc.id);
        return (
          <PcTile
            key={pc.id}
            pc={pc}
            walkinReq={walkinReq}
            isSelected={selectedPcId === pc.id}
            onSelect={onSelectPc}
            onQuickStart={onQuickStart}
            onRefresh={onRefresh}
            size={size}
          />
        );
      })}
    </div>
  );
}
