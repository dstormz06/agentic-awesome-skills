# Differential check: the app's notes reader against the browser simulation's copy

The simulation (`mockup/windows-simulation.html`) carries a JavaScript copy of `NoteParser`. A small shared fixture (`mockup/checklist-golden.json`) keeps the two equal on 51 hand-written
cases; this check compares them on thousands of random notes (typos, other scripts, odd digits, long runs).

```bash
SEED=777 N=5000 node gen.js /tmp/notes.json
ANDA_FUZZ_IN=/tmp/notes.json ANDA_FUZZ_OUT=/tmp/app.json dotnet test ../ --filter FullyQualifiedName~FuzzTests
node compare.js /tmp/notes.json /tmp/app.json        # prints how many notes were read identically
```

Round 16 result: 22,570 random notes (about 54,000 items) across five seeds, read identically.
