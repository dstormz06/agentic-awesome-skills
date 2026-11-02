// Generated from the app's fictional demo records by ExtensionGoldenTests. Do not edit: set ANDA_WRITE_GOLDEN=1 and run that test.
export const DEMO_RECORDS = [
  {
    "number": "216482",
    "kind": "ANDA",
    "drug": "Metformin HCl ER Tablets, 500 mg",
    "meta": "Meridian Pharmaceuticals (fictional) \u00B7 Cycle 2",
    "goalDate": "Oct 3, 2026",
    "goalDays": "\u00B7 80 days",
    "daysToGoal": 80,
    "forecastDeltaDays": 14,
    "progressPct": 62,
    "forecastDate": "Oct 17, 2026",
    "forecastDelta": "\u25B2 \u002B14 d vs goal",
    "forecastLate": true,
    "forecastConfidence": "moderate confidence",
    "attention": {
      "level": "warn",
      "text": "IR response due in 6 days \u2014 Biopharmaceutics"
    },
    "latest": "Amendment (minor) received Jun 30 \u2014 under review",
    "dataAsOf": "data as of Jul 16, 07:12 \u00B7 extract #412 (fictional)",
    "ahead": "Thu huddle \u2014 status requested on this ANDA \u002B sister 214990 (memo, Jul 14)",
    "pending": [
      {
        "title": "IR response \u2014 Biopharmaceutics",
        "due": "due Jul 21",
        "urgent": true
      },
      {
        "title": "Labeling carve-out review",
        "due": "in queue",
        "urgent": false
      }
    ],
    "notes": [
      {
        "who": "You \u00B7 Jul 8",
        "text": "Applicant confirmed receipt of IR; response expected on time."
      },
      {
        "who": "You \u00B7 Jun 30",
        "text": "Minor amendment logged; routed to OPQ same day."
      },
      {
        "who": "Team lead \u00B7 Jun 12",
        "text": "Watch facility linkage with 214990 \u2014 same site."
      }
    ],
    "forecastDetail": "Baseline from comparable cycle-2 reviews: Oct 10. Adjusted \u002B7 days for this applicant\u0027s IR-response history; band \u00B15 days. Assumptions: first-pass IR response; no new facility inspection. Tool calibration, trailing 6 months: median error \u00B18 days.",
    "sisters": [
      {
        "number": "214990",
        "description": "Metformin HCl ER Tablets, 750 mg \u2014 same applicant, same facility"
      }
    ],
    "goalDateValue": "2026-10-03",
    "forecastDateValue": "2026-10-17",
    "submittedDate": null,
    "asOfDate": "2026-07-16",
    "demo": true,
    "sourceLabel": "Fictional demo data",
    "activeIngredient": "METFORMIN HYDROCHLORIDE",
    "dosageForm": "TABLET, EXTENDED RELEASE",
    "strength": "500 MG",
    "psgDateFromSheet": null
  },
  {
    "number": "214990",
    "kind": "ANDA",
    "drug": "Metformin HCl ER Tablets, 750 mg",
    "meta": "Meridian Pharmaceuticals (fictional) \u00B7 Cycle 3",
    "goalDate": "Aug 15, 2026",
    "goalDays": "\u00B7 31 days",
    "daysToGoal": 31,
    "forecastDeltaDays": -9,
    "progressPct": 88,
    "forecastDate": "Aug 6, 2026",
    "forecastDelta": "\u25BC 9 d ahead of goal",
    "forecastLate": false,
    "forecastConfidence": "high confidence",
    "attention": null,
    "latest": "Facility IR resolved \u2014 closed Jul 2",
    "dataAsOf": "data as of Jul 16, 07:12 \u00B7 extract #412 (fictional)",
    "ahead": "Aug 1 \u2014 labeling final-format confirmation due from applicant",
    "pending": [],
    "notes": [
      {
        "who": "You \u00B7 Jul 2",
        "text": "Facility IR closed; memo filed."
      },
      {
        "who": "You \u00B7 Jun 20",
        "text": "Labeling agreed; final format pending."
      }
    ],
    "forecastDetail": "Baseline from comparable cycle-3 reviews: Aug 8. Adjusted \u22122 days: all disciplines complete or scheduled; band \u00B13 days. Assumption: no late labeling revisions. Tool calibration, trailing 6 months: median error \u00B18 days.",
    "sisters": [
      {
        "number": "216482",
        "description": "Metformin HCl ER Tablets, 500 mg \u2014 same applicant, same facility"
      }
    ],
    "goalDateValue": "2026-08-15",
    "forecastDateValue": "2026-08-06",
    "submittedDate": null,
    "asOfDate": "2026-07-16",
    "demo": true,
    "sourceLabel": "Fictional demo data",
    "activeIngredient": "METFORMIN HYDROCHLORIDE",
    "dosageForm": "TABLET, EXTENDED RELEASE",
    "strength": "750 MG",
    "psgDateFromSheet": null
  }
];
