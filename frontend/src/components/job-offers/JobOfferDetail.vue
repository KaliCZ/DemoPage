<script setup lang="ts">
import { computed, onMounted, onUnmounted, ref } from "vue";
import type { Locale } from "../../i18n/utils";
import type { ApiErrorMap } from "../../lib/api-errors";
import { getAccessToken, getCurrentUser, userHasAdminRole } from "../../lib/auth";
import { formatDateTime } from "../../lib/job-offers/format";
import type {
  JobOfferComment,
  JobOfferDetailDto,
  JobOfferEditBody,
  JobOfferHistoryEntry,
  JobOfferStatus,
} from "../../lib/job-offers/types";
import { showRequestError, type RequestErrorStrings } from "../../lib/request-errors";
import { fetchUserInfo, type UserInfo } from "../../lib/user-info";
import MaterialIcon from "../icons/MaterialIcon.vue";
import { materialSymbolPaths as paths } from "../icons/material-symbol-paths";
import JobOfferAttachments from "./JobOfferAttachments.vue";
import JobOfferComments from "./JobOfferComments.vue";
import JobOfferEditForm from "./JobOfferEditForm.vue";
import JobOfferHistory from "./JobOfferHistory.vue";
import JobOfferStatusBadge from "./JobOfferStatusBadge.vue";

const props = defineProps<{
  apiUrl: string;
  isAdmin: boolean;
  /** Locale-prefixed path of the list page, for redirects after cancel / missing id. */
  listPath: string;
  lang: Locale;
  t: {
    statusLabels: Record<JobOfferStatus, string>;
    remote: string;
    fields: {
      contact: string;
      location: string;
      salary: string;
      submittedOn: string;
      description: string;
      additionalNotes: string;
      /** Admin only — footer naming the submitting account. */
      submittedBy?: string;
    };
    attachments: { title: string; download: string; downloadFailed: string };
    comments: { title: string; empty: string; placeholder: string; send: string };
    history: {
      title: string;
      empty: string;
      fieldLabels: Record<string, string>;
      changed: string;
      before: string;
      after: string;
      yes: string;
      no: string;
    };
    /** User mode only. */
    userActions?: { edit: string; cancel: string; cancelConfirm: string; cancelReason: string; cancelButton: string };
    /** User mode only. */
    editForm?: {
      labels: Record<string, string>;
      save: string;
      discard: string;
      clearNotAllowed: string;
      validation: { required: string; emailInvalid: string; descriptionMinLength: string; maxLength: string };
    };
    /** Admin mode only. */
    admin?: { updateStatus: string; notesPlaceholder: string; save: string };
    errors: RequestErrorStrings;
    apiErrors: ApiErrorMap;
  };
}>();

const offerId = ref<string | null>(null);
const offer = ref<JobOfferDetailDto | null>(null);
const historyEntries = ref<JobOfferHistoryEntry[]>([]);
const commentsList = ref<JobOfferComment[]>([]);
const users = ref<Record<string, UserInfo>>({});
const loaded = ref(false);
const loadErrorMessage = ref("");

const editing = ref(false);
const editSubmitError = ref("");
const cancelConfirming = ref(false);
const cancelReason = ref("");
const adminStatus = ref<JobOfferStatus>("Submitted");
const adminNotes = ref("");

const canEdit = computed(() => !props.isAdmin && offer.value?.status === "Submitted");
const canCancel = computed(() => !props.isAdmin && (offer.value?.status === "Submitted" || offer.value?.status === "InReview"));

async function authHeaders(): Promise<Record<string, string> | null> {
  const token = await getAccessToken();
  return token ? { Authorization: `Bearer ${token}` } : null;
}

async function refreshUsers() {
  const ids = [...historyEntries.value.map((entry) => entry.actorUserId), ...commentsList.value.map((c) => c.userId)];
  users.value = await fetchUserInfo(props.apiUrl, ids);
}

