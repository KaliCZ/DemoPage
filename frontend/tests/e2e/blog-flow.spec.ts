import { test, expect } from "@playwright/test";
import { randomUUID } from "crypto";
import * as path from "path";
import { fileURLToPath } from "url";

const __dirname = path.dirname(fileURLToPath(import.meta.url));

/**
 * E2E test for the blog reactions + comments flow against the real backend:
 *   1. Anonymous visitor can react (no sign-in), but commenting still needs an account
 *   2. An anonymous reaction is attributed to the account after signing in
 *   3. Read tracking records the reader's view and drives the index controls
 *   4. After sign-in: toggle a reaction (persists across reload), post a
 *      comment, reply to it, and delete the reply (tombstone)
 *
 * Requires local Supabase, backend API at :5000, and frontend at :4321.
 */

const API_URL = process.env.PUBLIC_API_URL || "http://localhost:5000";
const SUPABASE_URL = process.env.SUPABASE_URL || "http://localhost:54321";
const SUPABASE_SERVICE_ROLE_KEY =
  process.env.SUPABASE_SERVICE_ROLE_KEY ||
  "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJpc3MiOiJzdXBhYmFzZS1kZW1vIiwicm9sZSI6InNlcnZpY2Vfcm9sZSIsImV4cCI6MTk4MzgxMjk5Nn0.EGIM96RAZx35lJzdJsyH-qQwv8Hdp7fsn3W0YpN81IU";

// Supabase's bundled mail catcher; the backend's dev SMTP config points at it.
const MAILPIT_URL = process.env.MAILPIT_URL || "http://127.0.0.1:54324";
const AUTHOR_EMAIL = "author@kalandra.local";

const SLUG = "zero-code-validations-in-your-dotnet-api";
const POST_PATH = `/blog/${SLUG}`;

const testUser = {
  email: `e2e-blog-${Date.now()}@test.local`,
  password: "test-password-123",
  fullName: "E2E Blog User",
};

const replyingUser = {
  email: `e2e-blog-replier-${Date.now()}@test.local`,
  password: "test-password-123",
  fullName: "E2E Blog Replier",
};

// Used only by the all-posts anti-drift probe; its reactions are reverted so they don't perturb the counts asserted elsewhere.
const driftUser = {
  email: `e2e-blog-drift-${Date.now()}@test.local`,
  password: "test-password-123",
  fullName: "E2E Drift User",
};

const avatarChangeUser = {
  email: `e2e-blog-avatar-${Date.now()}@test.local`,
  password: "test-password-123",
  fullName: "E2E Avatar Change User",
};

const pendingReactionUser = {
  email: `e2e-blog-pending-${Date.now()}@test.local`,
  password: "test-password-123",
  fullName: "E2E Pending Reaction User",
};

// Fresh per run so the read-count progression asserted below is deterministic.
const readTrackingUser = {
  email: `e2e-blog-reader-${Date.now()}@test.local`,
  password: "test-password-123",
  fullName: "E2E Read Tracking User",
};

/** Polls the mail catcher until an email to `to` whose text mentions `containing` arrives. */
async function waitForEmail(request: import("@playwright/test").APIRequestContext, { to, containing }: { to: string; containing: string }) {
  const query = `to:"${to}" "${containing}"`;
  await expect
    .poll(
      async () => {
        const response = await request.get(`${MAILPIT_URL}/api/v1/search?query=${encodeURIComponent(query)}`);
        if (!response.ok()) return 0;
        const body = await response.json();
        return (body.messages ?? []).length;
      },
      { timeout: 30000, message: `email to ${to} containing "${containing}"` },
    )
    .toBeGreaterThan(0);
}

