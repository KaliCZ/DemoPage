<script setup lang="ts">
import { computed, onMounted, onUnmounted, ref, watch } from "vue";
import type { Locale } from "../../i18n/utils";
import type { ApiErrorMap } from "../../lib/api-errors";
import { getAccessToken, getCurrentUser, userHasAdminRole } from "../../lib/auth";
import { formatDate } from "../../lib/job-offers/format";
import type { JobOfferListResponse, JobOfferStatus, JobOfferSummary, PaginationInfo } from "../../lib/job-offers/types";
import { showRequestError, type RequestErrorStrings } from "../../lib/request-errors";
import MaterialIcon from "../icons/MaterialIcon.vue";
import { materialSymbolPaths as paths } from "../icons/material-symbol-paths";
import JobOfferStatusBadge from "./JobOfferStatusBadge.vue";

const props = defineProps<{
  apiUrl: string;
  isAdmin: boolean;
  /** Locale-prefixed path of the detail page; the offer id goes in ?id=. */
  detailPath: string;
  /** Empty-state CTA target — user page only. */
  hireMeUrl?: string;
  lang: Locale;
  t: {
    filterByStatus: string;
    statusLabels: Record<JobOfferStatus, string>;
    remote: string;
    emptyTitle: string;
    emptyDescription: string;
    emptyCta?: string;
    paginationPrev: string;
    paginationNext: string;
    /** Contains `{current}` and `{total}` placeholders. */
    paginationPage: string;
    errors: RequestErrorStrings;
    apiErrors: ApiErrorMap;
  };
}>();

const PAGE_SIZE = 10;
const ALL_STATUSES: JobOfferStatus[] = ["Submitted", "InReview", "LetsTalk", "Declined", "Cancelled"];

const offers = ref<JobOfferSummary[]>([]);
const pagination = ref<PaginationInfo | null>(null);
const loaded = ref(false);
const page = ref(1);
const activeStatuses = ref<JobOfferStatus[]>([]);

const filterOpen = ref(false);
const filterToggle = ref<HTMLElement | null>(null);
const filterMenu = ref<HTMLElement | null>(null);

const filterLabel = computed(() =>
  activeStatuses.value.length > 0
    ? activeStatuses.value.map((status) => props.t.statusLabels[status] ?? status).join(", ")
    : props.t.filterByStatus,
);

const pageIndicator = computed(() => {
  const info = pagination.value;
  if (!info) return "";
  return props.t.paginationPage.replace("{current}", String(info.page)).replace("{total}", String(info.pageCount));
});

function detailHref(offer: JobOfferSummary): string {
  return `${props.detailPath}?id=${encodeURIComponent(offer.id)}`;
}

async function loadOffers() {
  const token = await getAccessToken();
  if (!token) return;

  const params = new URLSearchParams({ page: String(page.value), pageSize: String(PAGE_SIZE) });
  for (const status of activeStatuses.value) params.append("status", status);
  const endpoint = props.isAdmin ? "/api/job-offers" : "/api/job-offers/mine";

  let response: Response;
  try {
    response = await fetch(`${props.apiUrl}${endpoint}?${params}`, {
      headers: { Authorization: `Bearer ${token}` },
    });
  } catch {
    loaded.value = true;
    await showRequestError(undefined, props.t.apiErrors, props.t.errors);
    return;
  }

  loaded.value = true;
  if (!response.ok) {
    await showRequestError(response, props.t.apiErrors, props.t.errors);
    return;
  }

  const data: JobOfferListResponse = await response.json();
  offers.value = data.items;
  pagination.value = data.pagination;
}

// A filter change always restarts from page 1.
watch(activeStatuses, () => {
  page.value = 1;
  void loadOffers();
});

async function previousPage() {
  if (page.value > 1) {
    page.value--;
    await loadOffers();
  }
}

async function nextPage() {
  page.value++;
  await loadOffers();
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
  void loadOffers();
}

const onAuthChange = (event: Event) => applyUser((event as CustomEvent).detail?.user ?? null);
const onDocumentClick = (event: MouseEvent) => {
  const target = event.target as Node;
  if (!filterToggle.value?.contains(target) && !filterMenu.value?.contains(target)) filterOpen.value = false;
};

onMounted(async () => {
  window.addEventListener("auth-change", onAuthChange);
  document.addEventListener("click", onDocumentClick);
  // auth-change may already have fired before hydration — pull the current user.
  applyUser(await getCurrentUser());
});
onUnmounted(() => {
  window.removeEventListener("auth-change", onAuthChange);
  document.removeEventListener("click", onDocumentClick);
});
</script>

