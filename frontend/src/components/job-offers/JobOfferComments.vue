<script setup lang="ts">
import { ref } from "vue";
import type { Locale } from "../../i18n/utils";
import type { ApiErrorMap } from "../../lib/api-errors";
import { getAccessToken } from "../../lib/auth";
import { formatDateTime } from "../../lib/job-offers/format";
import type { JobOfferComment } from "../../lib/job-offers/types";
import { showRequestError, type RequestErrorStrings } from "../../lib/request-errors";
import type { UserInfo } from "../../lib/user-info";
import MaterialIcon from "../icons/MaterialIcon.vue";
import { materialSymbolPaths as paths } from "../icons/material-symbol-paths";

const props = defineProps<{
  apiUrl: string;
  offerId: string;
  comments: JobOfferComment[];
  /** Fresh display name/avatar per user id; falls back to the name stored on the comment. */
  users: Record<string, UserInfo>;
  lang: Locale;
  t: {
    title: string;
    empty: string;
    placeholder: string;
    send: string;
    errors: RequestErrorStrings;
    apiErrors: ApiErrorMap;
  };
}>();

const emit = defineEmits<{ posted: [] }>();

const draft = ref("");
const sending = ref(false);

function displayNameOf(comment: JobOfferComment): string {
  return props.users[comment.userId]?.displayName || comment.userName;
}

// Enter sends; only Shift+Enter is reserved for a newline, so Ctrl/Alt/Cmd+Enter send too.
function onComposerEnter(event: KeyboardEvent) {
  if (event.shiftKey) return;
  event.preventDefault();
  void send();
}

// Reused across retries of the current draft so a resend dedupes into one comment server-side.
let pendingCommentId: string | null = null;

async function send() {
  const content = draft.value.trim();
  if (!content || sending.value) return;

  sending.value = true;
  pendingCommentId ??= crypto.randomUUID();
  try {
    const token = await getAccessToken();
    if (!token) return;

    let response: Response;
    try {
      response = await fetch(`${props.apiUrl}/api/job-offers/${props.offerId}/comments`, {
        method: "POST",
        headers: { "Content-Type": "application/json", Authorization: `Bearer ${token}` },
        body: JSON.stringify({ content, commentId: pendingCommentId }),
      });
    } catch {
      await showRequestError(undefined, props.t.apiErrors, props.t.errors);
      return;
    }

    if (!response.ok) {
      await showRequestError(response, props.t.apiErrors, props.t.errors);
      return;
    }

    draft.value = "";
    pendingCommentId = null;
    emit("posted");
  } finally {
    sending.value = false;
  }
}
</script>

<template>
  <div class="mt-6 bg-surface-container-low rounded-2xl border border-outline-variant/10 p-8">
    <h3 class="font-headline text-lg font-bold text-on-surface mb-4">
      <MaterialIcon :d="paths.chat" class="size-[18px] inline align-middle mr-1" />
      {{ props.t.title }}
    </h3>

    <div id="comments-list" class="space-y-4 mb-4">
      <p v-if="props.comments.length === 0" class="text-sm text-on-surface-variant font-body">{{ props.t.empty }}</p>
      <div v-for="(comment, index) in props.comments" :key="index" class="flex gap-3">
        <img
          v-if="props.users[comment.userId]?.avatarUrl"
          :src="props.users[comment.userId].avatarUrl"
          alt=""
          class="w-8 h-8 rounded-full object-cover flex-shrink-0"
        />
        <div v-else class="w-8 h-8 rounded-full bg-primary-container flex items-center justify-center flex-shrink-0">
          <span class="text-xs font-bold text-on-primary-container">{{ displayNameOf(comment).charAt(0).toUpperCase() }}</span>
        </div>
        <div class="flex-1 min-w-0">
          <div class="flex items-baseline gap-2">
            <span class="text-sm font-label font-semibold text-on-surface">{{ displayNameOf(comment) }}</span>
            <span class="text-xs font-label text-on-surface-variant">{{ formatDateTime(comment.createdAt, lang) }}</span>
          </div>
          <p class="text-sm font-body text-on-surface mt-1 whitespace-pre-line">{{ comment.content }}</p>
        </div>
      </div>
    </div>

    <div class="flex gap-3">
      <textarea
        id="comment-input"
        v-model="draft"
        rows="2"
        :placeholder="props.t.placeholder"
        :aria-label="props.t.placeholder"
        class="flex-1 px-4 py-2 rounded-xl bg-surface-container border border-outline-variant/20 text-on-surface font-body text-sm placeholder:text-on-surface-variant/50 focus:outline-none focus:ring-2 focus:ring-primary/30 resize-y"
        @keydown.enter="onComposerEnter"
      ></textarea>
      <button
        id="send-comment"
        type="button"
        :disabled="sending"
        class="px-5 py-2 rounded-xl text-sm font-semibold font-label bg-primary text-on-primary hover:bg-primary/90 transition-colors cursor-pointer self-end disabled:opacity-40 disabled:cursor-not-allowed"
        @click="send"
      >
        {{ props.t.send }}
      </button>
    </div>
  </div>
</template>
