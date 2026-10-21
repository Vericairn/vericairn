# First-party policy and duty adoption proposals

Revision 4, October 20, 2026. Clarify settled parallel results and prospective work-state consistency for adoption, and the release-set location of the unchanged register-coverage parameters.

These are proposed definitions for the builder and POLICY-04. Adoption belongs to the operator's exact signed decision and authorized declarations after ESTATE-01 reconciliation. D18-4 is decision `01M54ZFDD1MG1GS49CQA56J6QW` at `sha256:086843a4140121cea86c686b496517c5f0058e82faf9bc7822fbefed3af0f3bc`; it selects the levels, builder ownership, 30-day exception limit and publication cohort. The adoption decision must also identify the accepted ESTATE-01 result and DESIGN-18 acceptance.

This is preparation for an estate running 0.8.0 at record format 3. Nothing here declares a policy, grants authority, records an exception, places work or edits the estate register. The only substitution fields are the exact adoption decision id/hash and UTC instant. Substitute the instant first, compute each canonical definition hash with only `basis` omitted, then obtain the operator's signed decision and insert its exact reference. Each requirement named `key-definition` approves `declare` for that policy key and hash. The historical decisions below do not provide those format-3 approvals.

The proposal is not yet wholly submit-ready. The 0.8.0 publication boundary, the builder's writing-grant reference and release-carrier identities cannot be resolved before their records exist. Six M4 obligation gates and the compound Knowledge retirement condition also lack faithful mappings to the current gate kinds. These gaps have explicit dispositions below; they are not fabricated references or quiet exceptions. Reprepare the affected bodies and obtain exact approval after resolving them. READ-04 may build after POLICY-03, but its first-party adoption still follows POLICY-04 on the upgraded estate; neither work closes merely because its product changes merge (decision `01M593YGRB18SCD5YCS4TZ2ZJD`, hash `sha256:28df4953293fe9864d5295a4ca54b5184d16b551f3728eb0fe6d928fd58bdb07`).

## Proposed release-devlog body

This body is conditional: `after-declaration` covers 0.8.0 only if its first recorded publication follows the declaration. Upgrading to 0.8.0 does not establish that ordering. If 0.8.0 has already been observed, replace this value with that publication's exact release reference before hashing and approval; that exact reference is unavailable in this preparation. Never approve the conditional body after that observation, because it would exclude the first required publication. Later policy revisions preserve the first declaration boundary. The source directory contains Internal draft devlogs whose first retained revisions follow their exact publications; each current revision needs an independent ordinary review. This remaining boundary is an adoption blocker, not a retrospective quota for earlier versions.

```json
{
  "key": "release-devlog",
  "title": "A reviewed devlog for every publication",
  "owner": "01M4TRNE1GYEK4P8MXQAB3C8Q4",
  "reason": "Each publication leaves a historical account before a later release of that product is submitted.",
  "basis": [
    {
      "decision": {
        "type": "decision",
        "id": "01M54ZFDD1MG1GS49CQA56J6QW",
        "hash": "sha256:086843a4140121cea86c686b496517c5f0058e82faf9bc7822fbefed3af0f3bc"
      }
    },
    {
      "decision": {"type": "decision", "id": "<exact adoption decision id>", "hash": "<exact adoption decision hash>"},
      "requirement": "release-devlog-definition"
    }
  ],
  "state": "active",
  "appliesTo": {"kind": "estate"},
  "rules": [
    {
      "key": "entry",
      "kind": "vericairn.estate/release-entry",
      "version": 1,
      "parameters": {
        "product": "01M4TRNE1GWEAQ17M4QCDHXEZ5",
        "entryKind": "devlog",
        "source": "estate-draft/1",
        "directory": "docs/knowledge/devlog",
        "fromPublication": "after-declaration",
        "review": "independent"
      }
    }
  ],
  "level": "refuse",
  "startsAt": "<adoption UTC instant>",
  "maxExceptionDays": 30
}
```

## Proposed register-coverage body

The product repository resource is `01M4TRNE1GB7CYB7DND8GBTTM1`. The register belongs to estate repository `01M4TRNF0RZE2VEZQYP9WK3GA4`, at `design/vocabulary.md`. The unchanged `referenceManifest: reference-inventory.json` parameter is relative to `docs/releases/<version>/` at the selected product commit. The adapter reads that inventory and all three sibling generated references together. At status, version and commit come from the estate-pinned publication; at submission they come from the candidate. The inventory's named generation commit must be locally available with identical build source trees, as described in [Policy evaluator](policy-evaluator.md). The mappings below must be reconciled again against the build actually adopted.

```json
{
  "key": "register-coverage",
  "title": "Define every generated reference concept",
  "owner": "01M4TRNE1GYEK4P8MXQAB3C8Q4",
  "reason": "Every public command, record type and API operation needs a defined concept in the register, with editorial review remaining separate.",
  "basis": [
    {
      "decision": {
        "type": "decision",
        "id": "01M54ZFDD1MG1GS49CQA56J6QW",
        "hash": "sha256:086843a4140121cea86c686b496517c5f0058e82faf9bc7822fbefed3af0f3bc"
      }
    },
    {
      "decision": {"type": "decision", "id": "<exact adoption decision id>", "hash": "<exact adoption decision hash>"},
      "requirement": "register-coverage-definition"
    }
  ],
  "state": "active",
  "appliesTo": {"kind": "estate"},
  "rules": [
    {
      "key": "references",
      "kind": "vericairn.software/reference-coverage",
      "version": 1,
      "parameters": {
        "product": "01M4TRNE1GWEAQ17M4QCDHXEZ5",
        "repository": "01M4TRNE1GB7CYB7DND8GBTTM1",
        "referenceManifest": "reference-inventory.json",
        "registerRepository": "01M4TRNF0RZE2VEZQYP9WK3GA4",
        "registerPath": "design/vocabulary.md",
        "categories": ["command", "record-type", "api-operation"]
      }
    }
  ],
  "level": "warn",
  "startsAt": "<adoption UTC instant>",
  "maxExceptionDays": 30
}
```

## Remaining initial policy bodies

Each block is a complete proposed `estate-policy` body. The adoption decision approves `declare` for the named key and its `key-definition` requirement after the instant is substituted and the canonical definition hash is computed with only `basis` omitted. Historical decisions supply rationale; they are not format-3 exact-definition approvals. The operator declares the policies; the builder receives only the separately bounded writing scope.

### planned-placement

```json
{
  "key": "planned-placement",
  "title": "Give planned work an explicit place",
  "owner": "01M4TRNE1GYEK4P8MXQAB3C8Q4",
  "reason": "Keep future work out of completed release plans.",
  "basis": [
    {
      "decision": {
        "type": "decision",
        "id": "01M54ZFDD1MG1GS49CQA56J6QW",
        "hash": "sha256:086843a4140121cea86c686b496517c5f0058e82faf9bc7822fbefed3af0f3bc"
      }
    },
    {
      "decision": {
        "type": "decision",
        "id": "<exact adoption decision id>",
        "hash": "<exact adoption decision hash>"
      },
      "requirement": "planned-placement-definition"
    }
  ],
  "state": "active",
  "appliesTo": {
    "kind": "estate"
  },
  "rules": [
    {
      "key": "planned-placement",
      "kind": "vericairn.estate/placement",
      "version": 1,
      "parameters": {
        "backlogs": [
          {
            "key": "future-work",
            "title": "Future work",
            "owner": "01M4TRNE1GYEK4P8MXQAB3C8Q4",
            "reason": "Approved work without a specific release; revisit its age and dependencies."
          },
          {
            "key": "awaiting-decision",
            "title": "Awaiting a placement decision",
            "owner": "01M4TRNEE10EJ1WXNDK3H2FKKS",
            "reason": "A concrete operator placement or prerequisite choice is still needed."
          }
        ]
      }
    }
  ],
  "level": "warn",
  "startsAt": "<adoption UTC instant>",
  "maxExceptionDays": 30
}
```

### planned-age

```json
{
  "key": "planned-age",
  "title": "Revisit old work without a release",
  "owner": "01M4TRNE1GYEK4P8MXQAB3C8Q4",
  "reason": "Age prompts inspection without asserting that an old plan is wrong.",
  "basis": [
    {
      "decision": {
        "type": "decision",
        "id": "01M54ZFDD1MG1GS49CQA56J6QW",
        "hash": "sha256:086843a4140121cea86c686b496517c5f0058e82faf9bc7822fbefed3af0f3bc"
      }
    },
    {
      "decision": {
        "type": "decision",
        "id": "<exact adoption decision id>",
        "hash": "<exact adoption decision hash>"
      },
      "requirement": "planned-age-definition"
    }
  ],
  "state": "active",
  "appliesTo": {
    "kind": "estate"
  },
  "rules": [
    {
      "key": "planned-age",
      "kind": "vericairn.estate/staleness",
      "version": 1,
      "parameters": {
        "class": "planned-without-release",
        "maxAgeDays": 30
      }
    }
  ],
  "level": "remind",
  "startsAt": "<adoption UTC instant>",
  "maxExceptionDays": 30
}
```

### review-wait

The prepared implementation resolves results per assignment, so parallel submissions remain individually reviewable. This rule excludes every result of an accepted work order, including other assignments' results without an individual acceptance. Settled history therefore creates no review-wait debt. Unaccepted, unretired work still waits from each selected result's submission until the required exact-result review exists. This does not waive review obligations in the brief or create review evidence. Reevaluate this unchanged proposed body with that implementation before adoption.

```json
{
  "key": "review-wait",
  "title": "Review submitted results promptly",
  "owner": "01M4TRNE1GYEK4P8MXQAB3C8Q4",
  "reason": "An independent exact-result review is distinct from acceptance; operator requirements in the brief remain binding.",
  "basis": [
    {
      "decision": {
        "type": "decision",
        "id": "01M54ZFDD1MG1GS49CQA56J6QW",
        "hash": "sha256:086843a4140121cea86c686b496517c5f0058e82faf9bc7822fbefed3af0f3bc"
      }
    },
    {
      "decision": {
        "type": "decision",
        "id": "<exact adoption decision id>",
        "hash": "<exact adoption decision hash>"
      },
      "requirement": "review-wait-definition"
    }
  ],
  "state": "active",
  "appliesTo": {
    "kind": "estate"
  },
  "rules": [
    {
      "key": "review-wait",
      "kind": "vericairn.estate/staleness",
      "version": 1,
      "parameters": {
        "class": "result-awaiting-review",
        "maxAgeDays": 7,
        "reviewRequirement": "independent"
      }
    }
  ],
  "level": "warn",
  "startsAt": "<adoption UTC instant>",
  "maxExceptionDays": 30
}
```

### assignment-start

```json
{
  "key": "assignment-start",
  "title": "Inspect unstarted assignments",
  "owner": "01M4TRNE1GYEK4P8MXQAB3C8Q4",
  "reason": "A delegation that has not started needs a placement and availability check.",
  "basis": [
    {
      "decision": {
        "type": "decision",
        "id": "01M54ZFDD1MG1GS49CQA56J6QW",
        "hash": "sha256:086843a4140121cea86c686b496517c5f0058e82faf9bc7822fbefed3af0f3bc"
      }
    },
    {
      "decision": {
        "type": "decision",
        "id": "<exact adoption decision id>",
        "hash": "<exact adoption decision hash>"
      },
      "requirement": "assignment-start-definition"
    }
  ],
  "state": "active",
  "appliesTo": {
    "kind": "estate"
  },
  "rules": [
    {
      "key": "assignment-start",
      "kind": "vericairn.estate/staleness",
      "version": 1,
      "parameters": {
        "class": "assignment-without-attempt",
        "maxAgeDays": 2
      }
    }
  ],
  "level": "remind",
  "startsAt": "<adoption UTC instant>",
  "maxExceptionDays": 30
}
```

### ask-age

```json
{
  "key": "ask-age",
  "title": "Keep unanswered choices visible",
  "owner": "01M4TRNEE10EJ1WXNDK3H2FKKS",
  "reason": "A reminder neither decides a choice by timeout nor grants an effect.",
  "basis": [
    {
      "decision": {
        "type": "decision",
        "id": "01M54ZFDD1MG1GS49CQA56J6QW",
        "hash": "sha256:086843a4140121cea86c686b496517c5f0058e82faf9bc7822fbefed3af0f3bc"
      }
    },
    {
      "decision": {
        "type": "decision",
        "id": "<exact adoption decision id>",
        "hash": "<exact adoption decision hash>"
      },
      "requirement": "ask-age-definition"
    }
  ],
  "state": "active",
  "appliesTo": {
    "kind": "estate"
  },
  "rules": [
    {
      "key": "ask-age",
      "kind": "vericairn.estate/staleness",
      "version": 1,
      "parameters": {
        "class": "open-ask",
        "maxAgeDays": 7
      }
    }
  ],
  "level": "remind",
  "startsAt": "<adoption UTC instant>",
  "maxExceptionDays": 30
}
```

