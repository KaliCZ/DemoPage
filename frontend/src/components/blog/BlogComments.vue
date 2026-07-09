<script setup lang="ts">
import { computed, onMounted, onUnmounted, ref } from "vue";
import { getApiError } from "../../lib/api-errors";

interface CommentDto {
  id: string;
  parentCommentId: string | null;
  userId: string | null;
  authorDisplayName: string | null;
  authorAvatarUrl: string | null;
  content: string | null;
  postedAt: string;
  isDeleted: boolean;
}

const props = defineProps<{
  apiUrl: string;
  slug: string;
  lang: string;
  t: {
    heading: string;
    empty: string;
    placeholder: string;
    submit: string;
    submitting: string;
    reply: string;
    replyingTo: string;
    cancel: string;
    delete: string;
    deleteConfirm: string;
    deleteYes: string;
    deleteNo: string;
    deleted: string;
    loadError: string;
    postError: string;
    deleteError: string;
    rateLimited: string;
    errors: {
      ParentCommentNotFound: string;
      ParentCommentDeleted: string;
    };
  };
}>();

const comments = ref<CommentDto[]>([]);
const loaded = ref(false);
const loadFailed = ref(false);

const viewerId = ref<string | null>(null);
const viewerIsAdmin = ref(false);
const viewerChecked = ref(false);

const draft = ref("");
const replyTo = ref<CommentDto | null>(null);
const submitting = ref(false);
const confirmingDeleteId = ref<string | null>(null);
const composer = ref<HTMLTextAreaElement | null>(null);

/** Depth-first flattening of the reply tree — keeps the template a single v-for. */
const thread = computed(() => {
  const byParent = new Map<string | null, CommentDto[]>();
  for (const comment of comments.value) {
    const key = comment.parentCommentId ?? null;
    if (!byParent.has(key)) byParent.set(key, []);
    byParent.get(key)!.push(comment);
  }

  const flat: { comment: CommentDto; depth: number }[] = [];
  const walk = (parentId: string | null, depth: number) => {
    for (const comment of byParent.get(parentId) ?? []) {
      flat.push({ comment, depth });
      walk(comment.id, depth + 1);
    }
  };
  walk(null, 0);
  return flat;
});

function formatDate(iso: string): string {
  return new Date(iso).toLocaleString(props.lang === "cs" ? "cs-CZ" : "en-US", {
    year: "numeric",
    month: "short",
    day: "numeric",
    hour: "2-digit",
    minute: "2-digit",
  });
}

function initialOf(name: string | null): string {
  return (name?.charAt(0) ?? "?").toUpperCase();
}

function canDelete(comment: CommentDto): boolean {
  if (comment.isDeleted || !viewerId.value) return false;
  return comment.userId === viewerId.value || viewerIsAdmin.value;
}

async function authToken(): Promise<string | null> {
  return ((await (window as any).__getAccessToken?.()) as string | null) ?? null;
}

async function loadComments() {
  try {
    const res = await fetch(`${props.apiUrl}/api/blog/${props.slug}/comments`);
    if (!res.ok) throw new Error(`Comments load failed with ${res.status}`);
    const data = await res.json();
    comments.value = data.comments;
    loadFailed.value = false;
  } catch {
    loadFailed.value = true;
  } finally {
    loaded.value = true;
  }
}

async function refreshViewer() {
  const user = await (window as any).__getUser?.();
  viewerId.value = user?.id ?? null;
  viewerIsAdmin.value = Array.isArray(user?.app_metadata?.roles) && user.app_metadata.roles.includes("admin");
  viewerChecked.value = true;
}

function openSignIn() {
  (window as any).__openAuthDialog?.();
}

function startReply(comment: CommentDto) {
  replyTo.value = comment;
  composer.value?.focus();
}

// Reused across retries of the current draft so a resend dedupes into one comment server-side.
let pendingCommentId: string | null = null;

