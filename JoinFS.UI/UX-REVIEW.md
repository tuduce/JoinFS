# UX review of the new UI

Reviewed on 2026-10-10, branch `ui-revamp` (head `5fd93e6`), from a functionality and usability point of view.

## Method and limits

- Screenshots of every tab and the main overlays, rendered with `JOINFS_UI_SCREENSHOTS=<folder> dotnet test JoinFS.UI.Tests --filter RenderTests`.
- A read of the AXAML, the view-models and the README.
- Measured against ISO 9241-110 (dialogue principles), Nielsen's heuristics, WCAG 2.2 and Windows conventions.
- **Limits.** All screenshots use fake data, and the Settings shots were caught mid-animation (the odd chevrons are not real). The live app was not driven in a simulator. Empty states and real error states were not seen.

### Standards or guidelines?

- **Standards:** ISO 9241 (Part 11 defines usability, Part 110 the dialogue principles, Part 210 the human-centred design process). WCAG 2.2 (ISO/IEC 40500) is the only one with testable pass/fail criteria. EN 301 549 and Section 508 make accessibility a legal requirement in some markets.
- **Guidelines:** Nielsen's 10 heuristics, Microsoft's Fluent/Windows design guidance, Shneiderman's rules. Widely accepted, but judged against, not certified against.

## What works well

- The connection strip (Simulator → Hub → Network) is visible on every tab: system status is always on screen, and it reads as a flow.
- The collapsed mini-window suits use while flying.
- The card/table/expand-row layout and the sortable headers repeat across tabs.
- Esc closes overlays except onboarding; Enter submits text fields; the ten global shortcuts are named in the buttons' tooltips.
- An unsaved recording is asked about; the update notice steps down from a card to a badge; settings apply at once with no OK/Cancel.

## Findings, most important first

### High

1. **[Fixed: a notice bar under the strip, and "Cancel" in the tooltip while connecting; see the README, "Live wiring".] Connection failures are silent.** `ConnectionViewModel.Error` is set but nothing shows it ("the design has no error UI yet"). A failed Join just returns to "Disconnected". Show the reason (wrong password, hub unreachable, no simulator). A click while "Connecting…" silently gives up: label it "Cancel".
2. **[Fixed: the Auto refresh and Tool Tips settings and the Refresh buttons are removed; the lists always poll and the tool tips are always on.] Controls that do nothing.** *Auto refresh* and *Tool Tips* are saved but not used by the new UI (README, "IPreferencesStore"); tool tips are hard-wired on. Every tab also has a Refresh button although the lists poll every second. Remove the settings or make them work, and drop the redundant Refresh buttons.
3. **[Fixed: the disconnected connectors are now neutral grey, see `util/gen_tokens.py`.] Red is overused.** A fresh start shows three red circles, which reads as three errors, and "Not loaded" for the optional Flight Plan is red too. Disconnected is a neutral state: use grey, and keep red for failures. State also rests largely on colour with an unchanged icon; the text labels help, but WCAG 1.4.1 wants a second non-colour cue.
4. **One control, two meanings.** The strip's Flight Plan button fetches SimBrief and sends the plan to the network at once. The tab's "Import from SimBrief" only fills the form, to be checked and saved. The README documents it, users will not read it. Differentiate the labels or the behaviour.
5. **[Fixed: unticking asks "Leave out of playback?" first, and the grey tick has a tooltip saying why. Re-ticking is still postponed.] Un-re-tickable checkbox in the Recorder.** Unticking an aircraft in "Loaded recording" cannot be undone until the file is reloaded (re-ticking is postponed, see "Left for later" in the README). Disable it with an explanation, or use a button with a confirmation.
6. **[Fixed: the two switches read "Log network traffic" and "Log variables"; the two dumps are plain buttons under "Write to the log".] Monitor chips look like filters but are not.** "Show (this session)" implies a view filter. Network and Variables switch logging on; Node Statistics and Received Packets are one-shot actions in toggle styling. Make the actions plain buttons and relabel the toggles ("Log network traffic").

### Medium

