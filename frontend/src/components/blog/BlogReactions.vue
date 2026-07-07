<script setup lang="ts">
import { onMounted, onUnmounted, reactive, ref } from "vue";

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

function applyState(data: { counts: Record<string, number>; mine: ReactionKind[] }) {
  counts.ThumbsUp = data.counts.thumbsUp ?? 0;
  counts.ThumbsDown = data.counts.thumbsDown ?? 0;
  counts.Heart = data.counts.heart ?? 0;
  counts.Insightful = data.counts.insightful ?? 0;
  counts.Rocket = data.counts.rocket ?? 0;
  mine.value = new Set(data.mine);
}

async function authToken(): Promise<string | null> {
  return ((await (window as any).__getAccessToken?.()) as string | null) ?? null;
}

async function loadReactions() {
  try {
    const token = await authToken();
    const res = await fetch(`${props.apiUrl}/api/blog/${props.slug}/reactions`, {
      headers: token ? { Authorization: `Bearer ${token}` } : {},
    });
    if (res.ok) applyState(await res.json());
  } catch {
    // Counts are decorative on load — a network hiccup just leaves them at zero.
  }
}

async function toggle(kind: ReactionKind) {
  if (busy.value) return;

  const token = await authToken();
  if (!token) {
    (window as any).__openAuthDialog?.();
    return;
  }

  // Optimistic flip; the server response is authoritative and replaces it.
  const wasMine = mine.value.has(kind);
  counts[kind] += wasMine ? -1 : 1;
  const optimistic = new Set(mine.value);
  if (wasMine) optimistic.delete(kind);
  else optimistic.add(kind);
  mine.value = optimistic;

  busy.value = true;
  try {
    const res = await fetch(`${props.apiUrl}/api/blog/${props.slug}/reactions/toggle`, {
      method: "POST",
      headers: { "Content-Type": "application/json", Authorization: `Bearer ${token}` },
      body: JSON.stringify({ kind }),
    });
    if (!res.ok) throw new Error(`Reaction toggle failed with ${res.status}`);
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

const refresh = () => void loadReactions();

onMounted(() => {
  void loadReactions();
  window.addEventListener("auth-change", refresh);
});
onUnmounted(() => window.removeEventListener("auth-change", refresh));
</script>

<template>
  <section :aria-label="props.t.heading">
    <h2 class="font-headline text-2xl font-bold tracking-tight mb-4">{{ props.t.heading }}</h2>
    <div class="flex flex-wrap gap-2">
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
