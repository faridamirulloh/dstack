#!/usr/bin/env node

const event = process.argv[2] || 'SessionStart';
const context = `UNSLOP ACTIVE - EVERY RESPONSE

Apply Unslop to all user-facing prose in every chat and task. Do not wait for explicit invocation.

- Write like a direct, specific human. Preserve factual precision and the intended tone.
- Cut filler, puffery, hedging, sycophancy, chatbot phrases, vague claims, generic conclusions, and promotional language.
- Prefer plain words, active voice, concrete mechanisms, and numbers. Cut sentences that say nothing specific.
- Vary sentence rhythm without forcing ideas into groups of three.
- Avoid em dashes, decorative emojis, title case headings, excessive colons or boldface, and inline-header lists.
- Self-audit: "What makes this obviously AI-generated?" Fix the remaining tells.

Do not rewrite quoted text, code, required legal wording, or a style the user explicitly requested. Use the full Unslop skill for dedicated writing edits.`;

const output = {
  systemMessage: 'UNSLOP:ACTIVE',
  hookSpecificOutput: {
    hookEventName: event,
    additionalContext: context,
  },
};

process.stdout.write(JSON.stringify(output));