### obligation-gates

```json
{
  "key": "obligation-gates",
  "title": "Keep obligations visible past their gates",
  "owner": "01M4TRNEE10EJ1WXNDK3H2FKKS",
  "reason": "Obligations keep their original owners, evidence requirements and reserved actions.",
  "basis": [
    {
      "decision": {
        "type": "decision",
        "id": "01M54ZFDD1MG1GS49CQA56J6QW",
        "hash": "sha256:086843a4140121cea86c686b496517c5f0058e82faf9bc7822fbefed3af0f3bc"
      }
    },
    {
      "decision": {
        "type": "decision",
        "id": "<exact adoption decision id>",
        "hash": "<exact adoption decision hash>"
      },
      "requirement": "obligation-gates-definition"
    }
  ],
  "state": "active",
  "appliesTo": {
    "kind": "estate"
  },
  "rules": [
    {
      "key": "obligation-gates",
      "kind": "vericairn.estate/staleness",
      "version": 1,
      "parameters": {
        "class": "obligation-past-gate",
        "maxAgeDays": 0,
        "gates": [
          {
            "subject": {
              "type": "decision",
              "id": "01M51CDST3GS2CDFXD22Z3NKJP",
              "hash": "sha256:d52dca4eb1e669317a2a25ec1ade3abf95eae3d95598722d32752c73b066cd88"
            },
            "obligation": "reviewer-separation-revisit",
            "conditions": [
              {
                "kind": "work-accepted",
                "workOrder": {
                  "type": "work-order",
                  "id": "01M504XBMAJ0TFWXCQZZ8P3BE7",
                  "hash": "sha256:d08275a1d83fe9fb4d1de9eda49bba875bdb71f914641d0153f01c8648ff813d"
                }
              }
            ]
          },
          {
            "subject": {
              "type": "decision",
              "id": "01M54ZFB99VAHZ9E5HDJPEV0TF",
              "hash": "sha256:91253e2f1b1963ef1e1baafa1c9515a4db07524a14df5b1faec318706c813b3a"
            },
            "obligation": "security-disclosure",
            "conditions": [
              {
                "kind": "release-published",
                "product": "01M4TRNE1GWEAQ17M4QCDHXEZ5",
                "version": "0.7.2"
              }
            ]
          },
          {
            "subject": {
              "type": "decision",
              "id": "01M54ZFDD1MG1GS49CQA56J6QW",
              "hash": "sha256:086843a4140121cea86c686b496517c5f0058e82faf9bc7822fbefed3af0f3bc"
            },
            "obligation": "writing-grant",
            "conditions": [
              {
                "kind": "at",
                "at": "2026-10-18T16:45:40Z"
              }
            ]
          }
        ]
      }
    }
  ],
  "level": "warn",
  "startsAt": "<adoption UTC instant>",
  "maxExceptionDays": 30
}
```

### retirement-follow-through

```json
{
  "key": "retirement-follow-through",
  "title": "Retain temporary material until its real condition",
  "owner": "01M4TRNE1GYEK4P8MXQAB3C8Q4",
  "reason": "Missing condition mappings remain unavailable; age alone is not permission to remove history.",
  "basis": [
    {
      "decision": {
        "type": "decision",
        "id": "01M54ZFDD1MG1GS49CQA56J6QW",
        "hash": "sha256:086843a4140121cea86c686b496517c5f0058e82faf9bc7822fbefed3af0f3bc"
      }
    },
    {
      "decision": {
        "type": "decision",
        "id": "<exact adoption decision id>",
        "hash": "<exact adoption decision hash>"
      },
      "requirement": "retirement-follow-through-definition"
    }
  ],
  "state": "active",
  "appliesTo": {
    "kind": "estate"
  },
  "rules": [
    {
      "key": "retirement-follow-through",
      "kind": "vericairn.estate/staleness",
      "version": 1,
      "parameters": {
        "class": "retirement-past-condition",
        "maxAgeDays": 7,
        "gates": []
      }
    }
  ],
  "level": "remind",
  "startsAt": "<adoption UTC instant>",
  "maxExceptionDays": 30
}
```

### work-key-families

```json
{
  "key": "work-key-families",
  "title": "Use the recorded work key families",
  "owner": "01M4TRNE1GYEK4P8MXQAB3C8Q4",
  "reason": "New work follows the inventory while historical identifiers remain unchanged.",
  "basis": [
    {
      "decision": {
        "type": "decision",
        "id": "01M54ZFDD1MG1GS49CQA56J6QW",
        "hash": "sha256:086843a4140121cea86c686b496517c5f0058e82faf9bc7822fbefed3af0f3bc"
      }
    },
    {
      "decision": {
        "type": "decision",
        "id": "<exact adoption decision id>",
        "hash": "<exact adoption decision hash>"
      },
      "requirement": "work-key-families-definition"
    }
  ],
  "state": "active",
  "appliesTo": {
    "kind": "estate"
  },
  "rules": [
    {
      "key": "work-key-families",
      "kind": "vericairn.estate/work-key-family",
      "version": 1,
      "parameters": {
        "families": [
          {
            "prefix": "BRAND",
            "separator": "-",
            "digits": 2,
            "suffixes": [
              ""
            ]
          },
          {
            "prefix": "CODE",
            "separator": "-",
            "digits": 2,
            "suffixes": [
              ""
            ]
          },
          {
            "prefix": "CODEX",
            "separator": "-",
            "digits": 2,
            "suffixes": [
              ""
            ]
          },
          {
            "prefix": "COMMS",
            "separator": "-",
            "digits": 2,
            "suffixes": [
              ""
            ]
          },
          {
            "prefix": "CONTEXT",
            "separator": "-",
            "digits": 2,
            "suffixes": [
              ""
            ]
          },
          {
            "prefix": "CRED",
            "separator": "-",
            "digits": 2,
            "suffixes": [
              ""
            ]
          },
          {
            "prefix": "DECISIONS",
            "separator": "-",
            "digits": 2,
            "suffixes": [
              ""
            ]
          },
          {
            "prefix": "DESIGN",
            "separator": "-",
            "digits": 2,
            "suffixes": [
              ""
            ]
          },
          {
            "prefix": "DIST",
            "separator": "-",
            "digits": 2,
            "suffixes": [
              ""
            ]
          },
          {
            "prefix": "DOCS",
            "separator": "-",
            "digits": 2,
            "suffixes": [
              ""
            ]
          },
          {
            "prefix": "EFF",
            "separator": "-",
            "digits": 2,
            "suffixes": [
              ""
            ]
          },
          {
            "prefix": "ESTATE",
            "separator": "-",
            "digits": 2,
            "suffixes": [
              ""
            ]
          },
          {
            "prefix": "FIX",
            "separator": "-",
            "digits": 2,
            "suffixes": [
              ""
            ]
          },
          {
            "prefix": "GOAL",
            "separator": "-",
            "digits": 2,
            "suffixes": [
              ""
            ]
          },
          {
            "prefix": "GUIDE",
            "separator": "-",
            "digits": 2,
            "suffixes": [
              ""
            ]
          },
          {
            "prefix": "HANDOFF",
            "separator": "-",
            "digits": 2,
            "suffixes": [
              ""
            ]
          },
          {
            "prefix": "ID",
            "separator": "-",
            "digits": 2,
            "suffixes": [
              ""
            ]
          },
          {
            "prefix": "KNOW",
            "separator": "-",
            "digits": 2,
            "suffixes": [
              ""
            ]
          },
          {
            "prefix": "M",
            "separator": "",
            "digits": 1,
            "suffixes": [
              "",
              "RELEASE",
              "SIGN",
              "STORE",
              "WORK",
              "API",
              "SOFTWARE",
              "WORKBENCH"
            ]
          },
          {
            "prefix": "MCP",
            "separator": "-",
            "digits": 2,
            "suffixes": [
              ""
            ]
          },
          {
            "prefix": "MERGE",
            "separator": "-",
            "digits": 2,
            "suffixes": [
              ""
            ]
          },
          {
            "prefix": "OPS",
            "separator": "-",
            "digits": 2,
            "suffixes": [
              ""
            ]
          },
          {
            "prefix": "PLACEMENT",
            "separator": "-",
            "digits": 2,
            "suffixes": [
              ""
            ]
          },
          {
            "prefix": "PLAN",
            "separator": "-",
            "digits": 2,
            "suffixes": [
              ""
            ]
          },
          {
            "prefix": "POLICY",
            "separator": "-",
            "digits": 2,
            "suffixes": [
              ""
            ]
          },
          {
            "prefix": "PROC",
            "separator": "-",
            "digits": 2,
            "suffixes": [
              ""
            ]
          },
          {
            "prefix": "READ",
            "separator": "-",
            "digits": 2,
            "suffixes": [
              ""
            ]
          },
          {
            "prefix": "REL",
            "separator": "-",
            "digits": 3,
            "suffixes": [
              "",
              "DOCS",
              "LIVING"
            ]
          },
          {
            "prefix": "RUN",
            "separator": "-",
            "digits": 2,
            "suffixes": [
              ""
            ]
          },
          {
            "prefix": "STATUS",
            "separator": "-",
            "digits": 2,
            "suffixes": [
              ""
            ]
          },
          {
            "prefix": "TPL",
            "separator": "-",
            "digits": 2,
            "suffixes": [
              ""
            ]
          },
          {
            "prefix": "TPL-COMMS",
            "separator": "-",
            "digits": 2,
            "suffixes": [
              ""
            ]
          },
          {
            "prefix": "UPDATE",
            "separator": "-",
            "digits": 2,
            "suffixes": [
              ""
            ]
          },
          {
            "prefix": "WATCH",
            "separator": "-",
            "digits": 2,
            "suffixes": [
              ""
            ]
          },
          {
            "prefix": "WB",
            "separator": "-",
            "digits": 2,
            "suffixes": [
              ""
            ]
          },
          {
            "prefix": "WEB-HOLD",
            "separator": "-",
            "digits": 2,
            "suffixes": [
              ""
            ]
          }
        ],
        "createdFrom": "<adoption UTC instant>"
      }
    }
  ],
  "level": "warn",
  "startsAt": "<adoption UTC instant>",
  "maxExceptionDays": 30
}
```

### design-revisions

```json
{
  "key": "design-revisions",
  "title": "Identify the authored design revision",
  "owner": "01M4TRNE1GYEK4P8MXQAB3C8Q4",
  "reason": "Top-level design documents identify their revision and date before generated revision lines are adopted.",
  "basis": [
    {
      "decision": {
        "type": "decision",
        "id": "01M54ZFDD1MG1GS49CQA56J6QW",
        "hash": "sha256:086843a4140121cea86c686b496517c5f0058e82faf9bc7822fbefed3af0f3bc"
      }
    },
    {
      "decision": {
        "type": "decision",
        "id": "<exact adoption decision id>",
        "hash": "<exact adoption decision hash>"
      },
      "requirement": "design-revisions-definition"
    }
  ],
  "state": "active",
  "appliesTo": {
    "kind": "repository",
    "repository": "01M4TRNF0RZE2VEZQYP9WK3GA4",
    "selectors": [
      "design/*.md"
    ]
  },
  "rules": [
    {
      "key": "design-revisions",
      "kind": "vericairn.estate/revision-line",
      "version": 1,
      "parameters": {
        "selectors": [
          "design/*.md"
        ],
        "style": "estate-design/1"
      }
    }
  ],
  "level": "warn",
  "startsAt": "<adoption UTC instant>",
  "maxExceptionDays": 30
}
```

### work-state-consistency

Work-state consistency warns prospectively from this policy identity's first declaration, not from the beginning of estate history. A contradiction is in scope when the later of its explicit open status and exact acceptance or publication is recorded at or after that boundary. Earlier contradictions raise no finding; policy revisions and a changed activation time preserve the original boundary. The proposed version-1 body below needs no added parameter, historical status rewrite or new done status. Acceptance remains a separate derived fact. Reevaluate with the prepared implementation before obtaining exact adoption approval.

