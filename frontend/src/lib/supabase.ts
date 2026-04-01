/**
 * Supabase client configuration.
 *
 * These values are public (safe to expose in the browser).
 * Set them as environment variables in .env:
 *   PUBLIC_SUPABASE_URL=https://your-project.supabase.co
 *   PUBLIC_SUPABASE_PUBLISHABLE_KEY=your-publishable-key
 *
 * For local development, create frontend/.env with these values.
 */
export const SUPABASE_URL = import.meta.env.PUBLIC_SUPABASE_URL || '';
export const SUPABASE_PUBLISHABLE_KEY = import.meta.env.PUBLIC_SUPABASE_PUBLISHABLE_KEY || '';

/**
 * API base URL for the backend.
 * In production: https://api.kalandra.tech
 * In development: http://localhost:5000
 */
export const API_URL = import.meta.env.PUBLIC_API_URL || 'http://localhost:5000';
