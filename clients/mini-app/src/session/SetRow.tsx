import { useState } from "react";
import { Stepper } from "../ui/Stepper";
import type { SetRow as Row, RowStatus } from "./setRows";

const UNIT_LABEL: Record<string, string> = { Kilograms: "kg", Pounds: "lb" };

export interface SetRowProps {
  row: Row;
  status: RowStatus;
  loadingIncrement: number;
  readOnly: boolean;
  onLog: (values: { weight: number; reps: number; rir: number | null }) => void;
}

/**
 * One set: the weight and reps it expects, and a single tap to confirm them.
 *
 * Tapping a number swaps it for a stepper, so adjusting is in reach without making the common case
 * -- the prescribed numbers are right -- cost more than one tap.
 */
export function SetRow({ row, status, loadingIncrement, readOnly, onLog }: SetRowProps) {
  const [weight, setWeight] = useState(row.weight);
  const [reps, setReps] = useState(row.reps);
  const [rir, setRir] = useState<number | null>(row.rir);
  const [editing, setEditing] = useState<"none" | "weight" | "reps" | "rir">("none");

  const unit = UNIT_LABEL[row.unit] ?? row.unit;
  const kindLabel = row.kind === "Warmup" ? "warm-up" : "working";
  const position = `${kindLabel} set ${row.ordinal + 1}`;

  // An in-flight set renders as confirmed, not as still-pending. That is the entire point of
  // logging optimistically: the athlete taps once and the row is done, because they are already
  // racking the bar. The only difference is a muted marker saying it is still saving.
  if (status === "logged" || status === "inFlight") {
    const saving = status === "inFlight";
    return (
      <li
        className={`set-row set-row--logged${saving ? " set-row--saving" : ""}`}
        data-testid={`row-${row.rowKey}`}
      >
        <span className="set-row__tick" aria-hidden="true">
          ✓
        </span>
        <span className="set-row__done">
          {row.weight}
          {unit} × {row.reps}
          {row.rir === null ? null : <span className="set-row__rir"> RIR {row.rir}</span>}
        </span>
        {saving ? (
          <span className="set-row__saving" aria-hidden="true">
            saving
          </span>
        ) : null}
        <span className="visually-hidden">{position} logged</span>
      </li>
    );
  }

  if (readOnly) {
    return (
      <li className="set-row set-row--readonly" data-testid={`row-${row.rowKey}`}>
        <span className="set-row__done set-row__done--muted">
          {row.weight}
          {unit} × {row.reps}
        </span>
        <span className="visually-hidden">{position} not logged</span>
      </li>
    );
  }

  const failed = status === "failed";

  return (
    <li
      className={`set-row${failed ? " set-row--failed" : ""}`}
      data-testid={`row-${row.rowKey}`}
    >
      {row.isOptional ? (
        <span className="set-row__optional" title="Optional warm-up">
          ◦
        </span>
      ) : (
        <span className="set-row__box" aria-hidden="true">
          ▢
        </span>
      )}

      {editing === "weight" ? (
        <Stepper
          value={weight}
          step={loadingIncrement}
          min={0}
          max={1000}
          label={`weight for ${position}`}
          suffix={unit}
          onChange={setWeight}
        />
      ) : (
        <button
          type="button"
          className="set-row__number"
          aria-label={`Adjust weight for ${position}, currently ${weight} ${unit}`}
          onClick={() => setEditing("weight")}
        >
          {weight}
          {unit}
        </button>
      )}

      <span className="set-row__times" aria-hidden="true">
        ×
      </span>

      {editing === "reps" ? (
        <Stepper
          value={reps}
          step={1}
          min={1}
          max={100}
          label={`reps for ${position}`}
          onChange={setReps}
        />
      ) : (
        <button
          type="button"
          className="set-row__number"
          aria-label={`Adjust reps for ${position}, currently ${reps}`}
          onClick={() => setEditing("reps")}
        >
          {reps}
        </button>
      )}

      {row.kind === "Working" ? (
        editing === "rir" ? (
          <Stepper
            value={rir ?? 2}
            step={1}
            min={0}
            max={10}
            label={`RIR for ${position}`}
            onChange={setRir}
          />
        ) : (
          <button
            type="button"
            className="set-row__rir-button"
            aria-label={`Add reps in reserve for ${position}`}
            onClick={() => {
              setRir(rir ?? 2);
              setEditing("rir");
            }}
          >
            {rir === null ? "RIR" : `RIR ${rir}`}
          </button>
        )
      ) : null}

      <button
        type="button"
        className="button button--primary set-row__log"
        onClick={() => onLog({ weight, reps, rir })}
      >
        {failed ? "Retry" : "Log"}
      </button>

      {failed ? (
        <span className="set-row__unsynced" role="status">
          Not saved
        </span>
      ) : null}
    </li>
  );
}