```json
{
  "key": "work-state-consistency",
  "title": "Keep explicit work states consistent",
  "owner": "01M4TRNE1GYEK4P8MXQAB3C8Q4",
  "reason": "Recorded acceptance and publication must not be concealed by stale open states.",
  "basis": [
    {
      "decision": {
        "type": "decision",
        "id": "01M54ZFDD1MG1GS49CQA56J6QW",
        "hash": "sha256:086843a4140121cea86c686b496517c5f0058e82faf9bc7822fbefed3af0f3bc"
      }
    },
    {
      "decision": {
        "type": "decision",
        "id": "<exact adoption decision id>",
        "hash": "<exact adoption decision hash>"
      },
      "requirement": "work-state-consistency-definition"
    }
  ],
  "state": "active",
  "appliesTo": {
    "kind": "estate"
  },
  "rules": [
    {
      "key": "work-state-consistency",
      "kind": "vericairn.estate/work-state-consistency",
      "version": 1,
      "parameters": {
        "cases": [
          "accepted-still-open",
          "published-plan-still-open"
        ]
      }
    }
  ],
  "level": "warn",
  "startsAt": "<adoption UTC instant>",
  "maxExceptionDays": 30
}
```

## Initial duty bodies

These seven complete bodies preserve the decided monthly stage and per-publication occurrence. The six monthly duties are due at activation, with their stated grace; completion never moves the anchor. No historical report prepays an occurrence. The release duty selects observed publications after activation and does not backfill earlier publications. Exact inventory preparation and review routing follow the bodies.

### dependency-review

```json
{
  "key": "dependency-review",
  "title": "Review dependency versions",
  "owner": "01M4TRNE1GYEK4P8MXQAB3C8Q4",
  "reason": "Retain the exact dependency inventory, source dates, version decisions and resulting work.",
  "basis": [
    {
      "decision": {
        "type": "decision",
        "id": "01M54ZFDD1MG1GS49CQA56J6QW",
        "hash": "sha256:086843a4140121cea86c686b496517c5f0058e82faf9bc7822fbefed3af0f3bc"
      }
    },
    {
      "decision": {
        "type": "decision",
        "id": "01M55EJ2HRGHQ30G0A34FDCY4E",
        "hash": "sha256:7e6cf877e17c38af39f1bd0ba40e76bb1d6c557d738c20207c398861d326760e"
      }
    },
    {
      "decision": {
        "type": "decision",
        "id": "<exact adoption decision id>",
        "hash": "<exact adoption decision hash>"
      },
      "requirement": "dependency-review-definition"
    }
  ],
  "state": "active",
  "appliesTo": {
    "kind": "product",
    "product": "01M4TRNE1GWEAQ17M4QCDHXEZ5"
  },
  "rules": [
    {
      "key": "occurrence",
      "kind": "vericairn.estate/recurring-duty",
      "version": 1,
      "parameters": {
        "subject": {
          "type": "product",
          "id": "01M4TRNE1GWEAQ17M4QCDHXEZ5"
        },
        "schedule": {
          "kind": "interval",
          "unit": "months",
          "every": 1,
          "firstDueAt": "<adoption UTC instant>"
        },
        "graceDays": 7,
        "renewal": "none",
        "discharge": {
          "checks": [
            "inventory-complete",
            "versions-reviewed",
            "findings-recorded"
          ],
          "review": "independent",
          "records": []
        }
      }
    }
  ],
  "level": "warn",
  "startsAt": "<adoption UTC instant>",
  "maxExceptionDays": 30
}
```

### advisory-review

```json
{
  "key": "advisory-review",
  "title": "Review dependency advisories",
  "owner": "01M4TRNE1GYEK4P8MXQAB3C8Q4",
  "reason": "Retain advisory observations matched to direct and transitive dependencies; unavailable sources remain incomplete.",
  "basis": [
    {
      "decision": {
        "type": "decision",
        "id": "01M54ZFDD1MG1GS49CQA56J6QW",
        "hash": "sha256:086843a4140121cea86c686b496517c5f0058e82faf9bc7822fbefed3af0f3bc"
      }
    },
    {
      "decision": {
        "type": "decision",
        "id": "01M55EJ2HRGHQ30G0A34FDCY4E",
        "hash": "sha256:7e6cf877e17c38af39f1bd0ba40e76bb1d6c557d738c20207c398861d326760e"
      }
    },
    {
      "decision": {
        "type": "decision",
        "id": "<exact adoption decision id>",
        "hash": "<exact adoption decision hash>"
      },
      "requirement": "advisory-review-definition"
    }
  ],
  "state": "active",
  "appliesTo": {
    "kind": "product",
    "product": "01M4TRNE1GWEAQ17M4QCDHXEZ5"
  },
  "rules": [
    {
      "key": "occurrence",
      "kind": "vericairn.estate/recurring-duty",
      "version": 1,
      "parameters": {
        "subject": {
          "type": "product",
          "id": "01M4TRNE1GWEAQ17M4QCDHXEZ5"
        },
        "schedule": {
          "kind": "interval",
          "unit": "months",
          "every": 1,
          "firstDueAt": "<adoption UTC instant>"
        },
        "graceDays": 2,
        "renewal": "none",
        "discharge": {
          "checks": [
            "sources-consulted",
            "inventory-matched",
            "findings-recorded"
          ],
          "review": "independent",
          "records": []
        }
      }
    }
  ],
  "level": "warn",
  "startsAt": "<adoption UTC instant>",
  "maxExceptionDays": 30
}
```

### workflow-pin-review

```json
{
  "key": "workflow-pin-review",
  "title": "Review workflow action pins",
  "owner": "01M4TRNE1GYEK4P8MXQAB3C8Q4",
  "reason": "Review action provenance across the product, estate and organization workflow inventories.",
  "basis": [
    {
      "decision": {
        "type": "decision",
        "id": "01M54ZFDD1MG1GS49CQA56J6QW",
        "hash": "sha256:086843a4140121cea86c686b496517c5f0058e82faf9bc7822fbefed3af0f3bc"
      }
    },
    {
      "decision": {
        "type": "decision",
        "id": "01M55EJ2HRGHQ30G0A34FDCY4E",
        "hash": "sha256:7e6cf877e17c38af39f1bd0ba40e76bb1d6c557d738c20207c398861d326760e"
      }
    },
    {
      "decision": {
        "type": "decision",
        "id": "<exact adoption decision id>",
        "hash": "<exact adoption decision hash>"
      },
      "requirement": "workflow-pin-review-definition"
    }
  ],
  "state": "active",
  "appliesTo": {
    "kind": "estate"
  },
  "rules": [
    {
      "key": "occurrence",
      "kind": "vericairn.estate/recurring-duty",
      "version": 1,
      "parameters": {
        "subject": {
          "type": "estate",
          "id": "01M4TRNF0RZE2VEZQYP9WK3GA4"
        },
        "schedule": {
          "kind": "interval",
          "unit": "months",
          "every": 1,
          "firstDueAt": "<adoption UTC instant>"
        },
        "graceDays": 7,
        "renewal": "none",
        "discharge": {
          "checks": [
            "workflows-covered",
            "pins-reviewed",
            "findings-recorded"
          ],
          "review": "independent",
          "records": []
        }
      }
    }
  ],
  "level": "warn",
  "startsAt": "<adoption UTC instant>",
  "maxExceptionDays": 30
}
```

### export-restore-verification

```json
{
  "key": "export-restore-verification",
  "title": "Verify an isolated estate export and restore",
  "owner": "01M4TRNE1GYEK4P8MXQAB3C8Q4",
  "reason": "Prove export integrity and isolated restore without restoring signing authority or private material.",
  "basis": [
    {
      "decision": {
        "type": "decision",
        "id": "01M54ZFDD1MG1GS49CQA56J6QW",
        "hash": "sha256:086843a4140121cea86c686b496517c5f0058e82faf9bc7822fbefed3af0f3bc"
      }
    },
    {
      "decision": {
        "type": "decision",
        "id": "01M55EJ2HRGHQ30G0A34FDCY4E",
        "hash": "sha256:7e6cf877e17c38af39f1bd0ba40e76bb1d6c557d738c20207c398861d326760e"
      }
    },
    {
      "decision": {
        "type": "decision",
        "id": "<exact adoption decision id>",
        "hash": "<exact adoption decision hash>"
      },
      "requirement": "export-restore-verification-definition"
    }
  ],
  "state": "active",
  "appliesTo": {
    "kind": "estate"
  },
  "rules": [
    {
      "key": "occurrence",
      "kind": "vericairn.estate/recurring-duty",
      "version": 1,
      "parameters": {
        "subject": {
          "type": "estate",
          "id": "01M4TRNF0RZE2VEZQYP9WK3GA4"
        },
        "schedule": {
          "kind": "interval",
          "unit": "months",
          "every": 1,
          "firstDueAt": "<adoption UTC instant>"
        },
        "graceDays": 7,
        "renewal": "none",
        "discharge": {
          "checks": [
            "export-verified",
            "restore-verified",
            "authority-not-restored"
          ],
          "review": "independent",
          "records": [
            {
              "key": "restore-proof",
              "kind": "verification",
              "method": "estate-export-restore",
              "subject": "result"
            }
          ]
        }
      }
    }
  ],
  "level": "warn",
  "startsAt": "<adoption UTC instant>",
  "maxExceptionDays": 30
}
```

### release-export-restore

```json
{
  "key": "release-export-restore",
  "title": "Verify export and restore with each published build",
  "owner": "01M4TRNE1GYEK4P8MXQAB3C8Q4",
  "reason": "Exercise each observed published product build in addition to monthly continuity verification.",
  "basis": [
    {
      "decision": {
        "type": "decision",
        "id": "01M54ZFDD1MG1GS49CQA56J6QW",
        "hash": "sha256:086843a4140121cea86c686b496517c5f0058e82faf9bc7822fbefed3af0f3bc"
      }
    },
    {
      "decision": {
        "type": "decision",
        "id": "01M55EJ2HRGHQ30G0A34FDCY4E",
        "hash": "sha256:7e6cf877e17c38af39f1bd0ba40e76bb1d6c557d738c20207c398861d326760e"
      }
    },
    {
      "decision": {
        "type": "decision",
        "id": "<exact adoption decision id>",
        "hash": "<exact adoption decision hash>"
      },
      "requirement": "release-export-restore-definition"
    }
  ],
  "state": "active",
  "appliesTo": {
    "kind": "product",
    "product": "01M4TRNE1GWEAQ17M4QCDHXEZ5"
  },
  "rules": [
    {
      "key": "occurrence",
      "kind": "vericairn.estate/recurring-duty",
      "version": 1,
      "parameters": {
        "subject": {
          "type": "product",
          "id": "01M4TRNE1GWEAQ17M4QCDHXEZ5"
        },
        "schedule": {
          "kind": "release",
          "product": "01M4TRNE1GWEAQ17M4QCDHXEZ5"
        },
        "graceDays": 7,
        "renewal": "none",
        "discharge": {
          "checks": [
            "published-build-used",
            "export-verified",
            "restore-verified"
          ],
          "review": "independent",
          "records": [
            {
              "key": "publication-proof",
              "kind": "verification",
              "method": "estate-export-restore",
              "subject": "duty-subject",
              "commitSource": "publication"
            }
          ]
        }
      }
    }
  ],
  "level": "warn",
  "startsAt": "<adoption UTC instant>",
  "maxExceptionDays": 30
}
```

### scratch-cleanup

```json
{
  "key": "scratch-cleanup",
  "title": "Account for the builder's disposable scratch",
  "owner": "01M4TRNE1GYEK4P8MXQAB3C8Q4",
  "reason": "Remove only the builder's own disposable scratch and retain a disposition for each selected item.",
  "basis": [
    {
      "decision": {
        "type": "decision",
        "id": "01M54ZFDD1MG1GS49CQA56J6QW",
        "hash": "sha256:086843a4140121cea86c686b496517c5f0058e82faf9bc7822fbefed3af0f3bc"
      }
    },
    {
      "decision": {
        "type": "decision",
        "id": "01M55EJ2HRGHQ30G0A34FDCY4E",
        "hash": "sha256:7e6cf877e17c38af39f1bd0ba40e76bb1d6c557d738c20207c398861d326760e"
      }
    },
    {
      "decision": {
        "type": "decision",
        "id": "<exact adoption decision id>",
        "hash": "<exact adoption decision hash>"
      },
      "requirement": "scratch-cleanup-definition"
    }
  ],
  "state": "active",
  "appliesTo": {
    "kind": "estate"
  },
  "rules": [
    {
      "key": "occurrence",
      "kind": "vericairn.estate/recurring-duty",
      "version": 1,
      "parameters": {
        "subject": {
          "type": "estate",
          "id": "01M4TRNF0RZE2VEZQYP9WK3GA4"
        },
        "schedule": {
          "kind": "interval",
          "unit": "months",
          "every": 1,
          "firstDueAt": "<adoption UTC instant>"
        },
        "graceDays": 2,
        "renewal": "none",
        "discharge": {
          "checks": [
            "inventory-reviewed",
            "removals-confirmed"
          ],
          "review": "independent",
          "records": []
        }
      }
    }
  ],
  "level": "remind",
  "startsAt": "<adoption UTC instant>",
  "maxExceptionDays": 30
}
```