async function submit() {
  const content = draft.value.trim();
  if (!content || submitting.value) return;

  // Latch before the first await so a rapid second click can't race in behind
  // the token fetch and post the comment twice.
  submitting.value = true;
  pendingCommentId ??= crypto.randomUUID();
  try {
    const token = await authToken();
    if (!token) {
      openSignIn();
      return;
    }

    const res = await fetch(`${props.apiUrl}/api/blog/${props.slug}/comments`, {
      method: "POST",
      headers: { "Content-Type": "application/json", Authorization: `Bearer ${token}` },
      body: JSON.stringify({ commentId: pendingCommentId, content, parentCommentId: replyTo.value?.id ?? null }),
    });
    if (res.status === 429) {
      (window as any).__showSnackbar?.(props.t.rateLimited, "error", 8000);
      return;
    }
    if (!res.ok) {
      // ProblemDetails error codes map onto i18n keys (same convention as the
      // hire-me form); anything unmapped falls back to the generic message.
      const apiError = await getApiError(res, {
        parentCommentId: {
          ParentCommentNotFound: props.t.errors.ParentCommentNotFound,
          ParentCommentDeleted: props.t.errors.ParentCommentDeleted,
        },
      });
      (window as any).__showSnackbar?.(apiError?.message ?? props.t.postError, "error", 8000);
      return;
    }
    const created: CommentDto = await res.json();
    comments.value = [...comments.value, created];
    draft.value = "";
    replyTo.value = null;
    pendingCommentId = null;
  } catch {
    (window as any).__showSnackbar?.(props.t.postError, "error", 8000);
  } finally {
    submitting.value = false;
  }
}

async function removeComment(comment: CommentDto) {
  confirmingDeleteId.value = null;

  const token = await authToken();
  if (!token) {
    openSignIn();
    return;
  }

  // Optimistic tombstone; restored if the server rejects the delete.
  const index = comments.value.findIndex((c) => c.id === comment.id);
  if (index === -1) return;
  const original = comments.value[index];
  const tombstoned = [...comments.value];
  tombstoned[index] = {
    ...original,
    isDeleted: true,
    content: null,
    authorDisplayName: null,
    authorAvatarUrl: null,
    userId: null,
  };
  comments.value = tombstoned;

  try {
    const res = await fetch(`${props.apiUrl}/api/blog/${props.slug}/comments/${comment.id}`, {
      method: "DELETE",
      headers: { Authorization: `Bearer ${token}` },
    });
    if (!res.ok) throw new Error(`Comment delete failed with ${res.status}`);
  } catch {
    const reverted = [...comments.value];
    reverted[index] = original;
    comments.value = reverted;
    (window as any).__showSnackbar?.(props.t.deleteError, "error", 8000);
  }
}

const onAuthChange = () => void refreshViewer();

onMounted(() => {
  void loadComments();
  void refreshViewer();
  window.addEventListener("auth-change", onAuthChange);
});
onUnmounted(() => window.removeEventListener("auth-change", onAuthChange));
</script>

