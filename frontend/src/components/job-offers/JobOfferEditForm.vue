<script setup lang="ts">
import { reactive, ref } from "vue";
import { fieldError, type FieldRules, type FieldValidationStrings } from "../../lib/field-validation";
import type { JobOfferDetailDto, JobOfferEditBody } from "../../lib/job-offers/types";

const props = defineProps<{
  offer: JobOfferDetailDto;
  /** Error from the parent's PATCH request, rendered inline like validation errors. */
  submitError: string;
  t: {
    /** Field labels + placeholders, shared with the hire-me form (hire-me.json `form`). */
    labels: Record<string, string>;
    save: string;
    discard: string;
    /** Prefix listing optional fields the user tried to clear. */
    clearNotAllowed: string;
    validation: FieldValidationStrings;
  };
}>();

const emit = defineEmits<{ save: [body: JobOfferEditBody]; discard: [] }>();

type TextFieldName =
  "companyName" | "contactName" | "contactEmail" | "jobTitle" | "description" | "salaryRange" | "location" | "additionalNotes";

const fieldRules: Record<TextFieldName, FieldRules> = {
  companyName: { required: true, maxLength: 200 },
  contactName: { required: true, maxLength: 200 },
  contactEmail: { required: true, email: true, maxLength: 255 },
  jobTitle: { required: true, maxLength: 200 },
  description: { required: true, minLength: 20, maxLength: 5000 },
  salaryRange: { maxLength: 100 },
  location: { maxLength: 200 },
  additionalNotes: { maxLength: 2000 },
};

const values = reactive<Record<TextFieldName, string>>({
  companyName: props.offer.companyName,
  contactName: props.offer.contactName,
  contactEmail: props.offer.contactEmail,
  jobTitle: props.offer.jobTitle,
  description: props.offer.description,
  salaryRange: props.offer.salaryRange ?? "",
  location: props.offer.location ?? "",
  additionalNotes: props.offer.additionalNotes ?? "",
});
const isRemote = ref(props.offer.isRemote);

const errors = reactive<Record<TextFieldName, string>>({
  companyName: "",
  contactName: "",
  contactEmail: "",
  jobTitle: "",
  description: "",
  salaryRange: "",
  location: "",
  additionalNotes: "",
});
const clearedFieldsError = ref("");

function validateField(name: TextFieldName): boolean {
  errors[name] = fieldError(values[name], fieldRules[name], props.t.validation);
  return !errors[name];
}

function revalidateIfInvalid(name: TextFieldName) {
  if (errors[name]) validateField(name);
}

function validateAll(): boolean {
  let firstInvalid: TextFieldName | null = null;
  for (const name of Object.keys(fieldRules) as TextFieldName[]) {
    if (!validateField(name) && !firstInvalid) firstInvalid = name;
  }
  if (firstInvalid) document.getElementById(`edit-${firstInvalid}`)?.focus();
  return !firstInvalid;
}

function submit() {
  clearedFieldsError.value = "";
  if (!validateAll()) return;

  // Clearing a previously-set optional field is not supported by the backend:
  // the Edit command treats null as "don't edit this field", so the update
  // would appear to succeed but do nothing. Surface the constraint instead.
  const cleared: string[] = [];
  if (props.offer.salaryRange && !values.salaryRange.trim()) cleared.push(props.t.labels.salaryRange);
  if (props.offer.location && !values.location.trim()) cleared.push(props.t.labels.location);
  if (props.offer.additionalNotes && !values.additionalNotes.trim()) cleared.push(props.t.labels.additionalNotes);
  if (cleared.length > 0) {
    clearedFieldsError.value = props.t.clearNotAllowed + cleared.join(", ");
    return;
  }

  emit("save", {
    companyName: values.companyName,
    contactName: values.contactName,
    contactEmail: values.contactEmail,
    jobTitle: values.jobTitle,
    description: values.description,
    salaryRange: values.salaryRange || null,
    location: values.location || null,
    isRemote: isRemote.value,
    additionalNotes: values.additionalNotes || null,
  });
}

const inputClass =
  "w-full px-4 py-3 rounded-xl bg-surface-container border text-on-surface font-body text-sm placeholder:text-on-surface-variant/50 focus:outline-none focus:ring-2 focus:ring-primary/30 focus:border-primary transition-colors";

