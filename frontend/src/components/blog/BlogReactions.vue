<script setup lang="ts">
import { onMounted, onUnmounted, reactive, ref } from "vue";
import { getAccessToken } from "../../lib/auth";
import { ensureVisitorLinked, visitorHeaders } from "../../lib/visitor";

type ReactionKind = "ThumbsUp" | "ThumbsDown" | "Heart" | "Insightful" | "Rocket";

const props = defineProps<{
  apiUrl: string;
  slug: string;
  t: {
    heading: string;
    error: string;
    labels: Record<ReactionKind, string>;
  };
}>();

const REACTIONS: { kind: ReactionKind; emoji: string }[] = [
  { kind: "ThumbsUp", emoji: "👍" },
  { kind: "ThumbsDown", emoji: "👎" },
  { kind: "Heart", emoji: "❤️" },
  { kind: "Insightful", emoji: "💡" },
  { kind: "Rocket", emoji: "🚀" },
];

const counts = reactive<Record<ReactionKind, number>>({
  ThumbsUp: 0,
  ThumbsDown: 0,
  Heart: 0,
  Insightful: 0,
  Rocket: 0,
});
const mine = ref<Set<ReactionKind>>(new Set());
const busy = ref(false);
// Gates a skeleton so the buttons don't flash zeros before the first load lands.
const loaded = ref(false);

function applyState(data: { counts: Record<string, number>; mine: ReactionKind[] }) {
  counts.ThumbsUp = data.counts.thumbsUp ?? 0;
  counts.ThumbsDown = data.counts.thumbsDown ?? 0;
  counts.Heart = data.counts.heart ?? 0;
  counts.Insightful = data.counts.insightful ?? 0;
  counts.Rocket = data.counts.rocket ?? 0;
  mine.value = new Set(data.mine);
}

// Bumped when a toggle lands so a slower in-flight load can't overwrite fresher state.
let loadSequence = 0;

async function loadReactions() {
  const sequence = ++loadSequence;
  try {
    const token = await getAccessToken();
    const res = await fetch(`${props.apiUrl}/api/blog/${props.slug}/reactions`, {
      headers: visitorHeaders(token),
    });
    if (res.ok) {
      const data = await res.json();
      if (sequence === loadSequence) applyState(data);
    }
  } catch {
    // Counts are decorative on load — a network hiccup just leaves them at zero.
  } finally {
    loaded.value = true;
  }
}

async function toggle(kind: ReactionKind) {
  if (busy.value) return;

  // Optimistic flip; the server response is authoritative and replaces it. Anyone may
  // react — signed out, the reaction is keyed to the anonymous visitor id.
  const wasMine = mine.value.has(kind);
  counts[kind] += wasMine ? -1 : 1;
  const optimistic = new Set(mine.value);
  if (wasMine) optimistic.delete(kind);
  else optimistic.add(kind);
  mine.value = optimistic;

  busy.value = true;
  try {
    const token = await getAccessToken();
    const res = await fetch(`${props.apiUrl}/api/blog/${props.slug}/reactions/toggle`, {
      method: "POST",
      headers: { "Content-Type": "application/json", ...visitorHeaders(token) },
      body: JSON.stringify({ kind }),
    });
    if (!res.ok) throw new Error(`Reaction toggle failed with ${res.status}`);
    loadSequence++;
    applyState(await res.json());
  } catch {
    counts[kind] += wasMine ? 1 : -1;
    const reverted = new Set(mine.value);
    if (wasMine) reverted.add(kind);
    else reverted.delete(kind);
    mine.value = reverted;
    (window as any).__showSnackbar?.(props.t.error, "error", 8000);
  } finally {
    busy.value = false;
  }
}

const onAuthChange = async (event: Event) => {
  // Fold anonymous reactions into the account before reloading, so "mine" reflects them.
  if ((event as CustomEvent).detail?.user) await ensureVisitorLinked(props.apiUrl);
  await loadReactions();
};

onMounted(() => {
  void loadReactions();
  window.addEventListener("auth-change", onAuthChange);
});
onUnmounted(() => window.removeEventListener("auth-change", onAuthChange));
</script>

<template>
  <section :aria-label="props.t.heading">
    <h2 class="font-headline text-2xl font-bold tracking-tight mb-4">{{ props.t.heading }}</h2>
    <div v-if="!loaded" class="flex flex-wrap gap-2" aria-hidden="true">
      <span v-for="{ kind } in REACTIONS" :key="kind" class="h-8 w-16 rounded-full bg-on-surface/10 animate-pulse" />
    </div>
    <div v-else class="flex flex-wrap gap-2">
      <button
        v-for="{ kind, emoji } in REACTIONS"
        :key="kind"
        type="button"
        :aria-pressed="mine.has(kind)"
        :aria-label="props.t.labels[kind]"
        :title="props.t.labels[kind]"
        class="flex items-center gap-2 px-3.5 py-1.5 rounded-full border font-label text-sm transition-colors cursor-pointer"
        :class="
          mine.has(kind)
            ? 'bg-primary-container text-on-primary-container border-primary/40'
            : 'bg-surface-container-low text-on-surface-variant border-outline-variant/30 hover:border-primary/40 hover:text-on-surface'
        "
        @click="toggle(kind)"
      >
        <span aria-hidden="true">{{ emoji }}</span>
        <span>{{ counts[kind] }}</span>
      </button>
    </div>
  </section>
</template>