<template>
  <div>
    <!-- Status filter dropdown -->
    <div class="relative mb-6">
      <button
        id="status-filter-toggle"
        ref="filterToggle"
        type="button"
        class="flex items-center gap-2 px-4 py-2.5 rounded-xl bg-surface-container-low border border-outline-variant/20 text-sm font-label font-semibold text-on-surface hover:border-primary/30 transition-colors cursor-pointer"
        aria-haspopup="listbox"
        :aria-expanded="filterOpen"
        @click="filterOpen = !filterOpen"
      >
        <MaterialIcon :d="paths.filterList" class="size-[18px]" />
        <span>{{ filterLabel }}</span>
        <span v-if="activeStatuses.length > 0" class="px-1.5 py-0.5 rounded-full text-xs bg-primary text-on-primary">
          {{ activeStatuses.length }}
        </span>
        <MaterialIcon :d="paths.expandMore" class="size-[18px] ml-1" />
      </button>
      <div
        v-show="filterOpen"
        ref="filterMenu"
        class="absolute left-0 top-full mt-1 z-10 min-w-[200px] bg-surface-container-low rounded-xl border border-outline-variant/20 shadow-lg py-1"
        role="listbox"
        aria-multiselectable="true"
        :aria-label="props.t.filterByStatus"
      >
        <label
          v-for="status in ALL_STATUSES"
          :key="status"
          class="flex items-center gap-3 px-4 py-2.5 hover:bg-surface-container-high transition-colors cursor-pointer"
          role="option"
        >
          <input
            v-model="activeStatuses"
            type="checkbox"
            :value="status"
            class="status-checkbox w-4 h-4 rounded border-outline-variant/30 text-primary focus:ring-primary/30 accent-primary"
          />
          <span class="text-sm font-label text-on-surface">{{ props.t.statusLabels[status] }}</span>
        </label>
      </div>
    </div>

    <!-- Initial load spinner -->
    <div v-if="!loaded" class="text-center py-12">
      <MaterialIcon :d="paths.progressActivity" class="size-[30px] text-on-surface-variant animate-spin inline-block" />
    </div>

    <!-- Empty state -->
    <div
      v-else-if="pagination && pagination.totalCount === 0"
      id="offers-empty"
      class="bg-surface-container-low rounded-2xl border border-outline-variant/10 p-8 md:p-12 text-center"
    >
      <MaterialIcon :d="paths.inbox" class="size-12 text-on-surface-variant/50 mb-4 mx-auto block" />
      <h2 class="font-headline text-xl font-bold text-on-surface mb-3">{{ props.t.emptyTitle }}</h2>
      <p class="text-on-surface-variant font-body" :class="{ 'mb-6': props.t.emptyCta && hireMeUrl }">
        {{ props.t.emptyDescription }}
      </p>
      <a
        v-if="props.t.emptyCta && hireMeUrl"
        :href="hireMeUrl"
        class="inline-block px-6 py-3 rounded-xl text-sm font-semibold font-label bg-primary text-on-primary hover:bg-primary/90 transition-colors"
      >
        {{ props.t.emptyCta }}
      </a>
    </div>

    <!-- Offer cards -->
    <div v-else id="offers-grid" class="grid gap-4">
      <a
        v-for="offer in offers"
        :key="offer.id"
        :href="detailHref(offer)"
        class="bg-surface-container-low rounded-2xl border border-outline-variant/10 p-6 hover:border-primary/20 transition-colors cursor-pointer"
      >
        <div class="flex flex-col sm:flex-row sm:items-center justify-between gap-4">
          <div class="flex-1 min-w-0">
            <h3 class="font-headline text-lg font-bold text-on-surface truncate">{{ offer.jobTitle }}</h3>
            <p class="text-sm text-on-surface-variant font-body">{{ offer.companyName }}</p>
          </div>
          <div class="flex items-center gap-3 flex-shrink-0">
            <span
              v-if="offer.isRemote"
              class="px-3 py-1 rounded-full text-xs font-label font-semibold bg-tertiary-container text-on-surface"
            >
              {{ props.t.remote }}
            </span>
            <JobOfferStatusBadge :status="offer.status" :labels="props.t.statusLabels" class="px-3 py-1 text-xs" />
            <span class="text-xs text-on-surface-variant font-label">{{ formatDate(offer.createdAt, lang) }}</span>
          </div>
        </div>
      </a>
    </div>

    <!-- Pagination controls -->
    <nav
      v-if="pagination && pagination.pageCount > 1"
      id="pagination-controls"
      class="flex items-center justify-center gap-3 mt-8"
      aria-label="Pagination"
    >
      <button
        id="pagination-prev"
        type="button"
        class="flex items-center gap-1 px-4 py-2 rounded-xl text-sm font-label font-semibold bg-surface-container-low border border-outline-variant/20 text-on-surface hover:border-primary/30 transition-colors cursor-pointer disabled:opacity-40 disabled:cursor-not-allowed"
        :disabled="!pagination.hasPreviousPage"
        @click="previousPage"
      >
        <MaterialIcon :d="paths.chevronLeft" class="size-[18px]" />
        {{ props.t.paginationPrev }}
      </button>
      <span id="pagination-indicator" class="text-sm font-label text-on-surface-variant">{{ pageIndicator }}</span>
      <button
        id="pagination-next"
        type="button"
        class="flex items-center gap-1 px-4 py-2 rounded-xl text-sm font-label font-semibold bg-surface-container-low border border-outline-variant/20 text-on-surface hover:border-primary/30 transition-colors cursor-pointer disabled:opacity-40 disabled:cursor-not-allowed"
        :disabled="!pagination.hasNextPage"
        @click="nextPage"
      >
        {{ props.t.paginationNext }}
        <MaterialIcon :d="paths.chevronRight" class="size-[18px]" />
      </button>
    </nav>
  </div>
</template>
