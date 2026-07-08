<script setup lang="ts">
import { onMounted, onUnmounted, ref } from "vue";
import { getAccessToken, getCurrentUser } from "../../lib/auth";

const props = defineProps<{
  apiUrl: string;
  slug: string;
  t: {
    notReadYet: string;
    readOnce: string;
    readTimes: string;
  };
}>();

const label = ref<string | null>(null);

// One recorded read per user per page load — auth-change also fires for token refreshes.
let recordedForUserId: string | null = null;

async function recordRead(user: any) {
  if (!user) {
    recordedForUserId = null;
    label.value = null;
    return;
  }
  if (recordedForUserId === user.id) return;
  recordedForUserId = user.id;

  try {
    const token = await getAccessToken();
    if (!token) return;
    const res = await fetch(`${props.apiUrl}/api/blog/${props.slug}/reads`, {
      method: "POST",
      headers: { Authorization: `Bearer ${token}` },
    });
    if (!res.ok) return;
    const data: { previousReadCount: number } = await res.json();
    label.value =
      data.previousReadCount === 0
        ? props.t.notReadYet
        : data.previousReadCount === 1
          ? props.t.readOnce
          : props.t.readTimes.replace("{count}", String(data.previousReadCount));
  } catch {
    // The read status is decorative — a network hiccup just leaves it blank.
  }
}

const onAuthChange = (event: Event) => void recordRead((event as CustomEvent).detail?.user ?? null);

onMounted(async () => {
  window.addEventListener("auth-change", onAuthChange);
  // auth-change may already have fired before hydration — pull the current user.
  await recordRead(await getCurrentUser());
});
onUnmounted(() => window.removeEventListener("auth-change", onAuthChange));
</script>

<template>
  <span v-if="label">{{ label }}</span>
</template>