### withdrawn-artifact-cleanup

```json
{
  "key": "withdrawn-artifact-cleanup",
  "title": "Account for withdrawn artifacts",
  "owner": "01M4TRNE1GYEK4P8MXQAB3C8Q4",
  "reason": "Retain a complete withdrawal inventory and exact removal or authorized retention evidence; reserved removals still require the operator.",
  "basis": [
    {
      "decision": {
        "type": "decision",
        "id": "01M54ZFDD1MG1GS49CQA56J6QW",
        "hash": "sha256:086843a4140121cea86c686b496517c5f0058e82faf9bc7822fbefed3af0f3bc"
      }
    },
    {
      "decision": {
        "type": "decision",
        "id": "01M55EJ2HRGHQ30G0A34FDCY4E",
        "hash": "sha256:7e6cf877e17c38af39f1bd0ba40e76bb1d6c557d738c20207c398861d326760e"
      }
    },
    {
      "decision": {
        "type": "decision",
        "id": "<exact adoption decision id>",
        "hash": "<exact adoption decision hash>"
      },
      "requirement": "withdrawn-artifact-cleanup-definition"
    }
  ],
  "state": "active",
  "appliesTo": {
    "kind": "estate"
  },
  "rules": [
    {
      "key": "occurrence",
      "kind": "vericairn.estate/recurring-duty",
      "version": 1,
      "parameters": {
        "subject": {
          "type": "estate",
          "id": "01M4TRNF0RZE2VEZQYP9WK3GA4"
        },
        "schedule": {
          "kind": "interval",
          "unit": "months",
          "every": 1,
          "firstDueAt": "<adoption UTC instant>"
        },
        "graceDays": 2,
        "renewal": "none",
        "discharge": {
          "checks": [
            "withdrawals-covered",
            "removals-confirmed"
          ],
          "review": "independent",
          "records": []
        }
      }
    }
  ],
  "level": "warn",
  "startsAt": "<adoption UTC instant>",
  "maxExceptionDays": 30
}
```

## Preparation, inventories and review capacity

All seven initial duties are owned by builder principal `01M4TRNE1GYEK4P8MXQAB3C8Q4`. Reviewer is `01M4W22P80M1DKEE5N1KHD01ZQ`; Codex is `01M50H5K1E5D0QK46952DS2NAK`; the operator identity is `01M4TRNEE10EJ1WXNDK3H2FKKS`. The policy owner routes work and grants no authority. The approved security route comes from decision `01M510RJ60YBNK4AY1XP323NP9`; the two additional monthly sessions come from D20-2, decision `01M55EJ2HRGHQ30G0A34FDCY4E` at `sha256:7e6cf877e17c38af39f1bd0ba40e76bb1d6c557d738c20207c398861d326760e`.

The continuing subjects in the bodies deliberately omit evidence hashes. Each occurrence's result instead binds the exact policy and subject revisions, every repository commit, retained outputs and proofs in `result.duties`. An inventory below defines preparation scope; it is not an extra rule parameter or a new automatically checked field. The preparer must refresh the selected records and obtain all necessary repository/source access for each occurrence. This preparation makes no live provider observation and qualifies no provider account.

| Initial duty | Exact inventory and preparation | Exact-result review route |
|---|---|---|
| dependency-review | Product `01M4TRNE1GWEAQ17M4QCDHXEZ5`; product repository `01M4TRNE1GB7CYB7DND8GBTTM1`. Retain its dependency manifests and lock files at the selected commit, direct and transitive packages, observed source/version dates, updates or reasons to retain, and resulting work. | Codex when the builder authors; builder when Codex authors. Vulnerability assessment belongs to advisory-review. |
| advisory-review | Same product and exact dependency inventory, matched to dated retained advisory observations. The preparer records every required source and unavailable source; inaccessible feeds leave `sources-consulted` incomplete. A monthly cadence does not postpone known urgent issues. | Reviewer; security assessment, with the entire exact result and its inventory available. |
| workflow-pin-review | Product repository `01M4TRNE1GB7CYB7DND8GBTTM1`, estate repository resource `01M4TRNE1G77059RFJCF2JEEDR`, and organization workflow repository `01M502AV38MKHGCQF00VC8VJTR`. Retain each selected commit, complete workflow inventory, action commit pins and dated upstream observations. An empty repository inventory requires evidence. FIX-13's build-time allow-list remains separate. | Reviewer; security assessment of action provenance and supply-chain exposure. |
| export-restore-verification | Estate identity `01M4TRNF0RZE2VEZQYP9WK3GA4`. Retain export manifest/hash, exact build commit used, isolated restore comparison, negative integrity checks and proof that private material and authority were not restored. The verification subject is the result; method `estate-export-restore`; no commit-source parameter. The verification still records the real build commit and its role in evidence. | Codex or builder other than the author, after the result-subject verification, with that exact verification in `review.read`. |
| release-export-restore | Product identity `01M4TRNE1GWEAQ17M4QCDHXEZ5`; each first observed publication after activation. Retain the published artifact/build identity and isolated export/restore evidence. The verification names the occurrence's exact product revision and the publication commit. A source-tree fixture does not qualify a published build. | Codex or builder other than the author. A candidate owes no occurrence for its own future publication. |
| scratch-cleanup | Estate subject; only the builder's own disposable local workspaces. Inventory portable workspace identities and retained/disposable classifications, with before/after hashes/counts and a removal or retention disposition for every selected item. Retained work and another principal's workspace are excluded. Machine paths stay local. | Codex or builder other than the author; no operator act for the scoped housekeeping. |
| withdrawn-artifact-cleanup | Estate subject; all current and historical release withdrawals and relevant resources, including product repository `01M4TRNE1GB7CYB7DND8GBTTM1`, NuGet organization `01M527CNDKEAJD7ADQSY1ASN0W`, and account identity `01M4VSZ1N0T22XTA42ZF681KCE` (current revision `01M550R1SFXQSASXTR1QB9CTCC`). Account for every selected artifact with confirmed removal or authorized retention. Preserve frozen documentation, records and audit history. The settled legacy versions and withdrawn drafts remain historical evidence. | Codex or builder other than the author. An empty inventory needs no operator review. Actual external removals need separately authorized asks/decisions, successful operator operation records and any required retirement proofs in the retained evidence; an ask or uncertain delete cannot pass `removals-confirmed`. |

The withdrawal body has no unconditional `observed-operation` clause, because a complete empty inventory can discharge it. Its declared checks and independent review must verify conditional native proofs when removals exist. The evaluator does not interpret the report's prose to discover a provider act. Prepare explicit proof clauses in a separately approved revision if a fixed scope later needs mechanical operation matching; do not silently add an unsupported conditional clause or treat the empty-inventory route as permission to omit actual receipts.

The six monthly occurrences open at the adoption instant. Reserve the first monthly Reviewer session within the advisory duty's two-day grace and the second within the workflow duty's seven-day grace, repeating from the original monthly anchor. Prepare both inventories before their sessions. These are approved demands, not evidence that appointments or available hours have already been confirmed. The builder confirms availability and prepares exact assignments before activation. If either session cannot be provided, put a concrete changed activation/cadence proposal to the operator; no substitute security reviewer or omitted review is assumed.

| Scenario / addition | Builder prepared results | Reviewer security reviews | Codex non-security reviews | Operator reviews and acts |
|---|---:|---:|---:|---|
| Initial six monthly duties, no publication | 6 | 2 | 4 | 0 routine |
| Initial stage, one publication | 7 | 2 | 5 | 0 routine |
| Each additional publication | +1 | +0 | +1 | No additional maintenance review |
| Each certificate observation once adopted | +1 | +0 | +1 | Exact successor-date body approval |
| Each adopted device/credential/enrollment act | +1 prepared report | +0 for action confirmation | +0 | +1 exact-result review, the reserved act, and successor-date approval for a continuing renewal |
| Cleanup with external removals | Already counted | Already counted | Already counted | Actual operations and linked asks/decisions for every target |
| Estate hygiene audit once adopted | +1 per month | +0 | +1 | Findings may separately require an operator choice |
| Code audit once adopted | +1 in its three-month cycle | +1 | +0 | Findings may separately require an operator choice |

The one-publication baseline assigns the builder seven results and no reviews, Reviewer two reviews and no results, Codex five reviews and no results, and the operator no routine result or review. When Codex prepares one of the five non-security results, move its preparation from the builder to Codex and its review from Codex to the builder. Totals stay seven results and seven reviews; no self-review is eligible. Extra security scope adds Reviewer demand. These counts exclude initial exact policy approvals, the writing grant, attempts, supporting verifications, remediation, debt and rework. They are not a claim that the whole estate fits in seven tasks.

The builder owns POLICY-04's capacity follow-up after three **completed** monthly cycles, and the operator approves any expansion. Retain preparation time, review time, missed and carried occurrences, unavailable inputs, operator interventions and successor-date approvals. Compare measured capacity with the two reserved Reviewer sessions before proposing audits or rotation. Later authorized adoption work cites POLICY-04's result and those measured cycles even if its product implementation has closed. Weekly advisory/scratch/withdrawal cadences require qualified standing agents and review routes plus exact operator approval; they never switch automatically at a version or installation. Community keeps definition, evidence, checks and on-demand work; Pro's scheduled coordination belongs to GOAL-05 after Remote. Integrity, export, recovery and existing certificate renewal gain no new edition restriction.

## Deferred duties and date readiness

These seven menu rows have no declaration and therefore no occurrence in the initial stage. Each row retains its decided owner, level, preparation and review route. Missing dates or subjects are readiness gaps, not a safety claim or an assumed anniversary. Existing expiry, compromise, recovery and renewal procedures continue; a known deadline is handled immediately through authorized work.

| Deferred duty | Subject/date readiness and proposed timing | Preparation, proofs and review | Next adoption review |
|---|---|---|---|
| device-key-rotation | Operator; warn. Four admitted signing-device identities are listed below. Every 12 months from an individually approved first rotation date; 30-day grace. No first date is approved here. The recovery device is separate. | Builder prepares public issue/rotation evidence and continuity report; operator performs the key change and reviews the exact result with `replacement-usable`, `continuity-checked` and a `device-rotation` proof clause. Security assessment, if separately commissioned, goes to Reviewer. | After three completed monthly cycles, with operator act and review capacity confirmed; urgent recovery does not wait. |
| credential-rotation | Operator; warn. Active credential-reference `01M505S19NSY5FSFY40ET0GXJQ` has no `rotateBy`. No date is invented and no credential store is accessed. Use the actual approved `rotateBy`, lead 14 days, grace 7 days, renewal `next-date-required`. | Builder prepares the public metadata/report; operator chooses the date, performs replacement/revocation and reviews `replacement-usable`, `old-access-ended`, `next-date-observed`, with exact `credential-rotation` provider/action proofs. Never retain a secret value or secret hash. | Operator date selection and exact approval before any required rotation; no dependency on standing agents. |
| runtime-certificate-renewal | Builder; warn. No resource identifying each in-use runtime and no reviewed public expiry report are in the selected inventory. Actual observed expiry, lead 30 days, grace 1 day, renewal `next-date-required`. | Retain public old/new fingerprints, validity dates and observation of the certificate actually presented; `replacement-observed`, `connectivity-checked`, `next-date-observed`. Date source is a signed retained result and exact independent passing review. Codex or builder other than the author reviews. The daemon performs its existing automatic renewal. | At the first actual identified transport/expiry inventory; approve the successor-date body after renewal. An automatically renewed certificate without its report leaves evidence incomplete, not proof of expired transport. |
| apple-enrollment-renewal | Operator; warn. No selected Apple enrollment resource or observed provider renewal date. Contingent on Remote's need. Actual date, lead 30 days, grace 23 days, next date required. | Builder prepares provider terms, receipt/status and account/access evidence; operator performs the authorized account/spending act and reviews `renewal-confirmed`, `access-confirmed`, `next-date-observed`, with an operator-authored `observed-operation` for the actual route/action. | Before Remote's real-device qualification requires enrollment; exact approval of subject, actual terms/date and capacity. |
| google-enrollment-renewal | Operator; warn only if an actual required provider act exists. No selected enrollment resource or observed requirement/date. Actual required renewal/verification/expiry date, lead 30 days, grace 23 days; next date only for a continuing term. | Builder prepares the source finding; operator performs and reviews the real act with `requirement-satisfied`, `access-confirmed` and exact observed-operation evidence. If no continuing renewal or operator act is required, declare no operator renewal duty. | Before Remote requires it, against actual provider terms. No assumed annual fee or deadline. An observation-only duty would need its own exact approval and independent review. |
| estate-hygiene-audit | Builder; warn. Estate identity `01M4TRNF0RZE2VEZQYP9WK3GA4`; proposed every month, first due at its later activation, grace 7 days. | `estate-check-read`, `work-and-asks-covered`, `obligations-and-retirements-covered`, `dispositions-recorded`; a retained accounting of every remaining finding and owner. Codex or builder other than the author reviews. An audit can complete while explicitly retained warnings remain. | After three completed monthly cycles and measured preparation/review capacity. ESTATE-01 remains the historical audit, not a reused recurring work identity. |
| code-audit | Builder; remind. Explicit code-repository inventory: product `01M4TRNE1GB7CYB7DND8GBTTM1`, website `01M4TVNF982KRKM5K17PVZGGXY`, and Pro `01M501XCJHH192N3Z1YERCEZZ9`; confirm access and selected commits before adoption. Every 3 months, first due at later activation, grace 14 days. | `repositories-covered`, `architecture-and-tests-reviewed`, `build-and-security-reviewed`, `findings-placed`; retained reports and limitations. Reviewer reviews the whole exact result because its scope includes security. Inaccessible repositories prevent complete coverage. | After three completed monthly cycles; plan the additional security session in each due month. CODE-01 stays historical. |