test.describe("Blog Flow", () => {
  // Set by the signed-in test, replied to by the cross-user test below it.
  let sharedCommentText = "";

  test.beforeAll(async ({ request }) => {
    for (const user of [testUser, replyingUser, driftUser, avatarChangeUser, pendingReactionUser, readTrackingUser]) {
      const response = await request.post(`${SUPABASE_URL}/auth/v1/admin/users`, {
        headers: {
          Authorization: `Bearer ${SUPABASE_SERVICE_ROLE_KEY}`,
          apikey: SUPABASE_SERVICE_ROLE_KEY,
          "Content-Type": "application/json",
        },
        data: {
          email: user.email,
          password: user.password,
          email_confirm: true,
          user_metadata: { full_name: user.fullName },
        },
      });
      expect(response.ok(), `Failed to create test user: ${await response.text()}`).toBeTruthy();
    }
  });

  test("anonymous visitor can react but must sign in to comment", async ({ page, request }) => {
    await page.goto(POST_PATH);

    const reactions = page.locator('section[aria-label="Was this useful?"]');
    const thumbsUp = reactions.getByRole("button", { name: "Thumbs up" });
    await expect(thumbsUp).toBeVisible();
    await expect(reactions.getByRole("button", { name: "Thumbs down" })).toBeVisible();

    // Anonymous reactions land directly — no auth dialog. Toggle on then off so counts stay put.
    const toggleOn = page.waitForResponse((response) => response.url().includes("/reactions/toggle"));
    await thumbsUp.click();
    expect((await toggleOn).ok()).toBeTruthy();
    await expect(thumbsUp).toHaveAttribute("aria-pressed", "true");
    await expect(page.locator("#auth-dialog")).not.toBeVisible();
    const toggleOff = page.waitForResponse((response) => response.url().includes("/reactions/toggle"));
    await thumbsUp.click();
    await toggleOff;
    await expect(thumbsUp).toHaveAttribute("aria-pressed", "false");

    // The sign-in CTA below the comments opens the dialog; the top-right X and the backdrop both close it.
    const signInCta = page.locator("#blog-signin-cta");
    await signInCta.getByRole("button", { name: "Sign In" }).click();
    await expect(page.locator("#auth-dialog")).toBeVisible();
    await page.locator("#auth-dialog-close").click();
    await expect(page.locator("#auth-dialog")).not.toBeVisible();
    await signInCta.getByRole("button", { name: "Sign In" }).click();
    await expect(page.locator("#auth-dialog")).toBeVisible();
    await page.mouse.click(10, 10);
    await expect(page.locator("#auth-dialog")).not.toBeVisible();

    // Reactions accept a visitor id anonymously; comments still require an account.
    const anonReaction = await request.post(`${API_URL}/api/blog/${SLUG}/reactions/toggle`, {
      headers: { "X-Visitor-Id": randomUUID() },
      data: { kind: "Rocket" },
    });
    expect(anonReaction.ok()).toBeTruthy();

    const commentResponse = await request.post(`${API_URL}/api/blog/${SLUG}/comments`, {
      data: { content: "anonymous comment" },
    });
    expect(commentResponse.status()).toBe(401);
  });

  test("an anonymous reaction is attributed to the account after signing in", async ({ page, request }) => {
    await page.goto(POST_PATH);

    const reactions = page.locator('section[aria-label="Was this useful?"]');
    const heart = reactions.getByRole("button", { name: "Love it" });

    // Baseline from the API, not the UI — the rendered count races the island's async fetch.
    const baseline = await request.get(`${API_URL}/api/blog/${SLUG}/reactions`);
    const countBefore = (await baseline.json()).counts.heart;

    // React while signed out — no dialog; the reaction lands under the anonymous visitor id.
    const toggleSettled = page.waitForResponse((response) => response.url().includes("/reactions/toggle"));
    await heart.click();
    expect((await toggleSettled).ok()).toBeTruthy();
    await expect(heart).toHaveAttribute("aria-pressed", "true");
    await expect(heart.locator("span").nth(1)).toHaveText(String(countBefore + 1));

    // Signing in links the visitor to the account: the reaction is folded in — still pressed, not doubled.
    const linkSettled = page.waitForResponse((response) => response.url().includes("/visitor/link"));
    await page.evaluate(
      async ({ email, password }) => {
        const supabase = await (window as any).__supabaseReady;
        if (!supabase) throw new Error("Supabase client not available");
        const { error } = await supabase.auth.signInWithPassword({ email, password });
        if (error) throw new Error(`Sign-in failed: ${error.message}`);
      },
      { email: pendingReactionUser.email, password: pendingReactionUser.password },
    );
    expect((await linkSettled).ok()).toBeTruthy();

    await expect(heart).toHaveAttribute("aria-pressed", "true");
    await expect(heart.locator("span").nth(1)).toHaveText(String(countBefore + 1));
  });

  test("read tracking records the reader's own count and drives the unread filter", async ({ page }) => {
    // Signed out: no sign-in nag on the post; the index hides the unread filter. (Public view/reader
    // counts are gated off until the pre-rollout traffic is seeded, so they aren't asserted here.)
    await page.goto(POST_PATH);
    await expect(page.getByText("Sign in to track your reading.")).toHaveCount(0);
    await page.goto("/blog");
    await expect(page.locator("#blog-sort")).toBeVisible();
    await expect(page.locator("#blog-unread-filter")).toHaveCount(0);

    // Sign in on the post: the reader's first view reads as "not read yet" (no prior session).
    await page.goto(POST_PATH);
    await page.evaluate(
      async ({ email, password }) => {
        const supabase = await (window as any).__supabaseReady;
        if (!supabase) throw new Error("Supabase client not available");
        const { error } = await supabase.auth.signInWithPassword({ email, password });
        if (error) throw new Error(`Sign-in failed: ${error.message}`);
      },
      { email: readTrackingUser.email, password: readTrackingUser.password },
    );
    await expect(page.getByText("Not read yet")).toBeVisible();

    // The index shows the reader's own read state, and the unread filter drops the post they've read.
    await page.goto("/blog");
    const readCard = page.locator("ul[role=list] > li").filter({ hasText: "Zero-Code Validations" });
    await expect(readCard.getByText("Read once")).toBeVisible();
    await expect(page.locator("#blog-unread-filter")).toBeVisible();
    await page.locator("#blog-unread-filter input").check();
    await expect(page.getByRole("link", { name: /Zero-Code Validations/ })).toHaveCount(0);
    await page.locator("#blog-unread-filter input").uncheck();
  });

  test("signed-in user reacts, comments, replies, and deletes", async ({ page, request }) => {
    await page.goto(POST_PATH);

    await page.evaluate(
      async ({ email, password }) => {
        const supabase = await (window as any).__supabaseReady;
        if (!supabase) throw new Error("Supabase client not available");
        const { error } = await supabase.auth.signInWithPassword({ email, password });
        if (error) throw new Error(`Sign-in failed: ${error.message}`);
      },
      { email: testUser.email, password: testUser.password },
    );

    // Reaction toggles on and survives a reload (state comes from the backend).
    const reactions = page.locator('section[aria-label="Was this useful?"]');
    const thumbsUp = reactions.getByRole("button", { name: "Thumbs up" });
    await expect(thumbsUp).toHaveAttribute("aria-pressed", "false");

    // Count-relative assertion — earlier runs may have left reactions behind.
    // Baseline from the API, not the UI: the rendered count races the island's
    // async fetch, while the post-toggle UI always converges on server state.
    const thumbsUpCount = thumbsUp.locator("span").nth(1);
    const baseline = await request.get(`${API_URL}/api/blog/${SLUG}/reactions`);
    const countBefore = (await baseline.json()).counts.thumbsUp;

    // The UI flips optimistically, so wait for the server write to land before
    // reloading — a reload mid-flight would abort the request and lose the toggle.
    const toggleSettled = page.waitForResponse((response) => response.url().includes("/reactions/toggle"));
    await thumbsUp.click();
    expect((await toggleSettled).ok()).toBeTruthy();
    await expect(thumbsUp).toHaveAttribute("aria-pressed", "true");
    await expect(thumbsUpCount).toHaveText(String(countBefore + 1));

    await page.reload();
    const reloadedThumbsUp = page.locator('section[aria-label="Was this useful?"]').getByRole("button", { name: "Thumbs up" });
    await expect(reloadedThumbsUp).toHaveAttribute("aria-pressed", "true");
    await expect(reloadedThumbsUp.locator("span").nth(1)).toHaveText(String(countBefore + 1));

    // Signed in, the sign-in CTA is gone (Layout's auth-known-in tier hides it).
    await expect(page.locator("#blog-signin-cta")).not.toBeVisible();

    // Post a top-level comment.
    const comments = page.locator('section[aria-label="Comments"]');
    const commentText = `E2E comment ${Date.now()}`;
    sharedCommentText = commentText;
    await comments.getByRole("textbox").fill(commentText);
    await comments.getByRole("button", { name: "Post comment" }).click();
    const commentItem = comments.locator("li").filter({ hasText: commentText }).first();
    await expect(commentItem).toBeVisible();
    await expect(commentItem).toContainText(testUser.fullName);

    // A success snackbar confirms the post landed.
    const snackbar = page.locator("#snackbar");
    await expect(snackbar).toContainText("Comment posted.");
    await expect(snackbar.locator("> div")).toHaveClass(/bg-tertiary-container/);

    // The same Temporal workflow that stored the comment notifies the blog author —
    // the email must land in the local mail catcher.
    await waitForEmail(request, { to: AUTHOR_EMAIL, containing: commentText });

    // Reply to it.
    await commentItem.getByRole("button", { name: "Reply" }).click();
    const replyText = `E2E reply ${Date.now()}`;
    await comments.getByRole("textbox").fill(replyText);
    await comments.getByRole("button", { name: "Post comment" }).click();
    const replyItem = comments.locator("li").filter({ hasText: replyText }).first();
    await expect(replyItem).toBeVisible();

    // Self-reply: the blog author is notified, the parent author (same user) is not.
    await waitForEmail(request, { to: AUTHOR_EMAIL, containing: replyText });

    // Collapsing the parent hides its replies; expanding brings them back.
    await commentItem.getByRole("button", { name: "Hide replies" }).click();
    await expect(replyItem).toBeHidden();
    await commentItem.getByRole("button", { name: /^Show replies/ }).click();
    await expect(replyItem).toBeVisible();

    // Delete the reply — inline confirm, then the tombstone placeholder takes its place.
    await replyItem.getByRole("button", { name: "Delete" }).click();
    await replyItem.getByRole("button", { name: "Delete" }).click();
    await expect(comments.getByText(replyText)).toHaveCount(0);
    await expect(comments.getByText("[deleted]").first()).toBeVisible();
  });

  test("a reply from another user emails the parent comment's author", async ({ page, request }) => {
    expect(sharedCommentText, "previous test must have posted a comment").not.toBe("");

    await page.goto(POST_PATH);
    await page.evaluate(
      async ({ email, password }) => {
        const supabase = await (window as any).__supabaseReady;
        if (!supabase) throw new Error("Supabase client not available");
        const { error } = await supabase.auth.signInWithPassword({ email, password });
        if (error) throw new Error(`Sign-in failed: ${error.message}`);
      },
      { email: replyingUser.email, password: replyingUser.password },
    );

    const comments = page.locator('section[aria-label="Comments"]');
    const parentItem = comments.locator("li").filter({ hasText: sharedCommentText }).first();
    await parentItem.getByRole("button", { name: "Reply" }).click();

    const crossReplyText = `E2E cross-user reply ${Date.now()}`;
    await comments.getByRole("textbox").fill(crossReplyText);
    await comments.getByRole("button", { name: "Post comment" }).click();
    await expect(comments.getByText(crossReplyText)).toBeVisible();

    // One workflow, two notifications: the blog author and the parent comment's author.
    await waitForEmail(request, { to: AUTHOR_EMAIL, containing: crossReplyText });
    await waitForEmail(request, { to: testUser.email, containing: crossReplyText });
  });

  test("the backend accepts reactions on every published post (slug catalogs don't drift)", async ({ page, request }) => {
    // The backend gates reactions to slugs in BlogPostCatalog; the frontend owns the
    // actual posts. Enumerate the published posts from the sitemap and prove the
    // backend recognizes each — a new frontend post whose slug wasn't added to the
    // catalog fails here.
    await page.goto(POST_PATH);
    await page.evaluate(
      async ({ email, password }) => {
        const supabase = await (window as any).__supabaseReady;
        if (!supabase) throw new Error("Supabase client not available");
        const { error } = await supabase.auth.signInWithPassword({ email, password });
        if (error) throw new Error(`Sign-in failed: ${error.message}`);
      },
      { email: driftUser.email, password: driftUser.password },
    );

    const token = await page.evaluate(async () => {
      const supabase = await (window as any).__supabaseReady;
      const { data } = await supabase.auth.getSession();
      return data.session?.access_token as string | undefined;
    });
    expect(token, "drift user must be signed in").toBeTruthy();

    const sitemap = await request.get("/sitemap.xml");
    expect(sitemap.ok()).toBeTruthy();
    const xml = await sitemap.text();
    // Capture the slug from /blog/<slug> and /cs/blog/<slug>; the bare /blog index has no slug.
    const slugs = [...new Set([...xml.matchAll(/<loc>https:\/\/www\.kalandra\.tech\/(?:cs\/)?blog\/([^<]+)<\/loc>/g)].map((m) => m[1]))];
    expect(slugs.length, "sitemap should list at least one blog post").toBeGreaterThan(0);

    const headers = { Authorization: `Bearer ${token}`, "X-Visitor-Id": randomUUID() };
    for (const slug of slugs) {
      const on = await request.post(`${API_URL}/api/blog/${slug}/reactions/toggle`, { headers, data: { kind: "ThumbsUp" } });
      expect(on.ok(), `backend rejected a reaction on "${slug}" — add it to BlogPostCatalog`).toBeTruthy();
      // Toggle back so this probe leaves reaction counts untouched.
      await request.post(`${API_URL}/api/blog/${slug}/reactions/toggle`, { headers, data: { kind: "ThumbsUp" } });
    }
  });

  test("a profile picture change shows on the author's past blog comments", async ({ page }) => {
    const portrait200 = path.join(__dirname, "..", "..", "public", "images", "pavel-portrait-200.webp");
    const portrait400 = path.join(__dirname, "..", "..", "public", "images", "pavel-portrait-400.webp");

    const signIn = ({ email, password }: { email: string; password: string }) =>
      page.evaluate(
        async ({ email, password }) => {
          const supabase = await (window as any).__supabaseReady;
          const { error } = await supabase.auth.signInWithPassword({ email, password });
          if (error) throw new Error(`Sign-in failed: ${error.message}`);
        },
        { email, password },
      );

    // Wait for the profile page's eviction ping so the upload has fully applied
    // (preview updated + server cache dropped) before we read the URL or navigate away.
    const uploadAvatar = async (file: string) => {
      const evicted = page.waitForResponse((r) => r.url().includes("/api/users/me/refresh") && r.request().method() === "POST");
      await page.locator("#avatar-file-input").setInputFiles(file);
      await evicted;
      return page.locator("#avatar-preview-img").getAttribute("src");
    };

    // Sign in on the profile page and set an initial avatar.
    await page.goto("/profile");
    await signIn(avatarChangeUser);
    await expect(page.locator("#avatar-change-btn")).toBeVisible();
    await uploadAvatar(portrait200);

    // Post a comment, then reload so it renders from the server with the resolved avatar.
    await page.goto(POST_PATH);
    const comments = page.locator('section[aria-label="Comments"]');
    const commentText = `E2E avatar-change ${Date.now()}`;
    await comments.getByRole("textbox").fill(commentText);
    await comments.getByRole("button", { name: "Post comment" }).click();
    await expect(comments.locator("li").filter({ hasText: commentText }).first()).toBeVisible();

    await page.reload();
    const firstAvatar = page
      .locator('section[aria-label="Comments"]')
      .locator("li")
      .filter({ hasText: commentText })
      .first()
      .locator("img")
      .first();
    await expect(firstAvatar).toBeVisible();
    const firstSrc = await firstAvatar.getAttribute("src");
    expect(firstSrc).toContain("/storage/v1/object/public/avatars/");

    // Change the avatar; the profile page evicts this user's server-side cache.
    await page.goto("/profile");
    await expect(page.locator("#avatar-change-btn")).toBeVisible();
    const newAvatarSrc = await uploadAvatar(portrait400);
    expect(newAvatarSrc).not.toBe(firstSrc);

    // Revisit the post — the past comment reflects the new avatar, not the cached one.
    await page.goto(POST_PATH);
    const finalAvatar = page
      .locator('section[aria-label="Comments"]')
      .locator("li")
      .filter({ hasText: commentText })
      .first()
      .locator("img")
      .first();
    await expect(finalAvatar).toBeVisible();
    await expect.poll(async () => finalAvatar.getAttribute("src"), { timeout: 10000 }).toBe(newAvatarSrc);
  });
});
