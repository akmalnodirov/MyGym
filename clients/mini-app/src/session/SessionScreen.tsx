import { useCallback, useMemo, useState } from "react";
import type { components } from "../api/generated";
import { ApiError } from "../api/ApiError";
import { useExerciseNames, useProfile, useSession } from "../api/queries";
import { useCompleteSession, useLogSet } from "../api/mutations";
import { messageForCode } from "../errors/codes";
import { useClosingConfirmation } from "../telegram/useClosingConfirmation";
import { useMainButton } from "../telegram/useMainButton";
import { ErrorBanner } from "../ui/ErrorBanner";
import { Spinner } from "../ui/Spinner";
import { ExerciseCard } from "./ExerciseCard";
import { RestTimerBar } from "./RestTimerBar";
import { useRestTimer } from "./RestTimerProvider";
import { usePendingSetIds } from "./PendingSetIdsProvider";
import {
  deriveSetRows,
  isExerciseComplete,
  type RowStatus,
  type SetRow,
  type WeightUnit,
} from "./setRows";

type Session = components["schemas"]["SessionResponse"];

function elapsed(startedAt: string, endedAt: string | null): string {
  const start = new Date(startedAt).getTime();
  const end = endedAt === null ? Date.now() : new Date(endedAt).getTime();
  const totalSeconds = Math.max(0, Math.floor((end - start) / 1000));
  const hours = Math.floor(totalSeconds / 3600);
  const minutes = Math.floor((totalSeconds % 3600) / 60);
  const seconds = totalSeconds % 60;
  return `${hours}:${String(minutes).padStart(2, "0")}:${String(seconds).padStart(2, "0")}`;
}

export function SessionScreen({ sessionId, onLeave }: { sessionId: string; onLeave: () => void }) {
  const session = useSession(sessionId);
  const profile = useProfile();
  const { nameFor } = useExerciseNames();
  const timer = useRestTimer();
  const pending = usePendingSetIds();

  const [expandedId, setExpandedId] = useState<string | null>(null);
  const [extraRows, setExtraRows] = useState<Record<string, number>>({});

  const live = session.data?.status === "InProgress";
  useClosingConfirmation(live);

  // The rest timer is started from the row's own handler below rather than through this hook's
  // onLogged, so there is one place where a tap turns into both a write and a countdown.
  const logSet = useLogSet(sessionId);
  const complete = useCompleteSession(sessionId);

  const onComplete = useCallback(() => {
    complete.mutate(undefined, { onSuccess: onLeave });
  }, [complete, onLeave]);

  useMainButton({
    text: "Finish workout",
    onClick: onComplete,
    visible: live,
    loading: complete.isPending,
  });

  if (session.isError) {
    const error = session.error;
    return (
      <ErrorBanner
        message={error instanceof ApiError ? messageForCode(error.errorCode) : "Could not load this workout."}
        onRetry={onLeave}
        retryLabel="Back to today"
      />
    );
  }

  if (!session.isSuccess || !profile.isSuccess) return <Spinner label="Loading workout" />;

  const data: Session = session.data;
  const unit: WeightUnit = profile.data.preferredUnit;
  const readOnly = data.status !== "InProgress";

  return (
    <SessionBody
      data={data}
      unit={unit}
      loadingIncrement={profile.data.loadingIncrement}
      readOnly={readOnly}
      nameFor={nameFor}
      expandedId={expandedId}
      setExpandedId={setExpandedId}
      extraRows={extraRows}
      setExtraRows={setExtraRows}
      statuses={pending.statuses}
      completeError={complete.error}
      onLog={(exerciseId, row, values) =>
        logSet.mutate(
          {
            sessionExerciseId: exerciseId,
            rowKey: row.rowKey,
            kind: row.kind,
            weight: values.weight,
            reps: values.reps,
            rir: values.rir,
            unit,
          })
      }
      onLogged={(exercise) => timer.start(exercise.restMinSeconds, nameFor(exercise.exerciseId))}
    />
  );
}

interface SessionBodyProps {
  data: Session;
  unit: WeightUnit;
  loadingIncrement: number;
  readOnly: boolean;
  nameFor: (id: string) => string;
  expandedId: string | null;
  setExpandedId: (id: string | null) => void;
  extraRows: Record<string, number>;
  setExtraRows: (next: Record<string, number>) => void;
  statuses: ReadonlyMap<string, RowStatus>;
  completeError: ApiError | null;
  onLog: (
    exerciseId: string,
    row: SetRow,
    values: { weight: number; reps: number; rir: number | null },
  ) => void;
  onLogged: (exercise: Session["exercises"][number]) => void;
}

function SessionBody({
  data,
  unit,
  loadingIncrement,
  readOnly,
  nameFor,
  expandedId,
  setExpandedId,
  extraRows,
  setExtraRows,
  statuses,
  completeError,
  onLog,
  onLogged,
}: SessionBodyProps) {
  const statusFor = useCallback(
    (row: SetRow): RowStatus => {
      if (row.status === "logged") {
        // A logged row may still be in flight or failed: its clientSetId is the optimistic entry's.
        const local = row.clientSetId === null ? null : statuses.get(row.clientSetId);
        return local ?? "logged";
      }
      return row.status;
    },
    [statuses],
  );

  /**
   * The expanded card: the athlete's explicit choice if they made one, otherwise the first
   * exercise with work left. When everything is logged the LAST card stays open, so the screen
   * never becomes a wall of closed cards with no obvious next action.
   */
  const autoExpandedId = useMemo(() => {
    const incomplete = data.exercises.find(
      (exercise) => !isExerciseComplete(deriveSetRows(exercise, { unit }).map((row) => ({ ...row, status: statusFor(row) }))),
    );
    return incomplete?.id ?? data.exercises.at(-1)?.id ?? null;
  }, [data.exercises, unit, statusFor]);

  const openId = expandedId ?? autoExpandedId;

  const loggedWorking = data.exercises.reduce(
    (total, exercise) => total + exercise.sets.filter((set) => set.kind === "Working").length,
    0,
  );
  const prescribedWorking = data.exercises.reduce(
    (total, exercise) => total + exercise.workingSetCount,
    0,
  );

  return (
    <div className="screen session">
      <header className="session__header">
        <div className="session__heading">
          <h1 className="session__title">{readOnly ? "Workout" : "Today's workout"}</h1>
          <span className="session__elapsed">{elapsed(data.startedAt, data.completedAt)}</span>
        </div>
        <p className="session__count">
          {loggedWorking} / {prescribedWorking} working sets
        </p>
        {readOnly ? <p className="session__badge">{data.status}</p> : null}
      </header>

      {completeError === null ? null : (
        <ErrorBanner message={messageForCode(completeError.errorCode)} />
      )}

      <ul className="session__exercises">
        {data.exercises.map((exercise) => (
          <ExerciseCard
            key={exercise.id}
            exercise={exercise}
            name={nameFor(exercise.exerciseId)}
            unit={unit}
            loadingIncrement={loadingIncrement}
            expanded={exercise.id === openId}
            readOnly={readOnly}
            extraRows={extraRows[exercise.id] ?? 0}
            statusFor={statusFor}
            onExpand={() => setExpandedId(exercise.id === openId ? null : exercise.id)}
            onAddSet={() =>
              setExtraRows({ ...extraRows, [exercise.id]: (extraRows[exercise.id] ?? 0) + 1 })
            }
            onLog={(row, values) => {
              onLog(exercise.id, row, values);
              onLogged(exercise);
            }}
          />
        ))}
      </ul>

      <RestTimerBar />
    </div>
  );
}