| Device identity | Current selected revision | Preparation disposition |
|---|---|---|
| `01M4V4AB0CHYZ1230HF1STQ5H1` | `01M4ZX3W6114BQKQDT1BVZKX5G` | Builder signing device; operator selects an actual first rotation date after inspecting public history. |
| `01M4W22PWEBXCR692HVKHFPJW6` | `01M4ZXBCFX7KEBS704TWCB45RQ` | Reviewer signing device; custody separation remains an explicit CRED-01/ID-03 decision obligation. |
| `01M4VGDX6WNAYA1XM7JAYKHMA9` | `01M4ZYGC5G8YSR109W7VMBYVRQ` | Operator signing device; required presence and the native continuity contract remain. |
| `01M50H5W0X6VQH8G3EJ2C2C52H` | `01M50H5W0X6VQH8G3EJ2C2C52H` | Codex signing device; no assumed rotation anniversary. |
| `01M4ZY92H35QJEDMSRCZ0R2X7J` | `01M4ZY92H35QJEDMSRCZ0R2X7J` | Operator recovery device; separate operator-selected recovery procedure/date, never an automatic online rotation. |

A date with less than the proposed lead available needs an explicitly approved shorter lead/grace that preserves its actual deadline. Every continuing date duty requires approved successor-date evidence as well as completed work. No date, device, enrollment or additional recurring workload is activated by this table.

## Four writing-entry contents and the unresolved authoring basis

D18-1 and the writing decision `01M54WE9CNZ30D3FTJNHP9BR9Q` require the builder to author these entries **after** the operator records the bounded `manage-writing-exceptions` grant. Neither current authority grants that scope. A writing entry's required `basis` accepts only operator authority or an exact authority-grant reference; an adoption decision reference is not a substitute. Consequently these four JSON objects are **content preparations, not complete record bodies**: their required `basis` is intentionally absent. This is an explicit outstanding part of the exact-body deliverable. No invented grant id/hash or additional substitution field conceals it, and operator authorship is not silently substituted for the decided builder route.

Prepare the grant for the operator on the upgraded estate, limited to the four case-sensitive tokens and the union of the exact scopes below, with explicit add/remove actions and an operator-selected expiry. After the grant is signed, add its actual `{kind: authority, grant: {type, id, hash}}` basis to each object and review all four complete bodies. The grant needs its own preparation and approval; policy ownership or the ordinary engineering grant does not cover it. No grant or entry is recorded by this proposal.

### Rapid7

```json
{
  "token": "Rapid7",
  "code": "writing.spacing",
  "reason": "Intentional spelling of the named security vendor; adding a space would alter the proper name.",
  "scope": {
    "paths": [
      {
        "repository": "01M4TRNF0RZE2VEZQYP9WK3GA4",
        "selectors": [
          "design/catalog.md",
          "design/catalog/information-technology-services-and-operations.md"
        ]
      }
    ]
  },
  "state": "active"
}
```

### Open311

```json
{
  "token": "Open311",
  "code": "writing.spacing",
  "reason": "Intentional name of the civic service-request interface; the digits belong to its name.",
  "scope": {
    "paths": [
      {
        "repository": "01M4TRNF0RZE2VEZQYP9WK3GA4",
        "selectors": [
          "design/catalog/public-sector.md"
        ]
      }
    ]
  },
  "state": "active"
}
```

### project44

```json
{
  "token": "project44",
  "code": "writing.spacing",
  "reason": "Intentional company and product spelling in the transportation catalog; preserve its lowercase initial and digits.",
  "scope": {
    "paths": [
      {
        "repository": "01M4TRNF0RZE2VEZQYP9WK3GA4",
        "selectors": [
          "design/catalog/transportation-and-logistics.md"
        ]
      }
    ]
  },
  "state": "active"
}
```

### efea482

```json
{
  "token": "efea482",
  "code": "writing.spacing",
  "reason": "A literal commit abbreviation in an immutable historical review; preserve the original record and explain this spelling only in its summary.",
  "scope": {
    "records": [
      {
        "record": "01M539T84D1MY51VP5JJPJC2JM",
        "field": "summary"
      }
    ]
  },
  "state": "active"
}
```


These scopes account for the eight original matches: Rapid7 two, Open311 four, project44 one, and efea482 one. Exact case and whole-token matching remain; another location or spelling is outside scope. There is no expiry, level or scheduled review on a writing entry. A removed spelling is handled by terminal signed removal when the unused-entry warning is supported by a complete scan. Adoption never rewrites the historical review or catalog text.

## Existing work: exact proposed placement patches

The October 19 preparation inventory has 34 unaccepted, unretired work orders. The table pins each selected revision and preserves its recorded timing and dependencies; concurrent work can change those revisions before adoption. Accepted work, the retired CODEX-16 identity, historical release carriers and their records remain untouched.

For every row the exact proposed `placement` field is `{"kind":"backlog","key":"future-work"}` or `{"kind":"backlog","key":"awaiting-decision"}`, as named in the third column. Preserve all other body fields and the continuing identity, and supersede only the pinned current revision after refreshing it. The placement policy must first be declared; `work place` also expects its current hash. These are reviewable field patches, not new work-order identities or decisions.

The selected estate has **no current future release carrier**. For rows with an already decided release, `awaiting-decision` is a proposed **interim carrier-preparation disposition**, requiring adoption approval; it does not cancel or change the release commitment in column four. The final release-placement bodies remain blocked until actual carrier identities are recorded. The builder prepares each approved carrier with `releasePlan` naming product `01M4TRNE1GWEAQ17M4QCDHXEZ5` and its exact version, then prepares every affected placement with `kind: release`, that real carrier identity, the same product and version. A completed REL-073 or historical carrier cannot carry future work. Do not silently turn interim backlog patches into a claim that the exact release placements are complete.

