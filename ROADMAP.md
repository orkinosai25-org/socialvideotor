# SocialVideotor — Feature Roadmap & Issue Plan

> **Product Philosophy:** Generate multiple high-quality clip options fast → help users choose → lightly enhance → export cleanly.
> Not a full editor. Not a social scheduler.

---

## 🟢 Phase 1 — Core MVP (Weeks 1–2)

**Goal:** "Upload → come back later → download clips"

### Issue 1 — User Authentication & Dashboard

**Title:** User login and jobs dashboard

**Scope:**
- Email/social login
- Dashboard showing:
  - New upload
  - Processing jobs
  - Completed jobs
- Persistent state across sessions

**Out of scope:** Social platform direct posting, analytics

---

### Issue 2 — Asynchronous Video Upload & Job Queue

**Title:** Upload video & background processing

**Scope:**
- Upload MP4 video
- Accept upload, return immediately
- Job runs async
- User can close browser and return later
- Job status: Queued / Processing / Ready / Failed

**Out of scope:** Real-time streaming, multi-file batch upload

---

### Issue 3 — Raw Clip Generation ✅ (already implemented)

**Title:** Generate raw vertical clips

**Scope:**
- 5–10 clips per upload
- 9:16 vertical framing
- 15–60 seconds
- Clean cuts
- Voice-only (no music)
- Download support

---

### Issue 4 — Progress & Notifications

**Title:** Job progress tracking

**Scope:**
- Progress bar per job
- Estimated time remaining
- Completed notification (in-app, email optional)

**Out of scope:** Push notifications, SMS

---

## 🟡 Phase 2 — Platform Mediators (Weeks 3–4)

**Goal:** Make clips *platform-specific*, not generic.

### 🎥 YouTube Mediator

#### Issue 5 — YouTube Audio Safety Mode

**Title:** YouTube-safe audio processing

**Why:** YouTubers fear music claims

**Scope:**
- Remove background music by default
- Keep voice only
- "Shorts-safe" badge shown
- Manual override (Pro only)

---

#### Issue 6 — YouTube Clip Review UI

**Title:** YouTube clip watch & selection view

**Scope:**
- Large clip preview
- Loop playback
- Clip list (cards)
- Keep / discard selection
- Duration & quality indicator

---

#### Issue 7 — Title & Text Overlay (YouTube)

**Title:** Text overlays for YouTube Shorts

**Scope:**
- Starter: manual text entry
- Pro: title suggestions (3–5 per clip)
- Toggle overlay ON/OFF
- Position (top / bottom)

**Out of scope:** Animations, advanced typography

---

#### Issue 8 — Clickability Suggestions (YouTube Pro)

**Title:** Click & watch-time suggestions

**Scope:**
- Highlight hook strength
- Suggest "earlier hook"
- Duration advice
- No forced changes (advisory only)

**Out of scope:** A/B testing, analytics dashboard

---

### 📸 Instagram Mediator

#### Issue 9 — Instagram Reel Presets

**Title:** Instagram Reel clip presets

**Scope:**
- Faster pacing
- Reel-length presets
- Visual-first framing
- Optional captions

---

#### Issue 10 — Captions & Text Styling

**Title:** Auto captions and text styling

**Scope:**
- Auto captions (toggle)
- High-contrast styles
- Emoji emphasis (Pro)

**Out of scope:** Full subtitle editor

---

### 📘 Facebook Mediator

#### Issue 11 — Silent-First Clips

**Title:** Facebook silent-first clip mode

**Scope:**
- Captions ON by default
- Music OFF by default
- Longer clips allowed (30–90s)

---

### ❌ X (Twitter) Mediator

#### Issue 12 — Ultra-Short Clip Generation

**Title:** X/Twitter ultra-short clips

**Scope:**
- 10–30 second clips
- Aggressive hook cuts
- Minimal overlays

---

### 💼 LinkedIn Mediator

#### Issue 13 — Professional Clip Mode

**Title:** LinkedIn professional clip mode

**Scope:**
- Clean framing
- No emojis by default
- Longer durations (30–120s)
- Thought-leadership focus

---

## 🔵 Phase 3 — Pro Features & Polish (Weeks 5–6)

**Goal:** Drive upgrades and retention.

### Issue 14 — Per-Clip Music Upload

**Title:** Per-clip background music upload and auto mixing

**Scope:**
- User uploads music
- Auto-duck voice
- Loop/fade
- Per-clip only
- Off by default

**Out of scope:** Multi-track audio, beat matching, effects

---

### Issue 15 — Watermark Control & Entitlements

**Title:** Watermark control and plan entitlements

**Scope:**
- Free trial: watermark ON
- Any paid tier: watermark OFF
- Clear messaging ("$1 removes watermark")

---

### Issue 16 — Platform-Batch Export

**Title:** Batch export clips by platform

**Scope:**
- Export selected clips
- Platform naming presets
- Bulk download

---

## 🟣 Phase 4 — Advanced & Enterprise (Weeks 7+)

**Goal:** Upsell & acquisition value.

### Issue 17 — Social Mediator (All-in-One)

**Title:** All-in-one social mediator

**Scope:**
- Upload once
- Generate clips for all platforms
- Platform-specific presets in one run

---

### Issue 18 — Brand Templates (Agency)

**Title:** Agency brand templates

**Scope:**
- Logo overlay
- Font presets
- Color palettes
- Stored per account

---

### Issue 19 — Usage Limits & Billing (Stripe)

**Title:** Usage limits and Stripe billing

**Scope:**
- $0 free trial (watermark)
- $0.99–$1.99 Starter
- Plus / Pro / Agency tiers
- Upgrade in-place

**Out of scope:** Annual plans, coupons, complex billing

---

### Issue 20 — Analytics Lite (Optional)

**Title:** Lightweight usage analytics

**Scope:**
- Clip count per job
- Downloads per clip
- No social platform analytics

---

## ✅ Rollout Schedule Summary

| Phase | Duration | Outcome |
|-------|----------|---------|
| Phase 1 | Weeks 1–2 | Working MVP |
| Phase 2 | Weeks 3–4 | Platform differentiation |
| Phase 3 | Weeks 5–6 | Monetisation |
| Phase 4 | Ongoing | Scale & exit value |