<template>
  <section :aria-label="props.t.heading">
    <h2 class="font-headline text-2xl font-bold tracking-tight mb-6">{{ props.t.heading }}</h2>

    <!-- Composer -->
    <div v-if="viewerChecked && viewerId" class="mb-10">
      <div v-if="replyTo" class="mb-2 flex items-start gap-2 rounded-lg border-l-4 border-primary bg-surface-container-low py-2 pr-2 pl-3">
        <div class="min-w-0 flex-1">
          <p class="font-label text-xs font-semibold text-primary">{{ props.t.replyingTo }} {{ replyTo.authorDisplayName }}</p>
          <p class="mt-0.5 line-clamp-3 font-body text-sm text-on-surface-variant break-words whitespace-pre-line">
            {{ replyTo.content }}
          </p>
        </div>
        <button
          type="button"
          :aria-label="props.t.cancel"
          class="shrink-0 rounded-md p-1 text-on-surface-variant transition-colors hover:bg-surface-container-high hover:text-on-surface cursor-pointer"
          @click="replyTo = null"
        >
          <svg viewBox="0 0 24 24" class="size-4" fill="currentColor" aria-hidden="true">
            <path d="M6.4 19 5 17.6l5.6-5.6L5 6.4 6.4 5l5.6 5.6L17.6 5 19 6.4 13.4 12l5.6 5.6-1.4 1.4-5.6-5.6z" />
          </svg>
        </button>
      </div>
      <textarea
        ref="composer"
        v-model="draft"
        :placeholder="props.t.placeholder"
        :aria-label="props.t.placeholder"
        rows="3"
        maxlength="5000"
        class="w-full rounded-xl border border-outline-variant/30 bg-surface-container-lowest px-4 py-3 font-body text-sm text-on-surface placeholder:text-on-surface-variant/60 focus:border-primary/50 focus:outline-none resize-y"
        @keydown.enter.ctrl.prevent="submit"
        @keydown.enter.meta.prevent="submit"
      ></textarea>
      <div class="flex justify-end mt-2">
        <button
          type="button"
          :disabled="submitting || !draft.trim()"
          class="px-4 py-1.5 rounded-lg text-sm font-semibold font-label bg-primary text-on-primary hover:bg-primary/90 transition-colors cursor-pointer disabled:opacity-40 disabled:cursor-not-allowed"
          @click="submit"
        >
          {{ submitting ? props.t.submitting : props.t.submit }}
        </button>
      </div>
    </div>

    <!-- Thread -->
    <p v-if="loadFailed" class="font-body text-sm text-error">{{ props.t.loadError }}</p>
    <p v-else-if="loaded && thread.length === 0" class="font-body text-sm text-on-surface-variant">
      {{ props.t.empty }}
    </p>
    <ul v-else class="space-y-5" role="list">
      <li
        v-for="{ comment, depth } in thread"
        :key="comment.id"
        :style="{ marginLeft: `${Math.min(depth, 4) * 1.25}rem` }"
        :class="depth > 0 ? 'border-l-2 border-outline-variant/30 pl-4' : ''"
      >
        <div class="flex items-start gap-3">
          <template v-if="!comment.isDeleted">
            <img
              v-if="comment.authorAvatarUrl"
              :src="comment.authorAvatarUrl"
              :alt="comment.authorDisplayName ?? ''"
              class="w-8 h-8 rounded-full shrink-0"
              loading="lazy"
            />
            <span
              v-else
              class="w-8 h-8 rounded-full bg-primary text-on-primary text-xs font-bold flex items-center justify-center shrink-0"
              aria-hidden="true"
            >
              {{ initialOf(comment.authorDisplayName) }}
            </span>
          </template>
          <span v-else class="w-8 h-8 rounded-full bg-surface-container-high shrink-0" aria-hidden="true"></span>

          <div class="flex-1 min-w-0">
            <div class="flex items-baseline gap-2 flex-wrap">
              <span v-if="!comment.isDeleted" class="font-label text-sm font-semibold text-on-surface">
                {{ comment.authorDisplayName }}
              </span>
              <span v-else class="font-label text-sm italic text-on-surface-variant">{{ props.t.deleted }}</span>
              <time :datetime="comment.postedAt" class="text-xs font-label text-on-surface-variant">
                {{ formatDate(comment.postedAt) }}
              </time>
            </div>

            <p
              v-if="!comment.isDeleted"
              class="font-body text-sm text-on-surface-variant leading-relaxed mt-1 whitespace-pre-line break-words"
            >
              {{ comment.content }}
            </p>
            <p v-else class="font-body text-sm italic text-on-surface-variant/60 mt-1">{{ props.t.deleted }}</p>

            <div v-if="!comment.isDeleted" class="flex items-center gap-4 mt-2">
              <button
                v-if="viewerId"
                type="button"
                class="text-xs font-label text-on-surface-variant hover:text-primary transition-colors cursor-pointer"
                @click="startReply(comment)"
              >
                {{ props.t.reply }}
              </button>
              <template v-if="canDelete(comment)">
                <button
                  v-if="confirmingDeleteId !== comment.id"
                  type="button"
                  class="text-xs font-label text-on-surface-variant hover:text-error transition-colors cursor-pointer"
                  @click="confirmingDeleteId = comment.id"
                >
                  {{ props.t.delete }}
                </button>
                <span v-else class="flex items-center gap-2 text-xs font-label">
                  <span class="text-on-surface-variant">{{ props.t.deleteConfirm }}</span>
                  <button type="button" class="text-error hover:underline cursor-pointer" @click="removeComment(comment)">
                    {{ props.t.deleteYes }}
                  </button>
                  <button type="button" class="text-on-surface-variant hover:underline cursor-pointer" @click="confirmingDeleteId = null">
                    {{ props.t.deleteNo }}
                  </button>
                </span>
              </template>
            </div>
          </div>
        </div>
      </li>
    </ul>
  </section>
</template>