| Work key | Current revision / expected hash | Proposed interim backlog key | Timing and remaining preparation |
|---|---|---|---|
| `CRED-01` | `01M504XBMAJ0TFWXCQZZ8P3BE7` / `sha256:d08275a1d83fe9fb4d1de9eda49bba875bdb71f914641d0153f01c8648ff813d` | `awaiting-decision` | Paused until a provider needs a vault; do not reactivate it by placement. |
| `DESIGN-06` | `01M4ZZAKBNBGGP2KXT0FFTFPNC` / `sha256:2bb11ffbcf206475a5565b6a22b3ae32fd2ed09b80f36ad3a3bbf5d4e687167d` | `future-work` | Design during the 0.9.0 cycle; place its build only after design acceptance. |
| `DESIGN-07` | `01M54ZGBMG0GCBJMR3FD01H6AZ` / `sha256:c6673ae43dd702f5b373d412073435a3eaa1456aa8a56736e176f06ab032e53f` | `future-work` | Design during the 0.9.0 cycle; later standing-agent implementation needs its own placement. |
| `DIST-02` | `01M552Z1JP3WTR0FHARS2767JT` / `sha256:38aab4ded8706b54d84d782e5429272b7f10252623356266ef9e7829570d68c5` | `awaiting-decision` | Decided release 0.8.1 after CODE-02; exact release carrier is not recorded. |
| `DOCS-04` | `01M4YSWK7RK26P04ARZ2NJJBFM` / `sha256:2b1d74c2b80792600aa980eb3bbe8bdc1f77ddc24674f4aac0a7963c2d4e7fa3` | `future-work` | Retained living-document follow-up; no exact release commitment is recorded here. |
| `DOCS-07` | `01M552YZK8GEVC9N1QBBX24P0J` / `sha256:3ef0fcc2015c6625df169e48c919a2299253aeace4e24e2a511c8a826089d287` | `awaiting-decision` | Decided release 0.8.1 after CODE-02; exact release carrier is not recorded. |
| `EFF-01` | `01M5879DYZY8VJ92HYCY0WQ789` / `sha256:b04306a942045ef6ebcfaf3d5b25eb9594b5b70f03bc9dc482dfd472a5dd6b05` | `awaiting-decision` | Moved to 0.8.1 by decision 01M58781AVD035Z2F1ZVZT23MD; exact carrier is not recorded. |
| `GOAL-05` | `01M54ZG9GJRSQMNW3QBW2QP631` / `sha256:572f4468e656a5357f4109dfbc9b6a47af054bd416c19f3389f73422dd1ca42c` | `future-work` | After Remote 0.9.0 in the existing Pro repository; no invented version. |
| `GOAL-06` | `01M4ZSFA0G08JB9XB3QQAZVAP9` / `sha256:95f704f8998c9eedb79f6befb59f87f9ffd4201961b8cf516e5eb770b62a479a` | `future-work` | Retain the estate-release journey and its distinct exit; inspect current evidence before acceptance. |
| `GUIDE-01` | `01M537JPSHAWQGN8E0Z40F2XPG` / `sha256:c06cd22127a222661128e68e3453890b49a93d60cf3cbd7ae83b2f2861c2c9ce` | `awaiting-decision` | Its 0.8.0-or-later placement is a proposal; operator selects the release before M4. |
| `GUIDE-02` | `01M537JRHE31SS0Y0SZ211ZMCE` / `sha256:3035c00bc9583baed83e25ffa38460087448064742dfde4e77bad44635775f61` | `future-work` | After GUIDE-01 and before M4; repository creation and publication remain separate operator choices. |
| `GUIDE-03` | `01M537JT98K7GW65XEVVVZTRCJ` / `sha256:e59985721e39c09538e867c1a2a80da9a10f50909885da260d331d3baca84468` | `future-work` | M4 package and catalog lifecycle; no exact release version approved. |
| `GUIDE-04` | `01M537JW12BNAD23GJSM5WFX8A` / `sha256:9b0c3c3f16c1861945f9f6015b674893f563cb49cebd5316b6b134a4fcd0d286` | `future-work` | M4 after GUIDE-03; preserve the separate Foundry qualification. |
| `GUIDE-05` | `01M537JXTH3Z4XJ5DWRTRQEA3B` / `sha256:92efcfef5d0885130253353c6a2cf7ff63484270e4a50cfc0f70863178127801` | `future-work` | M4 with each site's own activation and first-sign-in decisions. |
| `GUIDE-06` | `01M537JZJM29JEDG4JQ4R4ESV2` / `sha256:e1f854068279bc8618c16c252b01abfb7c2afea424b5acfc8bf484f2b0949f69` | `future-work` | Before the first release using record-backed guide retirement; exact carrier awaits that plan. |
| `ID-03` | `01M4YSWK7RXAVX1HKB14JX7QX1` / `sha256:596c5f08aa42ddb2d2ad25f3dc8035c7a1f762f9a9408de4abecf254124ea49d` | `future-work` | Windows presence and real-device qualification remain unfinished; no exact release here. |
| `M3` | `01M54ZG55WRBCWA9JAR6B2P4T8` / `sha256:f381a60d87d53088a4d868b11db0b277d82cc2e1f6ce57d7418967c20ab700ee` | `awaiting-decision` | Remote is decided for 0.9.0; preserve shipped identity/network work and supply a new release carrier. |
| `M4` | `01M54ZG7AKXZ1JFNZ29A0CN0QH` / `sha256:a2778fa8b1a210992081ddc04a38dd7f97b565f084aa55d7336b48265577d45d` | `future-work` | After Remote 0.9.0; Pro coordination, packages and sites keep their reserved gates. |
| `M5` | `01M4VSZ1N0YAF9JQPATWX5E3K6` / `sha256:a8222073f8cf3358c393e5bb76bf7b3bd9217b3f0e97f3f5b40926d4899609a1` | `future-work` | Vertical specialist journeys; commerce remains subject to operator decisions. |
| `OPS-01` | `01M57TDCCB37AM4FJ24MN5YXTJ` / `sha256:65b556fa161f29a48b88c957471410f111bbdb964110b9a061e68f101076c981` | `awaiting-decision` | Moved to 0.8.0 by decision 01M57TDA2VTJ4BV0GDVDV8B2BP; exact carrier is not recorded. |
| `POLICY-04` | `01M56Q8ZJNW30DQN7RXKH62DES` / `sha256:f068fdbd2283143902a04b9dc7e16facd094e189b44af02c7e209080d6f41b08` | `awaiting-decision` | Product build in 0.8.0; estate adoption after upgrade and exact approvals; carrier not recorded. |
| `POLICY-05` | `01M55FHF7Z6JRBNVH3G5RVAM5V` / `sha256:38d0046042c38231b8f47cf0835b613446b4ce6277b411e76effcfcaf2362490` | `future-work` | Remote slice with 0.9.0; Workbench release still unplaced. One placement cannot claim both client slices are scheduled. |
| `PROC-01` | `01M4ZSFA0G0VMQAPM09PA77JZE` / `sha256:58c2cb65d30b4f9fcbede27b46d328da75d513eba992efb5d38031de2225983e` | `future-work` | Process foundation after format 3, with templates before or alongside it; exact release not selected. |
| `PROC-02` | `01M4ZSFAZR5J94PD9TX22DGSYN` / `sha256:a1e5102bb7f6ec2e591cc525b9fc9724d478727eef26b7b1dfaec59e3b3dcf5b` | `future-work` | Practices pack with PROC-01; keep the thirteen profile scopes. |
| `PROC-03` | `01M4ZSFAZRZ0P8JBYADG2FKDSE` / `sha256:3558df8eb90cd6bc9d02a1aacad9a1dbece81477712d5f5ffc4bd62cf225b247` | `future-work` | Pro conductor after PROC-01; no automatic scheduled duty execution. |
| `PROC-04` | `01M4ZSFBZ0XQ801GHJA6JD04Q0` / `sha256:2fea175ffec91013f0088a1c0c15ac80a83410ee76255294d02d6f9889a775af` | `future-work` | Domain lifecycles with their packs at M5. |
| `READ-04` | `01M593YKDJKJ8ZKNVWFGD71Q5K` / `sha256:dca60f139bfb580e0defb1b4bf460cf49991eff7208e4f946761c8f6087f769d` | `awaiting-decision` | 0.8.0 build after POLICY-03; adoption after POLICY-04 on upgraded estate; exact carrier not recorded. |
| `READ-05` | `01M55E85R3TSV18AJXRQD6PQX4` / `sha256:b56004dc2d8d389b58bf53e0709ead97d74f510866e56e40a4c06f8517a3653f` | `awaiting-decision` | Remote 0.9.0 with real-device qualification; exact carrier not recorded. |
| `READ-06` | `01M55E87Z3AGVYRRDC6656NBQE` / `sha256:0eeded57c848700b5fb98955a4f8f561922cdc2ed43701300fcbd022a6246fb0` | `future-work` | M4 after Remote alongside package, Knowledge, catalog and site decisions. |
| `TPL-01` | `01M55FH0R4PRKQCP3GB3HGZGSB` / `sha256:6a2e418a1a2a01d6e850f3de33a6f4808885478c82e903c39c038a1e212320a0` | `awaiting-decision` | 0.8.2 after 0.8.1 publication by decision 01M55FGYPCXKESRK27BV9SNZK3; carrier not recorded. |
| `TPL-02` | `01M55FH2REPEVXVZVGN9EG6DVP` / `sha256:2732b66b1b27066cdf27e8ef1b96601205f4ef3105e7f86d703d0fe347ea9fbe` | `awaiting-decision` | 0.8.2 with TPL-01, including roadmap template; carrier not recorded. |
| `TPL-03` | `01M4ZSFCY8MT061QYZ9RFPWNKQ` / `sha256:f386ad5b8fe02886e36073ab044927c1de4495268c8769836f2beef1db004443` | `future-work` | With PROC-01; Community manual execution and Pro automation remain distinct. |
| `TPL-04` | `01M4ZSFCY8W8S8P0BF0T81EN0S` / `sha256:5df03d6acf10d884f6f23ec3e2531a68e52c767f287783f25109120fdccc068c` | `future-work` | With each owning Domain Pack. |
| `TPL-COMMS-01` | `01M55FH4YH72RPG69XPV5FBTKW` / `sha256:b7ce34e032f0452a32e7cd0877b73526bf9f5233e856af6c4e2c3f642cdb1f67` | `awaiting-decision` | 0.8.2 with TPL-01 and TPL-02; preserve the decided specialized key; carrier not recorded. |

Do not reset original age anchors during adoption. Backlog work remains selected by planned-age; an interim placement can repair placement while still leaving age reminders. Any deliberate exception must name exact findings and expire within 30 days; none is proposed as a blanket audit waiver. The work-key-family body was derived from every selected current key, includes the historical milestone/REL suffixes and specialized TPL-COMMS/WEB-HOLD families, and applies prospectively from adoption. It neither renames old work nor reopens accepted work.

## Gate interpretations and their limitations

Nine current typed obligations have no selected discharge. They all retain the builder as obligation owner; the `obligation-gates` policy's operator owner routes the overall finding without transferring those acts. Its three proposed mappings below do not cover the six M4 gates. Empty or absent mappings produce **unavailable**, not satisfied. Each mapped interpretation belongs in the exact operator approval alongside the policy hash.

| Obligation | Exact decision revision / hash | Gate and proposed interpretation | Evidence that settles it |
|---|---|---|---|
| `m4-offers` | `01M50C0A9J9XW07H4A8RXGKBCE` / `sha256:e49bd751d8262a5501b3c3b3ae0f7ebe7a27c5ac342d2e6fb12346b1a05c1504` | any offer, price or commercial license. No faithful current typed gate: keep unavailable until an exact actionable gate is prepared and approved. M4 acceptance or an arbitrary publication date would not mean this event. | The asks are recorded at M4's planning. |
| `m4-registry-hosting` | `01M50CM8BXKJ95XXVAY4TKDPP3` / `sha256:61303caa5720c393e6fb6ed8ede7000a29e66c00a40de68070d235eac49dccd1` | creating the registry's hosting. No faithful current typed gate: keep unavailable until an exact actionable gate is prepared and approved. M4 acceptance or an arbitrary publication date would not mean this event. | The ask is recorded at M4's planning. |
| `m4-registry-policy` | `01M50CXXZPT2Y86R4SZAQPGJDE` / `sha256:6a2b9dcb0a48f1a3ffe5d801361845efb22f9fe7042dc322d84915a8b73aaaf1` | the registry opening to other publishers. No faithful current typed gate: keep unavailable until an exact actionable gate is prepared and approved. M4 acceptance or an arbitrary publication date would not mean this event. | The operator's decision adopting the policy is recorded. |
| `m4-sign-in` | `01M50CXZEDCABFC1YH2J9MVP7F` / `sha256:cf97436470ca78fe8e6cc4ddce941e0b0aa3ca8b718954474bc0cd3dbb061c70` | the first official sign-in. No faithful current typed gate: keep unavailable until an exact actionable gate is prepared and approved. M4 acceptance or an arbitrary publication date would not mean this event. | The ask is recorded before the first sign-in ships. |
| `m4-site-activation` | `01M50CY0VZDV6RMX0PWYTBYDA4` / `sha256:1eaeabdd51a1bd2bf7ab8fe7cd6bf5c4fce4cdcef5aebaab740c4f04ab21301b` | the first public site. No faithful current typed gate: keep unavailable until an exact actionable gate is prepared and approved. M4 acceptance or an arbitrary publication date would not mean this event. | The asks are recorded before the first site is put to the operator for publication. |
| `m4-catalog-boundaries` | `01M50DD3VCWBERAFK2RG74KDY5` / `sha256:b7fc289c419b767bbb60ae61f69463ec7b7a292b1ca747856a0c7228bb414d41` | an overlapping entry's record and package. No faithful current typed gate: keep unavailable until an exact actionable gate is prepared and approved. M4 acceptance or an arbitrary publication date would not mean this event. | The asks are recorded when the catalog records are planned. |
| `reviewer-separation-revisit` | `01M51CDST3GS2CDFXD22Z3NKJP` / `sha256:d52dca4eb1e669317a2a25ec1ade3abf95eae3d95598722d32752c73b066cd88` | closing CRED-01. Map closing CRED-01 to acceptance of its exact selected revision, as proposed in the body. ID-03 changing the available custody still independently prompts the ask; the one declared gate does not erase that requirement. | A decision on the Reviewer's key separation taken with CRED-01 or ID-03. |
| `security-disclosure` | `01M54ZFB99VAHZ9E5HDJPEV0TF` / `sha256:91253e2f1b1963ef1e1baafa1c9515a4db07524a14df5b1faec318706c813b3a` | 0.7.2's publication. Map to publication of product 01M4TRNE1GWEAQ17M4QCDHXEZ5 version 0.7.2. Approval exists in decision 01M55EJKZAXY93AB4CBXM9AM83, but no discharge is selected. Builder prepares the exact discharge evidence; do not call this missing approval. | The operator's approval of both drafts is recorded before 0.7.2 is published. |
| `writing-grant` | `01M54ZFDD1MG1GS49CQA56J6QW` / `sha256:086843a4140121cea86c686b496517c5f0058e82faf9bc7822fbefed3af0f3bc` | POLICY-01's merge. Map to 2026-10-18T16:45:40Z, the recorded local merge instant of POLICY-01 at commit 68a1c8eb7aa026905540683e29234dcdd8251f02. This absolute-time interpretation needs exact adoption approval. The writing grant or explicit decline is still absent. | The operator's grant is recorded, or the operator declines it. |

The six M4 gates depend on offers, hosting creation, publisher access, first sign-in, site readiness or catalog boundaries. The implemented closed condition kinds are `at`, `work-accepted`, `release-published` and `release-submission`, with all listed conditions required. They cannot express arbitrary provider acts, planning events or an OR between work acceptances. Prepare an exact existing-work gate when its exit genuinely entails the required event, or obtain a separate explicit timing interpretation from the operator. Do not add a new evaluator or silently substitute the M4 milestone. Missing mappings remain visible adoption limitations until that preparation is approved.

The Knowledge retirement is `01M503TVN9GWQW4HF2EAXFRMTJ` at `sha256:c30fc15b4c3058024844cb76cd3f80cafeada2a8b5fc199bb9e1470bf632b2a8`. Its condition requires Knowledge at M4 **and** every retained draft admitted as revision 1 of an Internal entry, no second editable copy, and relocation of the cold-start check input before removal. M4 acceptance alone does not prove that compound condition. The retirement policy therefore intentionally proposes an empty gate list and reports this record unavailable until a faithful exact gate is prepared. Builder retains the material and later records admission, input migration, actual removal and the true retiredAt; the operator approves the exact condition interpretation. The two completed retirements remain completed when the source snapshot proves their paths absent.

The global `review-wait` rule mechanically checks independent review. It does not read a brief to select operator review, enforce the security roster or establish work acceptance. Brief-required operator review and the fourteen duty routes above remain explicit preparation/review obligations. Missing or ineligible review must remain visible in the work's result and review queue even if this one age rule is satisfied. This limitation is not authority to weaken a brief or relabel security work.

## ESTATE-01 reconciliation and retained findings

The accepted audit result is `01M54XHGX572BM1GW7V3AWBRXR` at `sha256:58050390867a386e02a6b7b7cc37edabf6b1238cc7b501ad3c7631591638da51`. Settlement review `01M551E2PSH1ZS6HGDSQ6DHVB1` at `sha256:51ab17dcb41a89dc51bc1024617136d4cef2bc4e5b236b950dd45aecab8d5b47` records the dispositions; ESTATE-01 acceptance is `01M551E4QRHTB3JK77D4B098DF` at `sha256:8019ff16fd77610ae3175193ba6516ec95b5c5bfcf2f72a5979d86e3ff33be4c`. The adoption decision must cite these, D18, D20 and the accepted DESIGN-18/20 definitions. This proposal preserves that settlement rather than reopening its historical baseline.

