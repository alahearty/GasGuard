# UI Text Audit

Date: 2026-10-29

This audit checked all WPF XAML and UI-facing source strings for AI/artificial-intelligence claims,
including `AI`, `artificial intelligence`, `AI-powered`, `machine learning`, `LLM`, `GPT`, `neural`,
and `smart` wording.

## Result

No AI-related UI text was found. The application presents itself as:

- a gas-detection monitoring application;
- a GSM/SMS telemetry receiver;
- an incident and evidence-recording workspace;
- a simulated-modem demonstration application.

It does not claim to be AI-powered, to use machine learning, to make autonomous safety decisions, or
to replace a detector's local alarm.

No UI text change was required for this audit. The release package should be rebuilt from the audited
source and verified with the normal test/publish process.

## Safety wording retained

The UI intentionally retains safety-critical wording about:

- DEMO/simulated data;
- raw/uncalibrated readings;
- stale or offline detector state;
- independent local alarm requirements;
- the distinction between modem submission and delivery.

That wording is not AI-related and must not be removed.
