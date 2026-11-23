import type { components } from "../api/generated";
import { SetRow } from "./SetRow";
import { deriveSetRows, type RowStatus, type SetRow as Row, type WeightUnit } from "./setRows";

type SessionExercise = components["schemas"]["SessionExerciseResponse"];

function restLabel(minSeconds: number, maxSeconds: number): string {
  const asMinutes = (seconds: number): string => {
    const minutes = seconds / 60;
    return Number.isInteger(minutes) ? String(minutes) : minutes.toFixed(1);
  };
  return minSeconds === maxSeconds
    ? `rest ${asMinutes(minSeconds)}m`
    : `rest ${asMinutes(minSeconds)}-${asMinutes(maxSeconds)}m`;
}

export interface ExerciseCardProps {
  exercise: SessionExercise;
  name: string;
  unit: WeightUnit;
  loadingIncrement: number;
  expanded: boolean;
  readOnly: boolean;
  /** Pending working rows the athlete added beyond the prescription. */
  extraRows: number;
  statusFor: (row: Row) => RowStatus;
  onExpand: () => void;
  onAddSet: () => void;
  onLog: (row: Row, values: { weight: number; reps: number; rir: number | null }) => void;
}

export function ExerciseCard({
  exercise,
  name,
  unit,
  loadingIncrement,
  expanded,
  readOnly,
  extraRows,
  statusFor,
  onExpand,
  onAddSet,
  onLog,
}: ExerciseCardProps) {
  // Raising the prescribed count is all an extra row needs: the derivation then produces it as a
  // pending working row with the next ordinal, so its row key -- and therefore its idempotency
  // key -- follows the same rule as every other row.
  const rows = deriveSetRows(
    { ...exercise, workingSetCount: exercise.workingSetCount + extraRows },
    { unit },
  );
  const warmups = rows.filter((row) => row.kind === "Warmup");
  const working = rows.filter((row) => row.kind === "Working");

  const target = `${exercise.workingSetCount} × ${exercise.targetRepMin}-${exercise.targetRepMax}`;
  // Displays the prescribed range; the countdown itself runs to the minimum, which marks the
  // earliest reasonable moment for the next set rather than a deadline.
  const rest = restLabel(exercise.restMinSeconds, exercise.restMaxSeconds);

  if (!expanded) {
    const done = rows.filter((row) => statusFor(row) === "logged");
    return (
      <li className="card card--collapsed">
        <button type="button" className="card__header" onClick={onExpand} aria-expanded={false}>
          <span className="card__name">{name}</span>
          <span className="card__target">
            {target} {exercise.suggestedWorkingWeight === null ? "" : `@ ${exercise.suggestedWorkingWeight}`}
          </span>
        </button>
        {done.length === 0 ? null : (
          <p className="card__summary">
            {done.map((row) => `${row.weight}×${row.reps}`).join("  ")}
          </p>
        )}
      </li>
    );
  }

  const section = (title: string, sectionRows: Row[]) =>
    sectionRows.length === 0 ? null : (
      <>
        <h3 className="card__section">{title}</h3>
        <ul className="card__rows">
          {sectionRows.map((row) => (
            <SetRow
              key={row.rowKey}
              row={row}
              status={statusFor(row)}
              loadingIncrement={loadingIncrement}
              readOnly={readOnly}
              onLog={(values) => onLog(row, values)}
            />
          ))}
        </ul>
      </>
    );

  return (
    <li className="card card--expanded">
      <button type="button" className="card__header" onClick={onExpand} aria-expanded={true}>
        <span className="card__name">{name}</span>
      </button>
      <p className="card__target">
        target {target} · {rest}
      </p>
      {section("warm-up", warmups)}
      {section("working", working)}
      {readOnly ? null : (
        <button type="button" className="card__add" onClick={onAddSet}>
          + add set
        </button>
      )}
    </li>
  );
}