async function load() {
  const headers = await authHeaders();
  if (!headers || !offerId.value) return;

  const base = `${props.apiUrl}/api/job-offers/${offerId.value}`;
  let detailRes: Response, historyRes: Response, commentsRes: Response;
  try {
    [detailRes, historyRes, commentsRes] = await Promise.all([
      fetch(base, { headers }),
      fetch(`${base}/history`, { headers }),
      fetch(`${base}/comments`, { headers }),
    ]);
  } catch {
    loaded.value = true;
    loadErrorMessage.value = await showRequestError(undefined, props.t.apiErrors, props.t.errors);
    return;
  }

  loaded.value = true;
  if (!detailRes.ok) {
    loadErrorMessage.value = await showRequestError(detailRes, props.t.apiErrors, props.t.errors);
    return;
  }

  loadErrorMessage.value = "";
  offer.value = await detailRes.json();
  historyEntries.value = historyRes.ok ? (await historyRes.json()).entries : [];
  commentsList.value = commentsRes.ok ? (await commentsRes.json()).comments : [];
  adminStatus.value = offer.value!.status;
  adminNotes.value = "";
  await refreshUsers();
}

/** Comments and history change together (a comment is also a history event). */
async function refreshDiscussion() {
  const headers = await authHeaders();
  if (!headers || !offerId.value) return;

  const base = `${props.apiUrl}/api/job-offers/${offerId.value}`;
  try {
    const [historyRes, commentsRes] = await Promise.all([fetch(`${base}/history`, { headers }), fetch(`${base}/comments`, { headers })]);
    if (historyRes.ok) historyEntries.value = (await historyRes.json()).entries;
    if (commentsRes.ok) commentsList.value = (await commentsRes.json()).comments;
    await refreshUsers();
  } catch {
    /* the mutation itself succeeded — the view is just stale until the next load */
  }
}

function startEdit() {
  editSubmitError.value = "";
  editing.value = true;
}

async function saveEdit(body: JobOfferEditBody) {
  const headers = await authHeaders();
  if (!headers || !offerId.value) return;

  editSubmitError.value = "";
  let response: Response;
  try {
    response = await fetch(`${props.apiUrl}/api/job-offers/${offerId.value}`, {
      method: "PATCH",
      headers: { ...headers, "Content-Type": "application/json" },
      body: JSON.stringify(body),
    });
  } catch {
    editSubmitError.value = await showRequestError(undefined, props.t.apiErrors, props.t.errors);
    return;
  }

  if (!response.ok) {
    editSubmitError.value = await showRequestError(response, props.t.apiErrors, props.t.errors);
    return;
  }

  (window as any).observability?.track("job-offer-edited", { offerId: offerId.value });
  editing.value = false;
  await load();
}

async function confirmCancel() {
  const headers = await authHeaders();
  if (!headers || !offerId.value) return;

  let response: Response;
  try {
    response = await fetch(`${props.apiUrl}/api/job-offers/${offerId.value}/cancel`, {
      method: "POST",
      headers: { ...headers, "Content-Type": "application/json" },
      body: JSON.stringify({ reason: cancelReason.value || null }),
    });
  } catch {
    await showRequestError(undefined, props.t.apiErrors, props.t.errors);
    return;
  }

  if (!response.ok) {
    await showRequestError(response, props.t.apiErrors, props.t.errors);
    return;
  }

  (window as any).observability?.track("job-offer-cancelled", { offerId: offerId.value });
  window.location.assign(props.listPath);
}

async function saveStatus() {
  const headers = await authHeaders();
  if (!headers || !offerId.value) return;

  let response: Response;
  try {
    response = await fetch(`${props.apiUrl}/api/job-offers/${offerId.value}/status`, {
      method: "PATCH",
      headers: { ...headers, "Content-Type": "application/json" },
      body: JSON.stringify({ status: adminStatus.value, adminNotes: adminNotes.value || null }),
    });
  } catch {
    await showRequestError(undefined, props.t.apiErrors, props.t.errors);
    return;
  }

  if (!response.ok) {
    await showRequestError(response, props.t.apiErrors, props.t.errors);
    return;
  }

  await load();
}

// The page's auth-gate script controls section visibility; the island only has
// to (re)load data when a qualifying user appears, and not reload for the same one.
let loadedForUserId: string | null = null;

function applyUser(user: any) {
  if (!user || (props.isAdmin && !userHasAdminRole(user))) {
    loadedForUserId = null;
    return;
  }
  if (loadedForUserId === user.id) return;
  loadedForUserId = user.id;
  void load();
}

const onAuthChange = (event: Event) => applyUser((event as CustomEvent).detail?.user ?? null);

onMounted(async () => {
  const id = new URLSearchParams(window.location.search).get("id");
  if (!id) {
    window.location.replace(props.listPath);
    return;
  }
  offerId.value = id;
  window.addEventListener("auth-change", onAuthChange);
  // auth-change may already have fired before hydration — pull the current user.
  applyUser(await getCurrentUser());
});
onUnmounted(() => window.removeEventListener("auth-change", onAuthChange));
</script>

