---
name: aeginext-doc
description: Create, update, or review stable AegiNext documentation for user Quick Start guides and developer understanding of the project. Prefer automatically captured, annotated screenshots of the real app in dark mode for user guides. Applies to README files and maintained guides under docs; excludes AI session artifacts, temporary plans, validation reports, and Word document creation.
---

# AegiNext project documentation

Documentation serves two audiences: users completing their first workflow, and developers understanding the project and contributing to it. Write maintained guides around implemented, verifiable behavior.

Locate the checkout with `git rev-parse --show-toplevel`, or resolve the root three directories above this skill's `.agents/skills/aeginext-doc/` location. Read the documentation index for the relevant language and the guide being edited, then inspect actual code, configuration, build scripts, or the running app. Existing documentation helps navigation but does not replace verification.

## Content and ownership

| Audience and content | Location and approach |
|---|---|
| User Quick Start | `docs/zh-cn/quick-start.md` and `docs/en/quick-start.md`; explain actions and visible results in the order of creating a project, opening media, making subtitles, saving, and exporting |
| Supporting user topics | Extend existing workbench, subtitle editing, effects, or export guides; keep Quick Start concise and link to supporting details |
| Developer understanding | Explain module responsibilities, dependency direction, key contracts, building, and scoped verification in existing building, architecture, workspace integration, media, or rendering guides |
| Entry points and images | Keep an overview and guide links in README files, navigation in documentation indexes, and published images in `docs/assets/` |

- Preserve matching filenames, language-switch links, and relative-link conventions in `docs/zh-cn/` and `docs/en/`. Synchronize behavior descriptions when updating existing bilingual guides. Place new topics according to the task, creating a file only when existing guides cannot accommodate it.
- Use menu, panel, and button names actually shown in the app. Include necessary prerequisites, action order, and results. Developer guides should contain details needed to understand responsibilities and contracts; avoid repeating implementation line by line or cataloging every class and method.
- Update relevant guides when user workflows or project contracts change, or when explicitly requested. Do not present unimplemented proposals, roadmaps, session progress, or temporary verification results as established capabilities.

## Keep docs stable

- Never place AI tool session artifacts in `docs/`: chat transcripts, prompts, session summaries, task breakdowns, temporary designs, agent handoffs, review or test reports, logs, performance samples, or temporary verification screenshots.
- Store working artifacts in a system temporary directory or the project's existing isolated artifact directories. Skills belong in `.agents/skills/`; reusable tools belong in `scripts/` or their owning `Tests/` project according to project conventions. Do not place session scripts in docs as part of a documentation task.
- Turn verified conclusions into standalone maintained explanations instead of copying sessions or reports. Keep examples only when readers need them, using existing example directories.
- New images in `docs/assets/` must serve a maintained guide or README. Keep raw captures, bulk screenshots, comparison images, and intermediate annotation files in isolated directories. Publish only final images referenced by the documentation.

## Illustrate user guides

Prefer instructions accompanied by screenshots and annotations for actions that require locating an entry point, identifying a region, or checking a result. Simple shortcuts or single-sentence explanations do not automatically need images.

1. Run the real AegiNext app using its existing launch workflow. Explicitly select dark mode in the app's appearance settings. Use the guide's interface language and a reproducible demonstration project, and wait for media, layout, and dialog state to stabilize.
2. Prefer existing automation or desktop automation tools to perform actions and capture the app window or relevant region. The theme, controls, content, and results must come from the real app. Do not use AI-generated graphics, fabricated interfaces, or recoloring to simulate dark mode.
3. Add numbers, arrows, or boxes to real screenshots to identify each step's target, matching numbers to the written steps. Use colors readable against dark backgrounds, keep control labels, input values, and results visible, and provide descriptive image alt text.
4. Prefer PNG for legible UI text and stable, descriptive filenames. English and Chinese guides may share language-independent images; use matching-language images when interface text or annotations depend on the language.
5. Inspect text size, cropping, annotation targets, and correspondence with the instructions. Headless test results cannot replace screenshots of real app workflows. Restore personal theme, language, or layout settings changed during capture when finished.

If the environment cannot run the app or capture screenshots automatically, finish the verifiable text and report the missing images and reason at delivery. Retain existing images that remain accurate. Involve the user in complex visual acceptance; never claim captures or acceptance checks that were not performed.

## Verify and deliver

- Verify actions, names, commands, and contracts against their implementations. Run only necessary verification relevant to the behavior being documented.
- Check navigation, relative links, image references, language correspondence, and examples in modified pages. Open new or updated final images for visual inspection.
- Run `git diff --check` and inspect new files to ensure no session artifacts have entered docs.
- Briefly report the updated guides and images, what was verified, and any missing screenshots or pending visual acceptance.
