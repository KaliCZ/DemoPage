/** Wire types for the job-offers API, shared by the list and detail Vue islands. */

export type JobOfferStatus = "Submitted" | "InReview" | "LetsTalk" | "Declined" | "Cancelled";

export interface JobOfferSummary {
  id: string;
  companyName: string;
  jobTitle: string;
  status: JobOfferStatus;
  isRemote: boolean;
  createdAt: string;
}

export interface JobOfferAttachment {
  fileName: string;
  fileSize: number;
  contentType: string;
}

export interface JobOfferDetailDto extends JobOfferSummary {
  contactName: string;
  contactEmail: string;
  description: string;
  salaryRange: string | null;
  location: string | null;
  additionalNotes: string | null;
  attachments: JobOfferAttachment[];
  /** Only present on the admin endpoint. */
  userEmail?: string;
}

export interface PaginationInfo {
  page: number;
  pageSize: number;
  totalCount: number;
  pageCount: number;
  hasPreviousPage: boolean;
  hasNextPage: boolean;
}

export interface JobOfferListResponse {
  items: JobOfferSummary[];
  pagination: PaginationInfo;
}

export interface JobOfferFieldChange {
  field: string;
  oldValue: string | null;
  newValue: string | null;
}

export interface JobOfferHistoryEntry {
  eventType: string;
  description: string;
  actorUserId: string | null;
  actorEmail: string;
  timestamp: string;
  changes: JobOfferFieldChange[] | null;
}

/** The PATCH /api/job-offers/{id} body; null means "don't edit this field". */
export interface JobOfferEditBody {
  companyName: string;
  contactName: string;
  contactEmail: string;
  jobTitle: string;
  description: string;
  salaryRange: string | null;
  location: string | null;
  isRemote: boolean;
  additionalNotes: string | null;
}

export interface JobOfferComment {
  userId: string;
  userName: string;
  content: string;
  createdAt: string;
}