<template>
  <div>
    <!-- Load spinner -->
    <div v-if="!loaded" class="text-center py-12">
      <MaterialIcon :d="paths.progressActivity" class="size-[30px] text-on-surface-variant animate-spin inline-block" />
    </div>

    <p v-else-if="loadErrorMessage" class="text-sm text-on-surface-variant font-body">{{ loadErrorMessage }}</p>

    <template v-else-if="offer">
      <!-- Detail card -->
      <div v-show="!editing" id="offer-detail" class="bg-surface-container-low rounded-2xl border border-outline-variant/10 p-8 md:p-12">
        <div class="flex flex-col sm:flex-row sm:items-start justify-between gap-4 mb-8">
          <div>
            <h2 class="font-headline text-2xl font-bold text-on-surface mb-1">{{ offer.jobTitle }}</h2>
            <p class="text-on-surface-variant font-body">{{ offer.companyName }}</p>
          </div>
          <JobOfferStatusBadge :status="offer.status" :labels="props.t.statusLabels" class="px-4 py-1.5 text-sm self-start" />
        </div>

        <div class="grid grid-cols-1 sm:grid-cols-2 gap-6 mb-8">
          <div>
            <p class="text-xs font-label text-on-surface-variant mb-1">{{ props.t.fields.contact }}</p>
            <p class="text-sm font-body text-on-surface">{{ offer.contactName }}</p>
            <p class="text-sm font-body text-on-surface-variant">{{ offer.contactEmail }}</p>
          </div>
          <div>
            <p class="text-xs font-label text-on-surface-variant mb-1">{{ props.t.fields.location }}</p>
            <p class="text-sm font-body text-on-surface">
              {{ offer.location || "—" }}
              <span v-if="offer.isRemote" class="text-tertiary">({{ props.t.remote }})</span>
            </p>
          </div>
          <div v-if="offer.salaryRange">
            <p class="text-xs font-label text-on-surface-variant mb-1">{{ props.t.fields.salary }}</p>
            <p class="text-sm font-body text-on-surface">{{ offer.salaryRange }}</p>
          </div>
          <div>
            <p class="text-xs font-label text-on-surface-variant mb-1">{{ props.t.fields.submittedOn }}</p>
            <p class="text-sm font-body text-on-surface">{{ formatDateTime(offer.createdAt, lang) }}</p>
          </div>
        </div>

        <div class="mb-6">
          <p class="text-xs font-label text-on-surface-variant mb-2">{{ props.t.fields.description }}</p>
          <p class="text-sm font-body text-on-surface whitespace-pre-line">{{ offer.description }}</p>
        </div>

        <div v-if="offer.additionalNotes" class="mb-6">
          <p class="text-xs font-label text-on-surface-variant mb-2">{{ props.t.fields.additionalNotes }}</p>
          <p class="text-sm font-body text-on-surface whitespace-pre-line">{{ offer.additionalNotes }}</p>
        </div>

        <JobOfferAttachments
          v-if="offer.attachments && offer.attachments.length > 0"
          :api-url="apiUrl"
          :offer-id="offer.id"
          :attachments="offer.attachments"
          :t="{ ...props.t.attachments, errors: props.t.errors }"
        />

        <div v-if="offer.userEmail && props.t.fields.submittedBy" class="pt-4 border-t border-outline-variant/10">
          <p class="text-xs font-label text-on-surface-variant">{{ props.t.fields.submittedBy }}: {{ offer.userEmail }}</p>
        </div>
      </div>

      <!-- Edit form (user mode) — created fresh on each edit so it starts from the current offer -->
      <JobOfferEditForm
        v-if="editing && props.t.editForm"
        :offer="offer"
        :submit-error="editSubmitError"
        :t="props.t.editForm"
        @save="saveEdit"
        @discard="editing = false"
      />

      <!-- User actions -->
      <div v-if="!editing && (canEdit || canCancel) && props.t.userActions" id="user-actions" class="mt-6 flex gap-4">
        <button
          v-if="canEdit"
          id="edit-btn"
          type="button"
          class="flex items-center gap-2 px-6 py-2 rounded-xl text-sm font-semibold font-label bg-primary text-on-primary hover:bg-primary/90 transition-colors cursor-pointer"
          @click="startEdit"
        >
          <MaterialIcon :d="paths.edit" class="size-[18px]" />
          {{ props.t.userActions.edit }}
        </button>
        <button
          v-if="canCancel"
          id="cancel-btn"
          type="button"
          class="flex items-center gap-2 px-6 py-2 rounded-xl text-sm font-semibold font-label bg-error text-on-primary hover:bg-error/90 transition-colors cursor-pointer"
          @click="cancelConfirming = true"
        >
          <MaterialIcon :d="paths.cancel" class="size-[18px]" />
          {{ props.t.userActions.cancel }}
        </button>
      </div>

      <!-- Cancel confirmation -->
      <div
        v-if="cancelConfirming && !editing && props.t.userActions"
        id="cancel-controls"
        class="mt-6 bg-surface-container-low rounded-2xl border border-outline-variant/10 p-8"
      >
        <h3 class="font-headline text-lg font-bold text-on-surface mb-4">{{ props.t.userActions.cancel }}</h3>
        <p class="text-sm text-on-surface-variant font-body mb-4">{{ props.t.userActions.cancelConfirm }}</p>
        <textarea
          id="cancel-reason"
          v-model="cancelReason"
          rows="2"
          :placeholder="props.t.userActions.cancelReason"
          class="w-full px-4 py-2 rounded-xl bg-surface-container border border-outline-variant/20 text-on-surface font-body text-sm placeholder:text-on-surface-variant/50 focus:outline-none focus:ring-2 focus:ring-primary/30 resize-y mb-4"
          @keydown.enter.ctrl.prevent="confirmCancel"
          @keydown.enter.meta.prevent="confirmCancel"
        ></textarea>
        <button
          id="confirm-cancel"
          type="button"
          class="px-6 py-2 rounded-xl text-sm font-semibold font-label bg-error text-on-primary hover:bg-error/90 transition-colors cursor-pointer"
          @click="confirmCancel"
        >
          {{ props.t.userActions.cancelButton }}
        </button>
      </div>

      <!-- Admin status controls -->
      <div
        v-if="isAdmin && props.t.admin && offer.status !== 'Cancelled'"
        id="admin-controls"
        class="mt-6 bg-surface-container-low rounded-2xl border border-outline-variant/10 p-8"
      >
        <h3 class="font-headline text-lg font-bold text-on-surface mb-4">{{ props.t.admin.updateStatus }}</h3>
        <div class="flex flex-col sm:flex-row gap-4">
          <select
            id="status-select"
            v-model="adminStatus"
            class="px-4 py-2 rounded-xl bg-surface-container border border-outline-variant/20 text-on-surface font-body text-sm focus:outline-none focus:ring-2 focus:ring-primary/30"
          >
            <option value="Submitted">{{ props.t.statusLabels.Submitted }}</option>
            <option value="InReview">{{ props.t.statusLabels.InReview }}</option>
            <option value="LetsTalk">{{ props.t.statusLabels.LetsTalk }}</option>
            <option value="Declined">{{ props.t.statusLabels.Declined }}</option>
          </select>
          <textarea
            id="admin-notes-input"
            v-model="adminNotes"
            rows="2"
            :placeholder="props.t.admin.notesPlaceholder"
            class="flex-1 px-4 py-2 rounded-xl bg-surface-container border border-outline-variant/20 text-on-surface font-body text-sm placeholder:text-on-surface-variant/50 focus:outline-none focus:ring-2 focus:ring-primary/30 resize-y"
            @keydown.enter.ctrl.prevent="saveStatus"
            @keydown.enter.meta.prevent="saveStatus"
          ></textarea>
          <button
            id="save-status"
            type="button"
            class="px-6 py-2 rounded-xl text-sm font-semibold font-label bg-primary text-on-primary hover:bg-primary/90 transition-colors cursor-pointer self-start"
            @click="saveStatus"
          >
            {{ props.t.admin.save }}
          </button>
        </div>
      </div>

      <JobOfferComments
        :api-url="apiUrl"
        :offer-id="offer.id"
        :comments="commentsList"
        :users="users"
        :lang="lang"
        :t="{ ...props.t.comments, errors: props.t.errors, apiErrors: props.t.apiErrors }"
        @posted="refreshDiscussion"
      />

      <JobOfferHistory :entries="historyEntries" :users="users" :lang="lang" :t="props.t.history" />
    </template>
  </div>
</template>
