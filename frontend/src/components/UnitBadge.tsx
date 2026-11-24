import { useState } from 'react';
import type { UnitType } from '../types/models';
import { UNIT_IMAGES } from '../utils/Images';

export default function UnitBadge({ type }: { type: UnitType }) {
  const [failed, setFailed] = useState(false);
  return (
    <span className="unit-badge">
      {failed ? (
        <span className="unit-fallback">{type[0]}</span>
      ) : (
        <img src={UNIT_IMAGES[type]} alt={type} onError={() => setFailed(true)} />
      )}
    </span>
  );
}