/**
 * Form-field validation rules shared by the job-offers edit form (Vue) and,
 * eventually, the hire-me submission form — the two forms must stay in sync
 * with the backend contract, so the rules live in one place.
 */

export interface FieldValidationStrings {
  required: string;
  emailInvalid: string;
  /** Contains a `{min}` placeholder. */
  descriptionMinLength: string;
  /** Contains a `{max}` placeholder. */
  maxLength: string;
}

export interface FieldRules {
  required?: boolean;
  email?: boolean;
  minLength?: number;
  maxLength?: number;
}

const emailPattern = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;

/** Returns the localized error for a field value, or "" when valid. */
export function fieldError(rawValue: string, rules: FieldRules, strings: FieldValidationStrings): string {
  const value = rawValue.trim();
  if (rules.required && !value) return strings.required;
  if (rules.email && value && !emailPattern.test(value)) return strings.emailInvalid;
  if (rules.minLength && value && value.length < rules.minLength) {
    return strings.descriptionMinLength.replace("{min}", String(rules.minLength));
  }
  if (rules.maxLength && value.length > rules.maxLength) {
    return strings.maxLength.replace("{max}", String(rules.maxLength));
  }
  return "";
}
