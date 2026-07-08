<script setup lang="ts">
import { computed, onMounted, onUnmounted, ref, watch } from "vue";
import { getAccessToken, getCurrentUser } from "../../lib/auth";
import type { BlogListPost, BlogPostStats, BlogStatsResponse } from "../../lib/blog/types";
import MaterialIcon from "../icons/MaterialIcon.vue";
import { materialSymbolPaths as paths } from "../icons/material-symbol-paths";

type SortKey = "recent" | "reactions" | "views";

const props = defineProps<{
  apiUrl: string;
  posts: BlogListPost[];
  t: {
    updatedOn: string;
    sortLabel: string;
    sortRecent: string;
    sortReactions: string;
    sortViews: string;
    unreadOnly: string;
    unreadEmpty: string;
    statViews: string;
    statReactions: string;
    paginationPrev: string;
    paginationNext: string;
    /** Contains `{current}` and `{total}` placeholders. */
    paginationPage: string;
    readState: { notReadYet: string; readOnce: string; readTimes: string };
  };
}>();

const PAGE_SIZE = 10;

const stats = ref<Record<string, BlogPostStats>>({});
const statsLoaded = ref(false);
const signedIn = ref(false);
const sortBy = ref<SortKey>("recent");
const unreadOnly = ref(false);
const page = ref(1);

const filteredPosts = computed(() => {
  let posts = props.posts;
  if (signedIn.value && unreadOnly.value) posts = posts.filter((post) => (stats.value[post.slug]?.viewerReads ?? 0) === 0);
  if (sortBy.value !== "recent") {
    const key = sortBy.value === "reactions" ? "totalReactions" : "totalReads";
    // Stable sort — ties keep the newest-first order of the posts prop.
    posts = [...posts].sort((a, b) => (stats.value[b.slug]?.[key] ?? 0) - (stats.value[a.slug]?.[key] ?? 0));
  }
  return posts;
});

const pageCount = computed(() => Math.max(1, Math.ceil(filteredPosts.value.length / PAGE_SIZE)));
const visiblePosts = computed(() => filteredPosts.value.slice((page.value - 1) * PAGE_SIZE, page.value * PAGE_SIZE));
const pageIndicator = computed(() =>
  props.t.paginationPage.replace("{current}", String(page.value)).replace("{total}", String(pageCount.value)),
);

function readStateLabel(post: BlogListPost): string | null {
  const viewerReads = stats.value[post.slug]?.viewerReads;
  if (viewerReads == null) return null;
  if (viewerReads === 0) return props.t.readState.notReadYet;
  if (viewerReads === 1) return props.t.readState.readOnce;
  return props.t.readState.readTimes.replace("{count}", String(viewerReads));
}

async function loadStats() {
  try {
    const params = new URLSearchParams();
    for (const post of props.posts) params.append("slug", post.slug);
    const token = await getAccessToken();
    const res = await fetch(`${props.apiUrl}/api/blog/stats?${params}`, {
      headers: token ? { Authorization: `Bearer ${token}` } : {},
    });
    if (!res.ok) return;
    const data: BlogStatsResponse = await res.json();
    stats.value = Object.fromEntries(data.posts.map((post) => [post.slug, post]));
    statsLoaded.value = true;
  } catch {
    // Stats are decorative — a network hiccup just leaves the plain list.
  }
}

// Reload once per auth state — auth-change also fires for token refreshes.
let loadedForUserId: string | null | undefined;

function applyUser(user: any) {
  signedIn.value = !!user;
  if (!user) unreadOnly.value = false;
  const userId = user?.id ?? null;
  if (loadedForUserId === userId) return;
  loadedForUserId = userId;
  void loadStats();
}

// A sort or filter change always restarts from page 1.
watch([sortBy, unreadOnly], () => (page.value = 1));
// The unread filter can shrink the list under the current page.
watch(pageCount, (count) => (page.value = Math.min(page.value, count)));

const onAuthChange = (event: Event) => applyUser((event as CustomEvent).detail?.user ?? null);

onMounted(async () => {
  window.addEventListener("auth-change", onAuthChange);
  // auth-change may already have fired before hydration — pull the current user.
  applyUser(await getCurrentUser());
});
onUnmounted(() => window.removeEventListener("auth-change", onAuthChange));
</script>

