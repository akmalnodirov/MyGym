interface StepperProps {
  value: number;
  step: number;
  min: number;
  max: number;
  label: string;
  suffix?: string;
  onChange: (value: number) => void;
}

/**
 * A plus/minus control for one number.
 *
 * Steps by the increment the caller gives it -- the athlete's plate increment for a weight, 1 for
 * reps -- because a generic 1 would make adjusting a barbell weight six taps instead of one.
 * Values are rounded to the step grid so repeated floating-point addition cannot drift 82.5 into
 * 82.50000000000001.
 */
export function Stepper({ value, step, min, max, label, suffix, onChange }: StepperProps) {
  const places = (String(step).split(".")[1] ?? "").length;
  const clamp = (next: number): number =>
    Number(Math.min(max, Math.max(min, next)).toFixed(places));

  return (
    <span className="stepper" role="group" aria-label={label}>
      <button
        type="button"
        className="stepper__button"
        aria-label={`Decrease ${label}`}
        disabled={value <= min}
        onClick={() => onChange(clamp(value - step))}
      >
        −
      </button>
      <span className="stepper__value" aria-live="polite">
        {value}
        {suffix === undefined ? null : <span className="stepper__suffix">{suffix}</span>}
      </span>
      <button
        type="button"
        className="stepper__button"
        aria-label={`Increase ${label}`}
        disabled={value >= max}
        onClick={() => onChange(clamp(value + step))}
      >
        +
      </button>
    </span>
  );
}
