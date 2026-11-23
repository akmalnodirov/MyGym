import { useCallback } from "react";
import { ApiError } from "../api/ApiError";
import { useDayPreview, useExerciseNames } from "../api/queries";
import { useStartSession } from "../api/mutations";
import { messageForCode, recoveryForCode } from "../errors/codes";
import { useMainButton } from "../telegram/useMainButton";
import { ErrorBanner } from "../ui/ErrorBanner";
import { Spinner } from "../ui/Spinner";

const UNIT_LABEL: Record<string, string> = { Kilograms: "kg", Pounds: "lb" };

export interface DayPreviewScreenProps {
  programId: string;
  dayId: string;
  onStarted: (sessionId: string) => void;
  /** Called when a workout turns out to be open already, so the athlete lands in it. */
  onResumeCurrent: () => void;
  onLeave: () => void;
}

export function DayPreviewScreen({
  programId,
  dayId,
  onStarted,
  onResumeCurrent,
  onLeave,
}: DayPreviewScreenProps) {
  const preview = useDayPreview(programId, dayId);
  const { nameFor } = useExerciseNames();
  const start = useStartSession();

  const onStart = useCallback(() => {
    start.mutate(
      { programId, programDayId: dayId },
      {
        onSuccess: (session) => onStarted(session.id),
        onError: (error) => {
          // A workout being open already is not a failure the athlete should read about -- they
          // wanted to be in a workout, so take them to the one they have.
          if (recoveryForCode(error.errorCode) === "resumeCurrentSession") onResumeCurrent();
        },
      },
    );
  }, [start, programId, dayId, onStarted, onResumeCurrent]);

  useMainButton({
    text: "Start workout",
    onClick: onStart,
    visible: preview.isSuccess,
    loading: start.isPending,
  });

  if (preview.isError) {
    const error = preview.error;
    return (
      <ErrorBanner
        message={error instanceof ApiError ? messageForCode(error.errorCode) : "Could not load this day."}
        onRetry={onLeave}
        retryLabel="Back to today"
      />
    );
  }

  if (!preview.isSuccess) return <Spinner label="Loading day" />;

  const startError =
    start.error !== null && recoveryForCode(start.error.errorCode) !== "resumeCurrentSession"
      ? messageForCode(start.error.errorCode)
      : null;

  return (
    <div className="screen">
      <header className="session__header">
        <h1 className="session__title">{preview.data.label}</h1>
        <p className="session__count">
          {preview.data.exercises.length} exercise{preview.data.exercises.length === 1 ? "" : "s"}
        </p>
      </header>

      {startError === null ? null : <ErrorBanner message={startError} />}

      <ul className="session__exercises">
        {preview.data.exercises.map((exercise) => {
          const unit = UNIT_LABEL[exercise.weightUnit] ?? exercise.weightUnit;
          return (
            <li className="card" key={exercise.exerciseId}>
              <p className="card__name">{nameFor(exercise.exerciseId)}</p>
              <p className="card__target">
                {exercise.workingSetCount} × {exercise.targetRepMin}-{exercise.targetRepMax}
                {" @ "}
                {/* A dash, never "null" or NaN: no history means no suggestion yet. */}
                {exercise.suggestedWorkingWeight === null
                  ? "—"
                  : `${exercise.suggestedWorkingWeight}${unit}`}
                {" · rest "}
                {Math.round(exercise.restMinSeconds / 60)}-{Math.round(exercise.restMaxSeconds / 60)}m
              </p>
              {exercise.warmup.length === 0 ? null : (
                <p className="card__summary">
                  warm-up{" "}
                  {exercise.warmup
                    .map((step) => `${step.weight}${unit}×${step.reps}${step.isOptional ? "?" : ""}`)
                    .join("  ")}
                </p>
              )}
              {exercise.notes === null ? null : <p className="card__summary">{exercise.notes}</p>}
            </li>
          );
        })}
      </ul>
    </div>
  );
}