<template>
  <div>
    <div class="flex flex-wrap items-center gap-x-6 gap-y-3 mb-8">
      <label class="flex items-center gap-2 text-sm font-label text-on-surface-variant" for="blog-sort">
        {{ props.t.sortLabel }}
        <select
          id="blog-sort"
          v-model="sortBy"
          class="px-3 py-2 rounded-xl bg-surface-container-low border border-outline-variant/20 text-sm font-label font-semibold text-on-surface hover:border-primary/30 transition-colors cursor-pointer"
        >
          <option value="recent">{{ props.t.sortRecent }}</option>
          <option value="reactions">{{ props.t.sortReactions }}</option>
          <option value="views">{{ props.t.sortViews }}</option>
        </select>
      </label>
      <label v-if="signedIn" id="blog-unread-filter" class="flex items-center gap-2 text-sm font-label text-on-surface cursor-pointer">
        <input
          v-model="unreadOnly"
          type="checkbox"
          class="w-4 h-4 rounded border-outline-variant/30 text-primary focus:ring-primary/30 accent-primary"
        />
        {{ props.t.unreadOnly }}
      </label>
    </div>

    <p v-if="filteredPosts.length === 0" class="font-body text-on-surface-variant">{{ props.t.unreadEmpty }}</p>

    <ul class="space-y-8" role="list">
      <li v-for="post in visiblePosts" :key="post.slug">
        <a
          :href="post.url"
          :hreflang="post.crossLocale"
          class="block rounded-xl border border-outline-variant/20 bg-surface-container-lowest p-6 md:p-8 hover:border-primary/40 transition-colors"
        >
          <p class="font-label text-xs uppercase tracking-widest text-tertiary mb-3">
            <time :datetime="post.pubDateIso">{{ post.pubDateLabel }}</time>
            <span v-if="post.updatedDateLabel">
              {{ " · " }}{{ props.t.updatedOn }} <time :datetime="post.updatedDateIso">{{ post.updatedDateLabel }}</time>
            </span>
            <span
              v-if="post.crossLocaleLabel"
              class="inline-block align-middle ml-2 px-2 py-0.5 rounded-full bg-surface-container-low border border-outline-variant/30 normal-case tracking-normal text-on-surface-variant"
            >
              {{ post.crossLocaleLabel }}
            </span>
          </p>
          <h2 :lang="post.crossLocale" class="font-headline text-2xl md:text-3xl font-bold tracking-tight mb-3 text-on-surface">
            {{ post.title }}
          </h2>
          <p :lang="post.crossLocale" class="font-body text-on-surface-variant leading-relaxed mb-4">
            {{ post.summary }}
          </p>
          <ul v-if="post.tags.length > 0" class="flex flex-wrap gap-2" role="list">
            <li
              v-for="tag in post.tags"
              :key="tag"
              class="px-3 py-1 rounded-full bg-surface-container-low border border-outline-variant/30 text-xs font-label text-on-surface-variant"
            >
              {{ tag }}
            </li>
          </ul>
          <p v-if="statsLoaded" class="flex flex-wrap items-center gap-x-4 gap-y-1 mt-4 text-xs font-label text-on-surface-variant">
            <span class="inline-flex items-center gap-1.5" :title="props.t.statViews">
              <MaterialIcon :d="paths.visibility" class="size-4" />
              {{ stats[post.slug]?.totalReads ?? 0 }}
              <span class="sr-only">{{ props.t.statViews }}</span>
            </span>
            <span class="inline-flex items-center gap-1.5" :title="props.t.statReactions">
              <MaterialIcon :d="paths.favorite" class="size-4" />
              {{ stats[post.slug]?.totalReactions ?? 0 }}
              <span class="sr-only">{{ props.t.statReactions }}</span>
            </span>
            <span v-if="readStateLabel(post)">{{ readStateLabel(post) }}</span>
          </p>
        </a>
      </li>
    </ul>

    <nav v-if="pageCount > 1" id="pagination-controls" class="flex items-center justify-center gap-3 mt-10" aria-label="Pagination">
      <button
        id="pagination-prev"
        type="button"
        class="flex items-center gap-1 px-4 py-2 rounded-xl text-sm font-label font-semibold bg-surface-container-low border border-outline-variant/20 text-on-surface hover:border-primary/30 transition-colors cursor-pointer disabled:opacity-40 disabled:cursor-not-allowed"
        :disabled="page <= 1"
        @click="page--"
      >
        <MaterialIcon :d="paths.chevronLeft" class="size-[18px]" />
        {{ props.t.paginationPrev }}
      </button>
      <span id="pagination-indicator" class="text-sm font-label text-on-surface-variant">{{ pageIndicator }}</span>
      <button
        id="pagination-next"
        type="button"
        class="flex items-center gap-1 px-4 py-2 rounded-xl text-sm font-label font-semibold bg-surface-container-low border border-outline-variant/20 text-on-surface hover:border-primary/30 transition-colors cursor-pointer disabled:opacity-40 disabled:cursor-not-allowed"
        :disabled="page >= pageCount"
        @click="page++"
      >
        {{ props.t.paginationNext }}
        <MaterialIcon :d="paths.chevronRight" class="size-[18px]" />
      </button>
    </nav>
  </div>
</template>
