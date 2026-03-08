# PM Trace Analysis Prompt Template

Use this template when asking analysis workflows to evaluate PM Trace coverage, generate configs, and (optionally) produce command scripts without reading implementation source by default.

```text
Use PM Trace contract artifacts as source-of-truth for this task.

Read only:
- /codex/HollowKnight.DebugMod/docs/pmtrace-capabilities.json
- /codex/HollowKnight.DebugMod/docs/pmtrace-config.schema.json
- /codex/HollowKnight.DebugMod/docs/pmtrace-record.schema.json
- /codex/HollowKnight.DebugMod/docs/pmtrace-command-contract.json
- /codex/HollowKnight.DebugMod/README.md (PM Trace section only)

Goal:
1) Determine whether PM Trace can capture signals needed for [ISSUE_LINK_OR_QUESTION].
2) If yes, generate a valid pmtrace_config.json optimized for this investigation.
3) Generate an execution command sequence (pmtrace commands) for repeatable capture.
4) If no, list exact missing signals and the minimum implementation delta needed.

Rules:
- Do NOT read full source unless the listed artifacts are insufficient or contradictory.
- If source read is required, explain exactly what artifact gap forced it and limit reads to the minimum files/lines.

Return:
- coverage verdict: yes / no / partial
- generated config JSON
- pmtrace command sequence (enable/disable/flush/status/dump as needed)
- expected output fields/events to inspect
- limitations and assumptions
```
