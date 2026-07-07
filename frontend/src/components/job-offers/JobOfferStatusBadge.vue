<script setup lang="ts">
import type { JobOfferStatus } from "../../lib/job-offers/types";

const props = defineProps<{
  status: JobOfferStatus;
  labels: Record<JobOfferStatus, string>;
}>();

const badgeClasses: Record<JobOfferStatus, string> = {
  Submitted: "bg-secondary-container text-on-surface",
  InReview: "bg-primary-container text-on-primary-container",
  LetsTalk: "bg-tertiary-container text-on-surface",
  Declined: "bg-error-container text-on-surface",
  Cancelled: "bg-surface-container-high text-on-surface-variant",
};
</script>

<template>
  <span class="rounded-full font-label font-semibold" :class="badgeClasses[props.status] ?? ''">
    {{ props.labels[props.status] ?? props.status }}
  </span>
</template>
