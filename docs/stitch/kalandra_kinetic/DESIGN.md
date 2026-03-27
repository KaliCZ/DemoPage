```markdown
# Design System Strategy: www.kalandra.tech

## 1. Overview & Creative North Star: "The Digital Architect"
This design system is built to reflect the intersection of high-level engineering leadership and refined personal curation. The **Creative North Star** is "The Digital Architect"—a philosophy that values structural integrity, expansive breathing room, and intentional depth. 

Instead of a standard "boxed" portfolio, we utilize an editorial layout that breaks the traditional grid. We move away from the "template" look by employing intentional asymmetry, wide margins (using our `24` spacing token), and overlapping typographic elements. The experience should feel like a premium digital monograph: authoritative yet approachable, technical yet tactile.

---

## 2. Colors & Tonal Depth

The palette is anchored in a sophisticated Indigo (`primary`) and a Refined Teal (`tertiary`), designed to create a sense of calm authority. 

### The "No-Line" Rule
To achieve a high-end feel, **1px solid borders are prohibited for sectioning.** Boundaries must be defined solely through background color shifts. For example, a content section using `surface-container-low` should sit directly against a `surface` background. The shift in tone provides a sophisticated "edge" that lines cannot replicate.

### Surface Hierarchy & Nesting
Treat the UI as a series of physical layers. Use the surface-container tiers to create "nested" depth:
- **Level 0 (Base):** `surface` (#131313) for the main page background.
- **Level 1 (Sectioning):** `surface-container-low` (#1C1B1B) for large content blocks.
- **Level 2 (Interactive):** `surface-container-high` (#2A2A2A) for cards or highlighted code snippets.
- **Level 3 (Prominence):** `surface-container-highest` (#353534) for floating elements or active states.

### The "Glass & Gradient" Rule
Standard flat colors feel static. To inject "soul" into the engineering narrative:
- **Glassmorphism:** Use `surface` colors at 70% opacity with a `backdrop-blur` of 12px for navigation bars and floating action menus.
- **Signature Gradients:** For primary CTAs or Hero backgrounds, transition from `primary` (#BAC3FF) to `primary-container` (#4453A7) at a 135-degree angle. This adds a subtle 3D quality to the "Architect" aesthetic.

---

## 3. Typography: The Editorial Scale

We use a tri-font system to separate technical data from leadership narrative.

- **Display & Headlines (Manrope):** Used for "The Narrative." These should be set with tight letter-spacing (-0.02em) to feel impactful. `display-lg` (3.5rem) should be used sparingly to anchor major sections.
- **Body & Titles (Inter):** Used for "The Detail." Inter provides maximum readability for long-form technical leadership articles. Use `body-lg` (1rem) for standard reading to ensure the experience feels "premium" rather than "dense."
- **Labels (Space Grotesk):** Used for "The Metadata." This monospaced-leaning sans-serif should be used for code labels, dates, and tags (`label-md`). It signals engineering precision.

---

## 4. Elevation & Depth

### Tonal Layering
Depth is achieved through the "stacking" of color tokens rather than drop shadows. 
*Example:* A technical project card (`surface-container-highest`) placed on a project gallery section (`surface-container-low`) creates a natural, soft lift.

### Ambient Shadows
When a floating element is functionally necessary (e.g., a mobile menu), use **Ambient Shadows**:
- **Color:** A tinted version of `on-surface` at 6% opacity.
- **Blur:** 32px to 64px. 
- **Effect:** The shadow should feel like a soft glow of light being blocked, not a black "drop shadow" under a box.

### The "Ghost Border" Fallback
If accessibility requirements demand a container boundary, use the **Ghost Border**:
- **Token:** `outline-variant` (#454652) at 15% opacity. 
- **Rule:** It must feel like a suggestion of a container, never a hard cage.

---

## 5. Components

### Buttons
- **Primary:** Gradient fill (`primary` to `primary-container`), `roundness-md` (0.375rem). No border.
- **Secondary:** Ghost style. `outline-variant` ghost border (20% opacity) with `on-surface` text.
- **Tertiary:** Text-only using `tertiary` (#76D6D5). Use for "Read More" or "View Source."

### Cards & Lists
**Forbid the use of divider lines.** 
- Separate list items using the `spacing-4` (1.4rem) scale. 
- For cards, use a subtle background shift to `surface-container-low` on hover to indicate interactivity.

### Code Blocks
- **Background:** `surface-container-lowest` (#0E0E0E).
- **Styling:** Use a `1.5` (0.5rem) padding scale. 
- **Syntax:** Vibrant `tertiary` for functions and `primary` for variables.

### Chips (Tech Stack)
- **Style:** Small, high-contrast. `surface-variant` background with `label-sm` (Space Grotesk) typography. `roundness-full` to contrast against the architectural squareness of the layout.

---

## 6. Do’s and Don’ts

### Do:
- **Embrace Asymmetry:** Align a headline to the left and body text to a 60% width column on the right.
- **Use "White Space" as a Tool:** Use the `20` (7rem) and `24` (8.5rem) spacing tokens to separate major narrative shifts.
- **Prioritize Typographic Hierarchy:** A user should understand your leadership philosophy just by reading the `display` and `headline` text.

### Don’t:
- **Don’t use 100% opaque borders:** They clutter the "Architect" aesthetic and look like a default bootstrap theme.
- **Don’t use standard shadows:** If the shadow is clearly visible as a "dark smudge," it is too heavy.
- **Don’t crowd the content:** Engineering leadership is about clarity. If a screen feels busy, increase the spacing tokens by one level.

### Accessibility Note
Ensure all text combinations (especially `secondary-text` on `surface-containers`) maintain a contrast ratio of at least 4.5:1. Use the `primary-fixed` and `secondary-fixed` tokens for high-contrast elements in dark mode.```