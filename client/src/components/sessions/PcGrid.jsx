import PcTile from './PcTile';

export default function PcGrid({ pcs, walkinRequests, selectedPcId, onSelectPc, onQuickStart, onRefresh }) {
  if (!pcs || pcs.length === 0) {
    return (
      <div className="flex flex-col items-center justify-center p-12 text-center bg-bg-2 border border-border rounded-lg">
        <p className="text-text-2 text-lg">No PCs detected for this branch.</p>
        <p className="text-text-3 text-sm mt-2">Add PCs via the Super Admin Settings.</p>
      </div>
    );
  }

  return (
    <div className="grid grid-cols-4 sm:grid-cols-5 md:grid-cols-6 xl:grid-cols-8 gap-2.5">
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
          />
        );
      })}
    </div>
  );
}
