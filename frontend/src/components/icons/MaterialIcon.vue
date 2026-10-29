<script setup lang="ts">
// Vue islands can't use astro-icon's <Icon>, so they draw material symbols through this.
defineProps<{ d: string }>();
</script>

<template>
  <svg viewBox="0 0 24 24" fill="currentColor" aria-hidden="true">
    <path :d="d" />
  </svg>
</template>
