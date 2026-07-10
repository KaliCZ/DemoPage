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

// A reaction clicked while signed out fires automatically once the sign-in completes.
// sessionStorage lets the intent survive the Google OAuth redirect.
const PENDING_REACTION_KEY = "pending-reaction";
const PENDING_REACTION_TTL_MS = 10 * 60 * 1000;

function rememberPendingReaction(kind: ReactionKind) {
  try {
    sessionStorage.setItem(PENDING_REACTION_KEY, JSON.stringify({ slug: props.slug, kind, savedAtUtc: Date.now() }));
  } catch {
    // Storage unavailable — the visitor just clicks the reaction again after signing in.
  }
}

function takePendingReaction(): ReactionKind | null {
  try {
    const raw = sessionStorage.getItem(PENDING_REACTION_KEY);
    if (!raw) return null;
    sessionStorage.removeItem(PENDING_REACTION_KEY);
    const pending = JSON.parse(raw);
    const isFresh = Date.now() - pending.savedAtUtc <= PENDING_REACTION_TTL_MS;
    return pending.slug === props.slug && isFresh ? (pending.kind as ReactionKind) : null;
  } catch {
    return null;
  }
}

function clearPendingReaction() {
  try {
    sessionStorage.removeItem(PENDING_REACTION_KEY);
  } catch {
    // Nothing to clear when storage is unavailable.
  }
}

// Bumped when a toggle lands so a slower in-flight load can't overwrite fresher state.
let loadSequence = 0;

async function loadReactions() {
  const sequence = ++loadSequence;
  try {
    const token = await authToken();
    const res = await fetch(`${props.apiUrl}/api/blog/${props.slug}/reactions`, {
      headers: token ? { Authorization: `Bearer ${token}` } : {},
    });
    if (res.ok) {
      const data = await res.json();
      if (sequence === loadSequence) applyState(data);
    }
  } catch {
    // Counts are decorative on load — a network hiccup just leaves them at zero.
  }
}

async function toggle(kind: ReactionKind) {
  if (busy.value) return;

  const token = await authToken();
  if (!token) {
    rememberPendingReaction(kind);
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
  const user = (event as CustomEvent).detail?.user;
  const pending = user ? takePendingReaction() : null;
  await loadReactions();
  // Toggle only when not already reacted — the visitor may have reacted earlier on another device.
  if (pending && !mine.value.has(pending)) void toggle(pending);
};

// Dismissing the dialog without signing in withdraws the stored reaction — a sign-in
// minutes later (e.g. from the navbar) shouldn't fire it unexpectedly.
const onAuthDialogClose = async () => {
  if (!(await authToken())) clearPendingReaction();
};

onMounted(() => {
  void loadReactions();
  window.addEventListener("auth-change", onAuthChange);
  document.getElementById("auth-dialog")?.addEventListener("close", onAuthDialogClose);
});
onUnmounted(() => {
  window.removeEventListener("auth-change", onAuthChange);
  document.getElementById("auth-dialog")?.removeEventListener("close", onAuthDialogClose);
});
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
