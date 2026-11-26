/**
 * @file UnitBadge.tsx
 * @brief Displays a unit icon, falling back to its type initial if the image fails.
 */


import { useState } from 'react';
import type { UnitType } from '../types/models';
import { UNIT_IMAGES } from '../utils/Images';

/** Displays a unit-type icon, falling back to the type's initial if the image fails to load. */
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