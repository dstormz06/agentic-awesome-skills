# Differential check: the app's notes reader against the browser simulation's copy

The simulation (`mockup/windows-simulation.html`) carries a JavaScript copy of `NoteParser`. A small shared fixture (`mockup/mytodo-golden.json`) keeps the two equal on 53 hand-written
cases; this check compares them on thousands of random notes (typos, other scripts, odd digits, long runs).

```bash
SEED=777 N=5000 node gen.js /tmp/notes.json
ANDA_FUZZ_IN=/tmp/notes.json ANDA_FUZZ_OUT=/tmp/app.json dotnet test ../ --filter FullyQualifiedName~FuzzTests
node compare.js /tmp/notes.json /tmp/app.json        # prints how many notes were read identically
```

Round 16 result (after the redo): 32,168 random notes (about 77,000 items) across eight seeds, including notes longer than the reading limit, read identically; an earlier 64,000 notes on the same rules gave the same answer.
