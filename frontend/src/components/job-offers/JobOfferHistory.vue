<script setup lang="ts">
import { computed, ref } from "vue";
import type { Locale } from "../../i18n/utils";
import { formatDateTime } from "../../lib/job-offers/format";
import type { JobOfferFieldChange, JobOfferHistoryEntry } from "../../lib/job-offers/types";
import type { UserInfo } from "../../lib/user-info";
import MaterialIcon from "../icons/MaterialIcon.vue";
import { materialSymbolPaths as paths } from "../icons/material-symbol-paths";

const props = defineProps<{
  entries: JobOfferHistoryEntry[];
  /** Fresh display name/avatar per user id; falls back to the actor email on the entry. */
  users: Record<string, UserInfo>;
  lang: Locale;
  t: {
    title: string;
    empty: string;
    fieldLabels: Record<string, string>;
    changed: string;
    before: string;
    after: string;
    yes: string;
    no: string;
  };
}>();

// Comment events live in the comments section, not the activity log.
const visibleEntries = computed(() => props.entries.filter((entry) => entry.eventType !== "Comment"));

// Description/AdditionalNotes diffs don't fit inline — they get an expandable before/after panel.
const LONG_TEXT_FIELDS = new Set(["Description", "AdditionalNotes"]);
const expandedDiffs = ref(new Set<string>());

function diffKey(entryIndex: number, field: string): string {
  return `${entryIndex}:${field}`;
}

function toggleDiff(key: string) {
  const expanded = new Set(expandedDiffs.value);
  if (!expanded.delete(key)) expanded.add(key);
  expandedDiffs.value = expanded;
}

function fieldLabel(change: JobOfferFieldChange): string {
  return props.t.fieldLabels[change.field] ?? change.field;
}

function formatValue(value: string | null, field: string): string {
  if (value == null || value === "") return "—";
  if (field === "IsRemote") return value === "true" ? props.t.yes : props.t.no;
  return value;
}

function actorName(entry: JobOfferHistoryEntry): string {
  return (entry.actorUserId && props.users[entry.actorUserId]?.displayName) || entry.actorEmail;
}

function actorAvatar(entry: JobOfferHistoryEntry): string | undefined {
  return entry.actorUserId ? props.users[entry.actorUserId]?.avatarUrl : undefined;
}
</script>

<template>
  <div id="history-section" class="mt-6 bg-surface-container-low rounded-2xl border border-outline-variant/10 p-8">
    <h3 class="font-headline text-lg font-bold text-on-surface mb-4">
      <MaterialIcon :d="paths.history" class="size-[18px] inline align-middle mr-1" />
      {{ props.t.title }}
    </h3>
    <div id="history-list" class="space-y-3">
      <p v-if="visibleEntries.length === 0" class="text-sm text-on-surface-variant font-body">{{ props.t.empty }}</p>
      <div v-for="(entry, entryIndex) in visibleEntries" :key="entryIndex" class="flex items-start gap-3 text-sm">
        <img v-if="actorAvatar(entry)" :src="actorAvatar(entry)" alt="" class="w-6 h-6 rounded-full object-cover mt-0.5 flex-shrink-0" />
        <MaterialIcon v-else :d="paths.person" class="size-[18px] text-on-surface-variant shrink-0" />
        <div class="flex-1 min-w-0">
          <p class="font-body text-on-surface">{{ entry.description }}</p>
          <ul v-if="entry.changes && entry.changes.length > 0" class="mt-1 space-y-1 list-none pl-0">
            <li v-for="change in entry.changes" :key="change.field" class="text-on-surface-variant">
              <template v-if="LONG_TEXT_FIELDS.has(change.field)">
                <button
                  type="button"
                  class="group diff-toggle inline-flex items-center gap-1 font-semibold text-primary cursor-pointer bg-transparent border-none p-0 text-sm"
                  :aria-expanded="expandedDiffs.has(diffKey(entryIndex, change.field))"
                  @click="toggleDiff(diffKey(entryIndex, change.field))"
                >
                  <span class="group-hover:underline">{{ fieldLabel(change) }} {{ props.t.changed }}</span>
                  <MaterialIcon
                    :d="paths.expandMore"
                    class="size-3.5 transition-transform"
                    :class="{ 'rotate-180': expandedDiffs.has(diffKey(entryIndex, change.field)) }"
                  />
                </button>
                <div
                  v-show="expandedDiffs.has(diffKey(entryIndex, change.field))"
                  class="mt-2 rounded-lg border border-outline-variant/20 overflow-hidden text-xs"
                >
                  <div class="px-3 py-2 bg-error-container/30">
                    <p class="font-label font-semibold text-on-surface-variant mb-1">{{ props.t.before }}</p>
                    <p class="font-body text-on-surface whitespace-pre-line">{{ change.oldValue || "—" }}</p>
                  </div>
                  <div class="px-3 py-2 bg-tertiary-container/30">
                    <p class="font-label font-semibold text-on-surface-variant mb-1">{{ props.t.after }}</p>
                    <p class="font-body text-on-surface whitespace-pre-line">{{ change.newValue || "—" }}</p>
                  </div>
                </div>
              </template>
              <template v-else>
                <span class="font-semibold">{{ fieldLabel(change) }}:</span>
                <span class="line-through opacity-60">{{ formatValue(change.oldValue, change.field) }}</span>
                →
                <span>{{ formatValue(change.newValue, change.field) }}</span>
              </template>
            </li>
          </ul>
          <p class="text-xs font-label text-on-surface-variant mt-0.5">
            {{ actorName(entry) }} · {{ formatDateTime(entry.timestamp, lang) }}
          </p>
        </div>
      </div>
    </div>
  </div>
</template>