7. **[Fixed: the two filters are ticks in the footer, the record column and link are gone (the Recorder tab chooses), and the links are in three groups, Control, Model and Other.] The Aircraft row mixes scopes.** "Include All Hub / Simulator Aircraft" are list filters among per-aircraft actions (Follow, Ignore). Move them to the footer, as Objects does with "List ignored objects". Twelve links in a three-column grid is a lot to scan: group them (Control, Match, Record, Filter).
8. **[Fixed: the Objects boxes sit beside the row button, not inside it; every row has a chevron, a name and a help text for screen readers. Row actions are still behind the expand click.] Rows are one big Button with a CheckBox nested in it.** Nested interactive controls cause tab-order and announcement problems for the keyboard and screen readers, and rows have no `AutomationProperties` (a row is read as one blob). Row actions are also hidden behind the expand click. Use a separate expand chevron, or a grid-style row.
9. **No confirmation or undo for destructive actions.** Address Book "Remove" and "Ignore" act at once. Only the Recorder uses `ConfirmViewModel`. Ignored items are hard to find again when "list ignored" is off.
10. **Jargon without help text.** "Create Your Own Mesh" shows `40383 51901` unexplained. "Circle of activity", "Sub Model" and "Original Model" are undefined. A bare "Password" in the Network card does not say whether it is for your own hub or for joining. Add one-line helper text, as the Scan and Keyboard cards already do.
11. **[Fixed: Simulator is split into Profile (with the nickname), Simulator and Models; the cards run from everyday to advanced, with Hub Mode and X-Plane under an "Advanced" caption and a line saying what Hub Mode is; a card that opens out of sight is scrolled into view; the setting labels are in sentence case. Not done: search, and the accordion stays single-open.] Settings information architecture.** Nickname sits under "Simulator"; "Hub Mode (Public)" (five fields, advanced) sits beside everyday options. The accordion is single-open, so long cards scroll inside a scrolling page. No search. Casing is inconsistent ("include AI", "Tool Tips", "Elevation Correction").
12. **Flight Plan form.** No validation (ICAO codes, altitude format) and no dirty-state or "saved" feedback. The most common action, SimBrief import, is a small link below the fields while Save is the primary button: put the import near the top.
13. **Onboarding "Continue" is disabled without a hint** that 2+ characters are needed, and it does not say what the nickname is used for.
14. **Accessibility details.**
    - Informational text uses `DisabledBrush` (`#8D8F92`, about 3.3:1 on white), e.g. the Recorder's "/ 01:32" total; it fails 4.5:1.
    - 11–12 px text is common for data.
    - Table checkboxes have `MinHeight="0" Padding="0"`, probably under the 24 px target size (WCAG 2.5.8).
    - No focus-trapping code was found for overlays, so Tab can probably reach the controls behind a modal. Verify.
    - Only a light palette is defined, which is glaring in a dark cockpit room.

### Low

15. **Chat.** No timestamps, no distinction for your own messages, no unread separator, no hint that notes vanish after 10 minutes, and the `.help` command is undiscoverable.
16. **Blank and unexplained cells.** The Owner cell is blank for simulator-own aircraft (e.g. LV-ALB): show "Simulator" or "—". Red distance (`IsFar`) has no legend.
17. **Home.** Map labels overlap (A320 / 9H-WDR). The fake data shows "Connected users 8" and a full map while the network is disconnected: check that live data never shows stale numbers in that state.
18. **Frameless window.** `ExtendClientAreaChromeHints="NoChrome"` with custom caption buttons usually loses the Windows 11 Snap Layouts flyout on Maximize (not verified here). The window does not appear to remember its position and size (only the startup anchoring code was read).
19. **Language.** Follows the OS only, with no in-app switch. Acceptable; noted.
20. **Model Matching.** No filter or search, and nothing explains what Original → Substitute means to a newcomer. Check the empty state.

## Suggested order

- **First (cheap, removes the sharpest edges):** #1, #3, #2, #5.
- **Then (small relabel-and-move jobs):** #6, #7.
- **Settle with users, not heuristics:** #4, #10, #12. Watch two or three pilots join a hub and import SimBrief cold, without help.

A heuristic review finds likely problems, not how often real users hit them; the evening in-sim tests are better evidence for severity.