function borderClass(name: TextFieldName): string {
  return errors[name] ? "border-error" : "border-outline-variant/20";
}
</script>

<template>
  <div id="edit-form-section" class="mt-6">
    <form
      id="edit-form"
      novalidate
      class="bg-surface-container-low rounded-2xl border border-outline-variant/10 p-8 md:p-12 space-y-6"
      @submit.prevent="submit"
      @keydown.enter.ctrl.prevent="submit"
      @keydown.enter.meta.prevent="submit"
    >
      <div class="grid grid-cols-1 md:grid-cols-2 gap-6">
        <div>
          <label for="edit-companyName" class="block text-sm font-label font-semibold text-on-surface mb-2">
            {{ props.t.labels.companyName }}
          </label>
          <input
            id="edit-companyName"
            v-model="values.companyName"
            type="text"
            name="companyName"
            maxlength="200"
            :placeholder="props.t.labels.companyNamePlaceholder"
            :class="[inputClass, borderClass('companyName')]"
            :aria-invalid="!!errors.companyName"
            :aria-describedby="errors.companyName ? 'edit-companyName-error' : undefined"
            @blur="validateField('companyName')"
            @input="revalidateIfInvalid('companyName')"
          />
          <p v-if="errors.companyName" id="edit-companyName-error" class="text-error text-xs font-body mt-1">
            {{ errors.companyName }}
          </p>
        </div>
        <div>
          <label for="edit-contactName" class="block text-sm font-label font-semibold text-on-surface mb-2">
            {{ props.t.labels.contactName }}
          </label>
          <input
            id="edit-contactName"
            v-model="values.contactName"
            type="text"
            name="contactName"
            maxlength="200"
            :placeholder="props.t.labels.contactNamePlaceholder"
            :class="[inputClass, borderClass('contactName')]"
            :aria-invalid="!!errors.contactName"
            :aria-describedby="errors.contactName ? 'edit-contactName-error' : undefined"
            @blur="validateField('contactName')"
            @input="revalidateIfInvalid('contactName')"
          />
          <p v-if="errors.contactName" id="edit-contactName-error" class="text-error text-xs font-body mt-1">
            {{ errors.contactName }}
          </p>
        </div>
      </div>
      <div class="grid grid-cols-1 md:grid-cols-2 gap-6">
        <div>
          <label for="edit-contactEmail" class="block text-sm font-label font-semibold text-on-surface mb-2">
            {{ props.t.labels.contactEmail }}
          </label>
          <input
            id="edit-contactEmail"
            v-model="values.contactEmail"
            type="email"
            name="contactEmail"
            maxlength="255"
            :placeholder="props.t.labels.contactEmailPlaceholder"
            :class="[inputClass, borderClass('contactEmail')]"
            :aria-invalid="!!errors.contactEmail"
            :aria-describedby="errors.contactEmail ? 'edit-contactEmail-error' : undefined"
            @blur="validateField('contactEmail')"
            @input="revalidateIfInvalid('contactEmail')"
          />
          <p v-if="errors.contactEmail" id="edit-contactEmail-error" class="text-error text-xs font-body mt-1">
            {{ errors.contactEmail }}
          </p>
        </div>
        <div>
          <label for="edit-jobTitle" class="block text-sm font-label font-semibold text-on-surface mb-2">
            {{ props.t.labels.jobTitle }}
          </label>
          <input
            id="edit-jobTitle"
            v-model="values.jobTitle"
            type="text"
            name="jobTitle"
            maxlength="200"
            :placeholder="props.t.labels.jobTitlePlaceholder"
            :class="[inputClass, borderClass('jobTitle')]"
            :aria-invalid="!!errors.jobTitle"
            :aria-describedby="errors.jobTitle ? 'edit-jobTitle-error' : undefined"
            @blur="validateField('jobTitle')"
            @input="revalidateIfInvalid('jobTitle')"
          />
          <p v-if="errors.jobTitle" id="edit-jobTitle-error" class="text-error text-xs font-body mt-1">
            {{ errors.jobTitle }}
          </p>
        </div>
      </div>
      <div>
        <label for="edit-description" class="block text-sm font-label font-semibold text-on-surface mb-2">
          {{ props.t.labels.description }}
        </label>
        <textarea
          id="edit-description"
          v-model="values.description"
          name="description"
          maxlength="5000"
          rows="5"
          :placeholder="props.t.labels.descriptionPlaceholder"
          :class="[inputClass, borderClass('description'), 'resize-y']"
          :aria-invalid="!!errors.description"
          :aria-describedby="errors.description ? 'edit-description-error' : undefined"
          @blur="validateField('description')"
          @input="revalidateIfInvalid('description')"
        ></textarea>
        <p v-if="errors.description" id="edit-description-error" class="text-error text-xs font-body mt-1">
          {{ errors.description }}
        </p>
      </div>
      <div class="grid grid-cols-1 md:grid-cols-2 gap-6">
        <div>
          <label for="edit-salaryRange" class="block text-sm font-label font-semibold text-on-surface mb-2">
            {{ props.t.labels.salaryRange }}
          </label>
          <input
            id="edit-salaryRange"
            v-model="values.salaryRange"
            type="text"
            name="salaryRange"
            maxlength="100"
            :placeholder="props.t.labels.salaryRangePlaceholder"
            :class="[inputClass, borderClass('salaryRange')]"
            :aria-invalid="!!errors.salaryRange"
            :aria-describedby="errors.salaryRange ? 'edit-salaryRange-error' : undefined"
            @blur="validateField('salaryRange')"
            @input="revalidateIfInvalid('salaryRange')"
          />
          <p v-if="errors.salaryRange" id="edit-salaryRange-error" class="text-error text-xs font-body mt-1">
            {{ errors.salaryRange }}
          </p>
        </div>
        <div>
          <label for="edit-location" class="block text-sm font-label font-semibold text-on-surface mb-2">
            {{ props.t.labels.location }}
          </label>
          <input
            id="edit-location"
            v-model="values.location"
            type="text"
            name="location"
            maxlength="200"
            :placeholder="props.t.labels.locationPlaceholder"
            :class="[inputClass, borderClass('location')]"
            :aria-invalid="!!errors.location"
            :aria-describedby="errors.location ? 'edit-location-error' : undefined"
            @blur="validateField('location')"
            @input="revalidateIfInvalid('location')"
          />
          <p v-if="errors.location" id="edit-location-error" class="text-error text-xs font-body mt-1">
            {{ errors.location }}
          </p>
        </div>
      </div>
      <div class="flex items-center gap-3">
        <input
          id="edit-isRemote"
          v-model="isRemote"
          type="checkbox"
          name="isRemote"
          class="w-5 h-5 rounded border-outline-variant/20 text-primary focus:ring-primary/30 accent-primary"
        />
        <label for="edit-isRemote" class="text-sm font-label font-semibold text-on-surface">
          {{ props.t.labels.isRemote }}
        </label>
      </div>
      <div>
        <label for="edit-additionalNotes" class="block text-sm font-label font-semibold text-on-surface mb-2">
          {{ props.t.labels.additionalNotes }}
        </label>
        <textarea
          id="edit-additionalNotes"
          v-model="values.additionalNotes"
          name="additionalNotes"
          maxlength="2000"
          rows="3"
          :placeholder="props.t.labels.additionalNotesPlaceholder"
          :class="[inputClass, borderClass('additionalNotes'), 'resize-y']"
          :aria-invalid="!!errors.additionalNotes"
          :aria-describedby="errors.additionalNotes ? 'edit-additionalNotes-error' : undefined"
          @blur="validateField('additionalNotes')"
          @input="revalidateIfInvalid('additionalNotes')"
        ></textarea>
        <p v-if="errors.additionalNotes" id="edit-additionalNotes-error" class="text-error text-xs font-body mt-1">
          {{ errors.additionalNotes }}
        </p>
      </div>
      <div v-if="clearedFieldsError || props.submitError" id="edit-error" class="text-error text-sm font-body">
        {{ clearedFieldsError || props.submitError }}
      </div>
      <div class="flex gap-4">
        <button
          type="submit"
          class="px-6 py-3 rounded-xl text-sm font-semibold font-label bg-primary text-on-primary hover:bg-primary/90 transition-colors cursor-pointer"
        >
          {{ props.t.save }}
        </button>
        <button
          type="button"
          class="px-6 py-3 rounded-xl text-sm font-semibold font-label bg-surface-container-high text-on-surface hover:bg-surface-container-highest transition-colors cursor-pointer"
          @click="emit('discard')"
        >
          {{ props.t.discard }}
        </button>
      </div>
    </form>
  </div>
</template>
