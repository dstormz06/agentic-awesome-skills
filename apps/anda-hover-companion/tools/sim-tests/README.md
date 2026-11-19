# Browser suites for the simulation

These 42 scripts drive `mockup/windows-simulation.html` in a real browser and check what it does: every window, the keyboard, the
accessibility scan, phone width, and the rules the Windows app and the extension must answer the same way. They were kept outside the
repository until round 28; they are here so that they cannot be lost and so that anyone can run them.

**They are for a development machine or a build server, not for the PC where the tool is used.** Using the simulation needs nothing
but a browser. Running these suites needs Node.js and two third-party development tools that this repository does not install:

- Playwright with its Chromium browser (`require('playwright')` must resolve);
- axe-core, for the accessibility scans. Either `require('axe-core')` resolves, or the environment variable `AXE_PATH` holds the full
  path of `axe.min.js`.

Run one suite, or all of them:

    node t36_round28.js
    node run-all.cjs mytag 3

`run-all.cjs` prints one line for each suite (its exit code, checks passed, checks failed) and the totals, and keeps each suite's full
output under the temporary folder. A run is clean only when every exit code is 0: please read them all, not only the totals.

Notes:

- Nothing here reaches the network. The pages are opened from disk, with the clock fixed at Jul 16, 2026 where a suite needs one day.
- All data in the suites is fictional.
- `gen_proofread_stress.js` and the `r*_parity.js` scripts check the shared answer files in `mockup/` and `extension/tests/`; with
  `WRITE=1` two of them write their file again, which is done only when a rule changes on purpose.
- `t11_matrix.js` and `t15_axe.js` can also check an older proposals page that is not in the repository; they do so only when
  `OPTIONS_PAGE` holds its path.
- Pictures that the oldest suites take are written to the folder given as the first argument, or to the temporary folder.
