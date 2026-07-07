<script setup lang="ts">
import { getAccessToken } from "../../lib/auth";
import { formatFileSize } from "../../lib/job-offers/format";
import type { JobOfferAttachment } from "../../lib/job-offers/types";
import { requestErrorMessage, type RequestErrorStrings } from "../../lib/request-errors";
import MaterialIcon from "../icons/MaterialIcon.vue";
import { materialSymbolPaths as paths } from "../icons/material-symbol-paths";

const props = defineProps<{
  apiUrl: string;
  offerId: string;
  attachments: JobOfferAttachment[];
  t: {
    title: string;
    download: string;
    downloadFailed: string;
    errors: RequestErrorStrings;
  };
}>();

async function download(attachment: JobOfferAttachment) {
  const token = await getAccessToken();
  if (!token) return;

  try {
    const encodedName = encodeURIComponent(attachment.fileName);
    const response = await fetch(`${props.apiUrl}/api/job-offers/${props.offerId}/attachments/${encodedName}`, {
      headers: { Authorization: `Bearer ${token}` },
    });

    if (!response.ok) {
      // A missing file gets the download-specific message instead of the generic 404 one.
      const strings = { ...props.t.errors, notFound: props.t.downloadFailed };
      (window as any).__showSnackbar?.(requestErrorMessage(strings, response.status), "error");
      return;
    }

    const blob = await response.blob();
    const url = URL.createObjectURL(blob);
    const anchor = document.createElement("a");
    anchor.href = url;
    anchor.download = attachment.fileName;
    anchor.click();
    URL.revokeObjectURL(url);
  } catch {
    (window as any).__showSnackbar?.(props.t.errors.networkError, "error");
  }
}
</script>

<template>
  <div class="mb-6">
    <p class="text-xs font-label text-on-surface-variant mb-2">{{ props.t.title }}</p>
    <div class="space-y-2">
      <div
        v-for="attachment in props.attachments"
        :key="attachment.fileName"
        class="flex items-center gap-3 px-3 py-2 rounded-lg bg-surface-container border border-outline-variant/10 text-sm"
      >
        <MaterialIcon :d="paths.description" class="size-[18px] text-on-surface-variant shrink-0" />
        <span class="flex-1 truncate font-body text-on-surface">{{ attachment.fileName }}</span>
        <span class="text-xs text-on-surface-variant font-label">{{ formatFileSize(attachment.fileSize) }}</span>
        <button
          type="button"
          class="flex items-center gap-1 px-3 py-1 rounded-lg text-xs font-label font-semibold text-primary hover:bg-primary/10 transition-colors cursor-pointer"
          @click="download(attachment)"
        >
          <MaterialIcon :d="paths.download" class="size-3.5" />
          {{ props.t.download }}
        </button>
      </div>
    </div>
  </div>
</template>
