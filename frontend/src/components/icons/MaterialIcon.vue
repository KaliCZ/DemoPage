<script setup lang="ts">
// Vue counterpart of astro-icon's material-symbols icons; path data comes
// from material-symbol-paths.ts. Size via Tailwind classes on the component.
defineProps<{ d: string }>();
</script>

<template>
  <svg viewBox="0 0 24 24" fill="currentColor" aria-hidden="true">
    <path :d="d" />
  </svg>
</template>
