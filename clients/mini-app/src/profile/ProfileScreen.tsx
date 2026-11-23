import { useEffect, useState } from "react";
import type { components } from "../api/generated";
import { useProfile } from "../api/queries";
import { useUpdateProfile } from "../api/mutations";
import { messageForCode } from "../errors/codes";
import { useMainButton } from "../telegram/useMainButton";
import { ErrorBanner } from "../ui/ErrorBanner";
import { Spinner } from "../ui/Spinner";

type Profile = components["schemas"]["ProfileResponse"];
type WeightUnit = components["schemas"]["WeightUnit"];
type ExperienceLevel = components["schemas"]["ExperienceLevel"];

interface Draft {
  birthYear: string;
  bodyweight: string;
  preferredUnit: WeightUnit;
  loadingIncrement: string;
  experienceLevel: ExperienceLevel;
  availableDaysPerWeek: string;
  sessionMinutes: string;
  timeZoneId: string;
}

function toDraft(profile: Profile): Draft {
  return {
    birthYear: profile.birthYear === null ? "" : String(profile.birthYear),
    bodyweight: profile.bodyweight === null ? "" : String(profile.bodyweight),
    preferredUnit: profile.preferredUnit,
    loadingIncrement: String(profile.loadingIncrement),
    experienceLevel: profile.experienceLevel,
    availableDaysPerWeek:
      profile.availableDaysPerWeek === null ? "" : String(profile.availableDaysPerWeek),
    sessionMinutes: profile.sessionMinutes === null ? "" : String(profile.sessionMinutes),
    timeZoneId: profile.timeZoneId,
  };
}

const numberOrNull = (value: string): number | null => {
  const trimmed = value.trim();
  if (trimmed === "") return null;
  const parsed = Number(trimmed);
  return Number.isFinite(parsed) ? parsed : null;
};

export function ProfileScreen({ onSaved }: { onSaved: () => void }) {
  const profile = useProfile();
  const update = useUpdateProfile();
  const [draft, setDraft] = useState<Draft | null>(null);

  useEffect(() => {
    if (profile.data !== undefined && draft === null) setDraft(toDraft(profile.data));
  }, [profile.data, draft]);

  const onSave = (): void => {
    if (draft === null) return;
    // PATCH /api/me is a full replace on this API, so every field goes every time. Sending only
    // what changed would clear the rest.
    update.mutate(
      {
        birthYear: numberOrNull(draft.birthYear),
        bodyweight: numberOrNull(draft.bodyweight),
        preferredUnit: draft.preferredUnit,
        loadingIncrement: numberOrNull(draft.loadingIncrement) ?? 2.5,
        experienceLevel: draft.experienceLevel,
        availableDaysPerWeek: numberOrNull(draft.availableDaysPerWeek),
        sessionMinutes: numberOrNull(draft.sessionMinutes),
        timeZoneId: draft.timeZoneId,
      },
      { onSuccess: onSaved },
    );
  };

  useMainButton({
    text: "Save",
    onClick: onSave,
    visible: draft !== null,
    loading: update.isPending,
  });

  if (profile.isError) {
    return <ErrorBanner message="Could not load your profile." onRetry={() => void profile.refetch()} />;
  }

  if (draft === null) return <Spinner label="Loading profile" />;

  const set = <K extends keyof Draft>(key: K, value: Draft[K]): void =>
    setDraft({ ...draft, [key]: value });

  return (
    <div className="screen">
      <header className="session__header">
        <h1 className="session__title">Profile</h1>
      </header>

      {update.error === null ? null : (
        <ErrorBanner message={messageForCode(update.error.errorCode)} />
      )}

      <label className="field">
        <span className="field__label">Weight unit</span>
        <select
          className="field__control"
          value={draft.preferredUnit}
          onChange={(event) => set("preferredUnit", event.target.value as WeightUnit)}
        >
          <option value="Kilograms">Kilograms</option>
          <option value="Pounds">Pounds</option>
        </select>
      </label>

      <label className="field">
        <span className="field__label">
          Plate increment — the smallest change you can actually load
        </span>
        <input
          className="field__control"
          type="number"
          inputMode="decimal"
          step="0.5"
          min="0.5"
          value={draft.loadingIncrement}
          onChange={(event) => set("loadingIncrement", event.target.value)}
        />
      </label>

      <label className="field">
        <span className="field__label">Bodyweight</span>
        <input
          className="field__control"
          type="number"
          inputMode="decimal"
          step="0.1"
          value={draft.bodyweight}
          onChange={(event) => set("bodyweight", event.target.value)}
        />
      </label>

      <label className="field">
        <span className="field__label">Experience</span>
        <select
          className="field__control"
          value={draft.experienceLevel}
          onChange={(event) => set("experienceLevel", event.target.value as ExperienceLevel)}
        >
          <option value="Beginner">Beginner</option>
          <option value="Intermediate">Intermediate</option>
          <option value="Advanced">Advanced</option>
        </select>
      </label>

      <label className="field">
        <span className="field__label">Birth year</span>
        <input
          className="field__control"
          type="number"
          inputMode="numeric"
          value={draft.birthYear}
          onChange={(event) => set("birthYear", event.target.value)}
        />
      </label>

      <label className="field">
        <span className="field__label">Training days per week</span>
        <input
          className="field__control"
          type="number"
          inputMode="numeric"
          min="1"
          max="7"
          value={draft.availableDaysPerWeek}
          onChange={(event) => set("availableDaysPerWeek", event.target.value)}
        />
      </label>

      <label className="field">
        <span className="field__label">Minutes per session</span>
        <input
          className="field__control"
          type="number"
          inputMode="numeric"
          value={draft.sessionMinutes}
          onChange={(event) => set("sessionMinutes", event.target.value)}
        />
      </label>

      <label className="field">
        <span className="field__label">Time zone</span>
        <input
          className="field__control"
          value={draft.timeZoneId}
          onChange={(event) => set("timeZoneId", event.target.value)}
        />
        {update.error?.errorCode === "profile.time_zone_unknown" ? (
          <span className="field__error">{messageForCode("profile.time_zone_unknown")}</span>
        ) : null}
      </label>
    </div>
  );
}