| Original finding group | Recorded settlement or deliberate retention | Adoption consequence / owner |
|---|---|---|
| 25 audit warnings: 14 due documents, 8 spacing matches, 3 stale-judgment findings on two records | The settlement cleared the due documents and reconsidered the stale GOAL-04 judgments; eight spacing warnings remained deliberately. | Builder prepares the four exact writing entries after the operator's grant. New due/stale findings need their own current review, not a policy exception that suppresses a built-in condition. |
| Stale REL-060-DOCS / REL-061-DOCS states and activity claims | Settled with records; preserve separate withdrawn and published frozen sets and real unfinished scope elsewhere. | Work-state-consistency warns prospectively without changing accepted work. |
| 15 latest result-acceptance gaps, plus 9 earlier results | Thirteen old milestone results remain covered by their operator milestone acceptances; the ordinary WB-04, FIX-05 and FIX-07 evidence/dispositions were settled. Earlier iterations remain history. | No blanket acceptance, retrospective review quota or automatic reopened work. |
| 23 raw open/notified asks, none genuinely unanswered at audit time | Recorded answers retain their original asks; raw state is not the waiting predicate. | ask-age uses the derived waiting state and original age; unanswered current asks remain with the operator. |
| Eight original typed obligations | mailmap was completed and discharged; six M4 gates and reviewer-separation remain deliberate future work. | Preserve the seven retained obligations and account for the two later obligations still lacking discharge records in the exact gate table. |
| Four legacy NuGet versions and withdrawn 0.6.0/0.7.0 drafts | Operator retirement/removal observations settled them, including the resource revision and retained operation history. | No new removal and no claim of a fresh live provider observation. Future inventory includes their historical dispositions. |
| Pending Knowledge retirement; two completed retirements | Knowledge drafts stay until admission and input migration; completed paths remain absent with history retained. | No deletion by age. The compound gate remains explicitly unmapped. |
| Four one-time reports | Retained as historical decision evidence: design-02 gap audit, design-02 history inventory, Knowledge history review and Pro-license comparison. | Do not turn them into new living obligations or delete them to improve counts. |
| CODEX-16 abandoned attempt, WB-04 effect and related reviewed results | Settled in the audit follow-up; keep attribution and exact result history. | No retry or new external effect is inferred. |
| Stale or absent CRED-01, DESIGN-06/07, FIX-13, M3/M4/GOAL-05 placements | Recorded decisions settled the intent: CRED-01 paused, designs in the 0.9.0 cycle, FIX-13 in 0.7.2, Remote 0.9.0 and Pro after Remote. | Preserve those decisions in the typed preparation above; current release carriers remain a separate missing input. |
| DOCS-05 / DESIGN-18 collision; seven overlap groups | DOCS-05's kinds are folded into the accepted policy build. Distinct withdrawn/published sets, successive implementation/review/fix results, template scopes and guide layers remain distinct. | No duplicate policy engine, renamed history or blanket identity reconciliation. GOAL-06 retains its separate estate journey exit. |
| Five naming outliers and duplicate descriptive history | Keep M1-RELEASE, M2-RELEASE, WB-04, TPL-COMMS-01 and WEB-HOLD-01. Keep redundant DESIGN-09 acceptances and both NuGet resource roots with their provenance. | Prospective family matching; no deletion or renaming to satisfy policy. |
| 42 absent revision openings and one wrong process-profile revision number | The process-profile number was corrected. Missing authored lines were retained for policy/template follow-up; old pins and actual revision dates remain evidence. | The decided initial policy selects top-level design Markdown only. Builder corrects selected current files attributably and obtains any living-document clearance; catalog-family files remain outside this initial selection, explicitly accounted for below. |
| Nine historical decision-document coverage omissions, historical missing/current references and settled external effects | Retained as historical audit findings under the settlement; newer decisions and exact current evidence govern. | No backdated record/document rewrite or fabricated current claim. New unexplained changes still need their own disposition. |

The 42 revision-opening paths in the audit are listed here to preserve the full original inventory; this is not a claim that every one still lacks a line. Refresh each current pin before any later authorized edit. Top-level files enter the declared selector; catalog-family files are deliberately outside it and remain builder editorial/template follow-up rather than an undeclared recursive policy.

- `design/architecture.md`
- `design/blueprints.md`
- `design/catalog/aerospace-and-defense.md`
- `design/catalog/chemicals.md`
- `design/catalog/construction.md`
- `design/catalog/ecommerce-and-online-retail.md`
- `design/catalog/education-institutions.md`
- `design/catalog/environmental-consulting.md`
- `design/catalog/finance-accounting-and-audit.md`
- `design/catalog/food-and-agriculture.md`
- `design/catalog/governance-risk-and-compliance.md`
- `design/catalog/information-technology-services-and-operations.md`
- `design/catalog/insurance.md`
- `design/catalog/land-management.md`
- `design/catalog/legal-practice-operations.md`
- `design/catalog/life-sciences-quality-and-regulatory.md`
- `design/catalog/manufacturing-quality-and-operations.md`
- `design/catalog/marine.md`
- `design/catalog/marketing-and-advertising.md`
- `design/catalog/medical.md`
- `design/catalog/mining-and-minerals.md`
- `design/catalog/nonprofits-and-grants.md`
- `design/catalog/oil-field.md`
- `design/catalog/public-sector.md`
- `design/catalog/real-estate-and-property-management.md`
- `design/catalog/refinery.md`
- `design/catalog/telecommunications.md`
- `design/catalog/transportation-and-logistics.md`
- `design/catalog/utilities-and-power.md`
- `design/cli-and-sdk.md`
- `design/cohesion-contract.md`
- `design/editions-and-services.md`
- `design/estate-model.md`
- `design/identity-and-connectivity.md`
- `design/journeys.md`
- `design/master-design.md`
- `design/open-decisions.md`
- `design/platforms.md`
- `design/product-ip-graph.md`
- `design/remote.md`
- `design/storage-profiles.md`
- `design/workbench.md`

Before authoring adoption, the builder reruns check, status, progress, policies and writing reads on the upgraded estate at one retained explicit basis, reviews every current finding, and records exact remaining warnings in the adoption result. The audit's counts and the local fixture's passing assertions do not establish a zero-warning live estate. Current validation notes, environment limits and the unrun platform checks belong in the work result, not a committed build-output log.

## Proposed register mappings

The rows below propose exact identities from the generated references. POLICY-04 should compare them with the adopted build's retained inventory, review their concepts and add or remove mappings attributably. Each command, including a family command, has its own row. A family row covers only that exact invocation. Several operations share a term when they concern the same concept.

The following exact term rows supply the ten definitions absent from the selected register revision `01M56SV547Q0KVXNYE2G2K26WT` at `sha256:e9ff467118f94005e3a85e4474155aa6eb1c27b65f3fecf60c4d115809800cfb`. The builder reviews and adopts them with the mappings after upgrade and approval. These are proposed register text, not new record types or product operations. An undefined term remains unmet until the actual register contains a unique current definition and owning section. Existing term definitions also need editorial review: `Work order` must cover typed placement, `Release` its retained submission basis, and `Review` exact-result evidence and independence. A set comparison cannot establish that editorial quality.

| Term | Meaning | Not to be confused with | Owning document and section | Status |
|---|---|---|---|---|
| Checked repository change | A repository change prepared and observed through the existing push, pull-request and merge operations, with exact targets, evidence and authority checked before each effect. | A release publication; permission implied by a passing check; an unobserved remote effect | `docs/software.md` (product) # Checked repository changes (0.8.0) | current |
| Credential reference | Public metadata identifying a credential and its intended custody/use without retaining its secret value; resolution or an act still requires the applicable provider and authority. | A secret; a grant; proof that an external store is available | `design/estate-model.md` § 2 | current |
| Estate check | The read-only validation of an estate's records and selected sources, reporting built-in diagnostics and declared policy findings at an identified basis. | A verification record; acceptance; a release submission that has passed its fresh gate | `design/cli-and-sdk.md` # Estate policies and writing exceptions, planned for 0.8.0 | current |
| Estate status | A read-only projection of the selected estate's recorded state, findings and required attention, with its basis and unavailable inputs exposed. | Live provider health; progress claimed by an agent; authorization to act | `design/cli-and-sdk.md` # The command surface | current |
| Goal status | An attributable lifecycle record for a goal, distinct from its computed evaluation, run ledger and exact sponsor acceptance. | A conversation's goal state; proof of completion; a duty discharge | `design/goals.md` § 4 | current |
| Reconciliation | An attributable resolution of divergent successors in one record lineage that preserves the competing history and selects the continuing revision. | Merging unrelated identities; retrying an uncertain external effect; deleting a losing record | `design/estate-model.md` § 3 | current |
| Record recovery | Inspection and supported settlement of interrupted local record publication, preserving journals, exact bytes and visible uncertainty. | Restoring signing authority; resolving a provider effect by retry; accepting the recovered claim | `docs/software.md` (product) # Record publication and local recovery (0.7.2) | current |
| Resource | An identified estate dependency or asset, such as a repository, service or account, with its role and location metadata. | The estate itself; access credentials; authority over the named system | `design/estate-model.md` § 2 | current |
| Runtime connection | Machine-local connection configuration for reaching an estate runtime under its current transport and device authentication requirements. | Estate membership; a credential copied in an export; permission to use another principal's device | `design/cli-and-sdk.md` # The command surface | current |
| Work status | An attributable work-order state record whose current interpretation also respects exact acceptance, results and other lifecycle evidence. | A result; acceptance; an agent's progress claim; an automatic policy disposition | `design/estate-model.md` § 5 | current |

The coverage table below contains one exact row for each built command, API operation and record type. It includes family commands, policy and writing operations, exact-result review, placement and retained release-basis reads. No historical row is being used to claim a missing capability. Recompare both directions with the final adopted build; READ-04's later integration may add surfaces that this preparation cannot predict.

## Generated reference coverage

