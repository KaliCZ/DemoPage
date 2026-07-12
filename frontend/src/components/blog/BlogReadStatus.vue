<script setup lang="ts">
import { onMounted, onUnmounted, ref } from "vue";
import { getAccessToken, getCurrentUser } from "../../lib/auth";
import { ensureVisitorLinked, visitorHeaders } from "../../lib/visitor";
import { SHOW_PUBLIC_STATS } from "../../lib/blog/flags";
import MaterialIcon from "../icons/MaterialIcon.vue";
import { materialSymbolPaths as paths } from "../icons/material-symbol-paths";

const showPublicStats = SHOW_PUBLIC_STATS;

const props = defineProps<{
  apiUrl: string;
  slug: string;
  t: {
    viewsLabel: string;
    peopleLabel: string;
    notReadYet: string;
    readOnce: string;
    readTimes: string;
  };
}>();

const loaded = ref(false);
const totalViews = ref<number | null>(null);
const uniqueVisitors = ref(0);
// Only set when signed in — the reader's personal "read N times" note.
const readLabel = ref<string | null>(null);

// One recorded view per auth state ("anon", then each user id) per page load —
// auth-change also fires for token refreshes. The 15-minute window is enforced server-side.
let recordedForKey: string | null = null;

async function recordView(user: any) {
  const key = user?.id ?? "anon";
  if (recordedForKey === key) return;
  recordedForKey = key;

  if (user?.id) void ensureVisitorLinked(props.apiUrl);

  try {
    const token = user ? await getAccessToken() : null;
    const res = await fetch(`${props.apiUrl}/api/blog/${props.slug}/views`, {
      method: "POST",
      headers: visitorHeaders(token),
    });
    if (!res.ok) return;
    const data: { previousViewCount: number; totalViews: number; uniqueVisitors: number } = await res.json();
    totalViews.value = data.totalViews;
    uniqueVisitors.value = data.uniqueVisitors;
    readLabel.value = user ? readStateLabel(data.previousViewCount) : null;
  } catch {
    // The view count is decorative — a network hiccup just leaves it hidden.
  } finally {
    loaded.value = true;
  }
}

function readStateLabel(previousViewCount: number): string {
  if (previousViewCount === 0) return props.t.notReadYet;
  if (previousViewCount === 1) return props.t.readOnce;
  return props.t.readTimes.replace("{count}", String(previousViewCount));
}

const onAuthChange = (event: Event) => void recordView((event as CustomEvent).detail?.user ?? null);

onMounted(async () => {
  window.addEventListener("auth-change", onAuthChange);
  // auth-change may already have fired before hydration — pull the current user.
  await recordView(await getCurrentUser());
});
onUnmounted(() => window.removeEventListener("auth-change", onAuthChange));
</script>

<template>
  <!-- Public view/reader counts are gated until the pre-rollout traffic is seeded; the reader's own count always shows. -->
  <template v-if="showPublicStats">
    <span v-if="!loaded" class="inline-block h-4 w-24 rounded bg-on-surface/10 animate-pulse align-middle" aria-hidden="true" />
    <span v-else-if="totalViews !== null" class="inline-flex items-center gap-1.5">
      <MaterialIcon :d="paths.visibility" class="size-4" />
      {{ totalViews }}
      <span class="sr-only">{{ props.t.viewsLabel }}</span>
      <span aria-hidden="true">·</span>
      <MaterialIcon :d="paths.person" class="size-4" />
      {{ uniqueVisitors }}
      <span class="sr-only">{{ props.t.peopleLabel }}</span>
      <template v-if="readLabel"
        ><span aria-hidden="true">·</span> <span>{{ readLabel }}</span></template
      >
    </span>
  </template>
  <span v-else-if="readLabel">{{ readLabel }}</span>
</template>
