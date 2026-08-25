# Improvements

## Block A — The Windows engine (Docker without Docker Desktop)

## Block B — The daemon client (talk to the engine)

## Block C — The window (claude-tray's elements)

## Block D — Container operations (what a user came to do)

## Block E — Images, volumes and networks

## Block F — Installer and distribution (free, Apache 2.0)

## Block G — The agent surface (an agent operates this, and pays in tokens)

## Block H — The public surface (the site a reader and an agent both read)

### §DD185 DD185 — the em dashes the site cannot fix for itself

DD184 took the em dash out of every sentence this repository writes for a reader. It
could not take it out of the sentences the repository quotes.

`preflightTerminal` in `diagrams.ts`, three fenced blocks in `README.md` and the mock
window in the SVG diagrams reproduce what the product prints, and the em dashes in them
belong to `ReportText.cs`, `PreflightInspection.cs`, `StateIcon.cs` and `BuildRow.cs`.
S1 says a depicted surface is the one the build produces, so editing the quotation to
satisfy a writing rule would trade a style defect for a false claim. Six survive into
the published Markdown twins because of it, and they are the only ones left.

The source holds 64 of them across 27 files, in strings the window, the tray tooltip and
the CLI print. That is a change to the product rather than to the copy, and it is not
free: the preflight tests assert their report text, the window captures are compared
byte for byte, and `StateIcon`'s tooltip is what the tray shows. Each string wants the
same judgement DD184 applied one sentence at a time, which is why this is a task and not
a sweep.

Out of scope, deliberately: the 721 em dashes in C# doc comments. They are not published
and no reader meets them.