| Category | Reference | Term |
|---|---|---|
| api-operation | `GET /v1/asks` | Ask |
| api-operation | `GET /v1/check` | Estate check |
| api-operation | `GET /v1/communication` | Communication setting |
| api-operation | `GET /v1/companions` | Generated |
| api-operation | `GET /v1/companions/compare` | Generated |
| api-operation | `GET /v1/companions/render` | Generated |
| api-operation | `GET /v1/companions/sources` | Generated |
| api-operation | `GET /v1/context/original/{pin}` | Context package |
| api-operation | `GET /v1/context/work/{key}` | Context package |
| api-operation | `GET /v1/context/work/{key}/brief` | Brief |
| api-operation | `GET /v1/context/{manifest}/sources/{source}` | Context package |
| api-operation | `GET /v1/decisions` | Decision |
| api-operation | `GET /v1/decisions/{id}` | Decision |
| api-operation | `GET /v1/devices` | Device |
| api-operation | `GET /v1/documents` | Living document |
| api-operation | `GET /v1/estate-policies` | Estate policy |
| api-operation | `GET /v1/estate-policies/access` | Estate policy |
| api-operation | `GET /v1/estate-policies/evaluate` | Estate policy |
| api-operation | `GET /v1/estate-policies/kinds` | Estate policy |
| api-operation | `GET /v1/estate-policies/{key}/view` | Estate policy |
| api-operation | `GET /v1/goals` | Goal |
| api-operation | `GET /v1/goals/{id}` | Goal |
| api-operation | `GET /v1/goals/{id}/next` | Next-eligible view |
| api-operation | `GET /v1/handoff` | Hand-over |
| api-operation | `GET /v1/health` | Runtime |
| api-operation | `GET /v1/launchers` | Launcher |
| api-operation | `GET /v1/objects` | Object |
| api-operation | `GET /v1/objects/verify` | Object |
| api-operation | `GET /v1/objects/{hash}` | Object |
| api-operation | `GET /v1/operator/actions` | Reserved action |
| api-operation | `GET /v1/progress` | Progress report |
| api-operation | `GET /v1/providers` | Provider |
| api-operation | `GET /v1/reconcile` | Reconciliation |
| api-operation | `GET /v1/records` | Record |
| api-operation | `GET /v1/records/{id}` | Record |
| api-operation | `GET /v1/recover/inspect` | Record recovery |
| api-operation | `GET /v1/release-bases/{hash}` | Release |
| api-operation | `GET /v1/releases` | Release |
| api-operation | `GET /v1/runs` | Agent run |
| api-operation | `GET /v1/runtime/identity` | Runtime |
| api-operation | `GET /v1/status` | Estate status |
| api-operation | `GET /v1/validate` | Estate check |
| api-operation | `GET /v1/work/{key}` | Work order |
| api-operation | `GET /v1/work/{key}/brief` | Brief |
| api-operation | `GET /v1/writing-exceptions` | Writing exception |
| api-operation | `GET /v1/writing-exceptions/{id}` | Writing exception |
| api-operation | `POST /v1/changes/merge` | Checked repository change |
| api-operation | `POST /v1/changes/observe` | Checked repository change |
| api-operation | `POST /v1/changes/open` | Checked repository change |
| api-operation | `POST /v1/changes/push` | Checked repository change |
| api-operation | `POST /v1/communication` | Communication setting |
| api-operation | `POST /v1/communication/delegations` | Communication delegation |
| api-operation | `POST /v1/devices` | Device |
| api-operation | `POST /v1/devices/admit` | Admission |
| api-operation | `POST /v1/devices/recovery` | Recovery device |
| api-operation | `POST /v1/devices/rename` | Device |
| api-operation | `POST /v1/devices/revoke` | Admission |
| api-operation | `POST /v1/devices/rotate` | Device |
| api-operation | `POST /v1/documents/{id}/reviews` | Clearance |
| api-operation | `POST /v1/estate-policies` | Estate policy |
| api-operation | `POST /v1/estate-policies/{key}/exceptions` | Policy exception |
| api-operation | `POST /v1/estate-policies/{key}/exceptions/{id}/revoke` | Policy exception |
| api-operation | `POST /v1/estate-policies/{key}/retire` | Estate policy |
| api-operation | `POST /v1/estate-policies/{key}/revisions` | Estate policy |
| api-operation | `POST /v1/estate/export` | Export |
| api-operation | `POST /v1/estate/upgrade` | Migration |
| api-operation | `POST /v1/goals` | Goal |
| api-operation | `POST /v1/goals/{id}/accept` | Acceptance |
| api-operation | `POST /v1/goals/{id}/activate` | Goal activation |
| api-operation | `POST /v1/goals/{id}/evaluate` | Evaluation |
| api-operation | `POST /v1/goals/{id}/pause` | Goal status |
| api-operation | `POST /v1/goals/{id}/resume` | Goal status |
| api-operation | `POST /v1/goals/{id}/step` | Step |
| api-operation | `POST /v1/goals/{id}/stop` | Goal status |
| api-operation | `POST /v1/harness-qualifications` | Harness qualification |
| api-operation | `POST /v1/objects` | Object |
| api-operation | `POST /v1/operator/record` | Reserved action |
| api-operation | `POST /v1/pairing/confirm` | Pairing |
| api-operation | `POST /v1/pairing/hello` | Pairing |
| api-operation | `POST /v1/pairings` | Pairing |
| api-operation | `POST /v1/records` | Record |
| api-operation | `POST /v1/releases/observe` | Release |
| api-operation | `POST /v1/releases/submit` | Release |
| api-operation | `POST /v1/resume` | Resume |
| api-operation | `POST /v1/runs` | Agent run |
| api-operation | `POST /v1/runs/{id}/stop` | Stop request |
| api-operation | `POST /v1/runtime/certificate` | Runtime |
| api-operation | `POST /v1/shutdown` | Daemon |
| api-operation | `POST /v1/status/observations` | Estate observation |
| api-operation | `POST /v1/work/amend` | Amendment |
| api-operation | `POST /v1/work/assign` | Assignment |
| api-operation | `POST /v1/work/attempt` | Attempt |
| api-operation | `POST /v1/work/progress` | Progress claim |
| api-operation | `POST /v1/work/result` | Result |
| api-operation | `POST /v1/work/review` | Review |
| api-operation | `POST /v1/work/takeover` | Work takeover |
| api-operation | `POST /v1/work/{key}/placement` | Work order |
| api-operation | `POST /v1/writing-exceptions` | Writing exception |
| api-operation | `POST /v1/writing-exceptions/{id}/remove` | Writing exception |
| command | `vericairn ask` | Ask |
| command | `vericairn changes` | Checked repository change |
| command | `vericairn changes merge` | Checked repository change |
| command | `vericairn changes observe` | Checked repository change |
| command | `vericairn changes open` | Checked repository change |
| command | `vericairn changes push` | Checked repository change |
| command | `vericairn check` | Verification |
| command | `vericairn check observe` | Verification |
| command | `vericairn check run` | Verification |
| command | `vericairn communication` | Communication setting |
| command | `vericairn communication delegate` | Communication delegation |
| command | `vericairn communication reset` | Communication setting |
| command | `vericairn communication set` | Communication setting |
| command | `vericairn communication show` | Communication setting |
| command | `vericairn connections` | Runtime connection |
| command | `vericairn connections add` | Runtime connection |
| command | `vericairn connections list` | Runtime connection |
| command | `vericairn connections remove` | Runtime connection |
| command | `vericairn context` | Context package |
| command | `vericairn context catalogue` | Context package |
| command | `vericairn context prepare` | Context package |
| command | `vericairn context read` | Context package |
| command | `vericairn decisions` | Decision |
| command | `vericairn devices` | Device |
| command | `vericairn devices admit` | Admission |
| command | `vericairn devices create` | Device |
| command | `vericairn devices list` | Device |
| command | `vericairn devices pair` | Pairing |
| command | `vericairn devices recovery` | Recovery device |
| command | `vericairn devices rename` | Device |
| command | `vericairn devices revoke` | Admission |
| command | `vericairn devices rotate` | Device |
| command | `vericairn devices writes` | Admission |
| command | `vericairn docs` | Frozen documentation set |
| command | `vericairn docs check` | Frozen documentation set |
| command | `vericairn docs companions` | Generated |
| command | `vericairn docs declare` | Living document |
| command | `vericairn docs living` | Living document |
| command | `vericairn docs notes` | Notes window |
| command | `vericairn docs regenerate` | Generated |
| command | `vericairn docs render` | Generated |
| command | `vericairn docs review` | Clearance |
| command | `vericairn doctor` | Runtime |
| command | `vericairn estate` | Estate |
| command | `vericairn estate check` | Estate check |
| command | `vericairn estate export` | Export |
| command | `vericairn estate init` | First authority |
| command | `vericairn estate reconcile` | Reconciliation |
| command | `vericairn estate restore` | Inventory-bound restore |
| command | `vericairn estate upgrade` | Migration |
| command | `vericairn estate writing-exceptions` | Writing exception |
| command | `vericairn estate writing-exceptions add` | Writing exception |
| command | `vericairn estate writing-exceptions list` | Writing exception |
| command | `vericairn estate writing-exceptions remove` | Writing exception |
| command | `vericairn estate writing-exceptions show` | Writing exception |
| command | `vericairn goal` | Goal |
| command | `vericairn goal accept` | Acceptance |
| command | `vericairn goal activate` | Goal activation |
| command | `vericairn goal custody-exception` | Custody exception |
| command | `vericairn goal evaluate` | Evaluation |
| command | `vericairn goal next` | Next-eligible view |
| command | `vericairn goal pause` | Goal status |
| command | `vericairn goal propose` | Goal |
| command | `vericairn goal resume` | Goal status |
| command | `vericairn goal show` | Goal |
| command | `vericairn goal step` | Step |
| command | `vericairn goal stop` | Goal status |
| command | `vericairn handoff` | Hand-over |
| command | `vericairn mcp` | MCP server |
| command | `vericairn mcp orientation` | Context package |
| command | `vericairn mcp serve` | MCP server |
| command | `vericairn objects` | Object |
| command | `vericairn objects list` | Object |
| command | `vericairn objects put` | Object |
| command | `vericairn objects verify` | Object |
| command | `vericairn operator` | Operator |
| command | `vericairn operator list` | Operator |
| command | `vericairn operator record` | Reserved action |
| command | `vericairn policies` | Estate policy |
| command | `vericairn policies check` | Estate policy |
| command | `vericairn policies declare` | Estate policy |
| command | `vericairn policies except` | Policy exception |
| command | `vericairn policies kinds` | Estate policy |
| command | `vericairn policies list` | Estate policy |
| command | `vericairn policies retire` | Estate policy |
| command | `vericairn policies revise` | Estate policy |
| command | `vericairn policies revoke-exception` | Policy exception |
| command | `vericairn policies show` | Estate policy |
| command | `vericairn progress` | Progress report |
| command | `vericairn records` | Record |
| command | `vericairn records describe` | Record |
| command | `vericairn records format` | Record format |
| command | `vericairn records hash` | Content hash |
| command | `vericairn records new` | Record |
| command | `vericairn records show` | Record |
| command | `vericairn records validate` | Record |
| command | `vericairn recover` | Record recovery |
| command | `vericairn recover compare` | Record recovery |
| command | `vericairn recover inspect` | Record recovery |
| command | `vericairn release` | Release |
| command | `vericairn release basis` | Release |
| command | `vericairn release list` | Release |
| command | `vericairn release observe` | Release |
| command | `vericairn release providers` | Provider |
| command | `vericairn release submit` | Release |
| command | `vericairn resume` | Resume |
| command | `vericairn runtime` | Runtime |
| command | `vericairn runtime configure` | Runtime |
| command | `vericairn runtime install` | Daemon |
| command | `vericairn runtime rotate` | Runtime |
| command | `vericairn runtime serve` | Daemon |
| command | `vericairn runtime start` | Daemon |
| command | `vericairn runtime status` | Runtime |
| command | `vericairn runtime stop` | Daemon |
| command | `vericairn runtime uninstall` | Daemon |
| command | `vericairn sources` | Source |
| command | `vericairn sources adopt` | Source |
| command | `vericairn sources clone` | Source |
| command | `vericairn sources inspect` | Source |
| command | `vericairn sources observe` | Observation |
| command | `vericairn status` | Estate status |
| command | `vericairn updates` | Release notice |
| command | `vericairn updates configure` | Release notice |
| command | `vericairn version` | Release |
| command | `vericairn work` | Work order |
| command | `vericairn work amend` | Amendment |
| command | `vericairn work assign` | Assignment |
| command | `vericairn work attempt` | Attempt |
| command | `vericairn work brief` | Brief |
| command | `vericairn work observe` | Estate observation |
| command | `vericairn work place` | Work order |
| command | `vericairn work result` | Result |
| command | `vericairn work review` | Review |
| command | `vericairn work takeover` | Work takeover |
| command | `vericairn worker` | Worker |
| command | `vericairn worker configure` | Worker configuration |
| command | `vericairn worker launchers` | Launcher |
| command | `vericairn worker qualify` | Harness qualification |
| command | `vericairn worker run` | Agent run |
| command | `vericairn worker runs` | Agent run |
| command | `vericairn worker stop` | Stop request |
| record-type | `acceptance` | Acceptance |
| record-type | `admission` | Admission |
| record-type | `amendment` | Amendment |
| record-type | `ask` | Ask |
| record-type | `assignment` | Assignment |
| record-type | `attempt` | Attempt |
| record-type | `authority` | Authority |
| record-type | `communication-delegation` | Communication delegation |
| record-type | `communication-setting` | Communication setting |
| record-type | `credential-reference` | Credential reference |
| record-type | `custody-exception` | Custody exception |
| record-type | `decision` | Decision |
| record-type | `device` | Device |
| record-type | `discharge` | Discharge |
| record-type | `document-review` | Clearance |
| record-type | `document-revision` | Pin |
| record-type | `estate` | Estate |
| record-type | `estate-policy` | Estate policy |
| record-type | `goal` | Goal |
| record-type | `goal-status` | Goal status |
| record-type | `harness-qualification` | Harness qualification |
| record-type | `living-document` | Living document |
| record-type | `migration` | Migration |
| record-type | `observation` | Observation |
| record-type | `operation` | Operation |
| record-type | `policy-exception` | Policy exception |
| record-type | `possibility` | Possibility |
| record-type | `principal` | Principal |
| record-type | `product` | Product |
| record-type | `qualification` | Qualification |
| record-type | `reconciliation` | Reconciliation |
| record-type | `release` | Release |
| record-type | `resource` | Resource |
| record-type | `result` | Result |
| record-type | `retirement` | Retirement |
| record-type | `review` | Review |
| record-type | `roster` | Roster |
| record-type | `verification` | Verification |
| record-type | `work-order` | Work order |
| record-type | `work-progress` | Progress claim |
| record-type | `work-status` | Work status |
| record-type | `writing-exception` | Writing exception |
