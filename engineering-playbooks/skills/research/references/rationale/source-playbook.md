# Source Playbook

Use only sources available in the current Codex environment and within the user's authority. A source name below is a category, not a required connector.

| Question | Start with | Guidance |
| --- | --- | --- |
| Why did this line or module appear? | [Code archaeology](sources/code-archaeology.md) | Repository history, docs, tests, linked reviews |
| What product decision drove it? | [Issue tracker](sources/linear.md) and [documents](sources/notion.md) | Requirements, decisions, meeting notes |
| What did the team discuss informally? | [Team chat](sources/slack.md) | Search around symbols, errors, dates, and authors |
| What production behavior justified it? | [Observability](sources/datadog.md), [errors](sources/sentry.md), and [analytics](sources/databricks.md) | Correlate code dates with measured behavior |
| Was it incident-driven? | [Incident records](sources/incident-postmortem.md) | Timeline, cause, remediation, follow-up |

Start close to the code, then widen. Prefer primary records written at decision time. Search is discovery, not proof: open the underlying item, capture its date and location, and distinguish explicit rationale from temporal correlation.

If an external source is unavailable, name the gap and lower confidence. Do not ask for credentials, invent connector calls, or imply a source was checked when it was not.
