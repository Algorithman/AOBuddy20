# AOBuddy20 rules

Binding for everyone working on this repo, people and AI alike.

1. **Never assume, never guess. Verify first.** Before you change code or say how the game or the bot behaves, check it against the data and cite what you found. If you can't check it, stop and go look.
2. **Cite the evidence.** Name the exact source: a line in the bot log, a decoded capture, or a file:line in the code. No evidence means no change.
3. **The wire is the truth.** Packet layouts come from OmniCell's message library, which decodes every recorded packet with nothing left over. A layout change is verified against the recordings (0 throws, 0 bytes left over) before it ships.
4. **No hardcoding and no per-character values.** Perks, stats, nanos, items and profession all come from the character's own data on the wire, so the bot works for any class or character.
5. **The owner's description of game mechanics is ground truth.** When he states how something works, that is the starting point, not something to argue with.
6. **No pretending, no half-measures.** If something can't be done right yet, say so plainly and set out the real options.
7. **One verified fix at a time.** Accuracy first, speed second.
8. **A deep dive means exhaustive.** Cover all of the data, not a sample or the top picks.
9. **Don't edit someone else's code unasked.** Report the finding, with evidence, to the person who owns that code.
10. **Restart a running bot only when it's safe:** out of combat, not resting, not dead, and HP at 80% or more.
11. **Commit locally. Push only when the owner says so.**
