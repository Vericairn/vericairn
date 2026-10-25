// SPDX-License-Identifier: Apache-2.0
// Copyright (c) 2026 Jason Furr

using System.Text.Json;

namespace Vericairn.Records.Templates;

/// <summary>Immutable declarative content shipped with its versioned generators and checks.</summary>
public static class FirstPartyTemplates
{
    /// <summary>The six complete contracts, available without an estate's files or an external package.</summary>
    public static IReadOnlyList<TemplateDefinition> Definitions { get; } = Read();

    private static TemplateDefinition[] Read()
    {
        string[] contracts =
        [
            """
            {
              "format": "vericairn.template/1",
              "id": "vericairn.estate/design-document",
              "version": "1.0.0",
              "purpose": "A design whose recorded instruction, authored reasoning, implementation and review criteria stay consistent.",
              "artifact": "document",
              "sections": [
                {
                  "id": "revision",
                  "heading": "Recorded revision",
                  "purpose": "Name the current document revision from its record, preserving the authored revision history.",
                  "presence": "required",
                  "role": {
                    "write": "builder",
                    "review": "reviewer",
                    "clear": "operator"
                  },
                  "synthesis": {
                    "mode": "generated",
                    "generator": "vericairn.estate/revision-line-v1",
                    "inputs": [
                      "version=1"
                    ]
                  },
                  "sources": [
                    {
                      "id": "revisions",
                      "kind": "records",
                      "type": "document-revision"
                    }
                  ],
                  "cohesion": [
                    {
                      "id": "record-derived",
                      "kind": "derived-from",
                      "summary": "The complete section equals fresh generation from its recorded sources."
                    }
                  ],
                  "fidelity": "final",
                  "quality": {
                    "review": "self",
                    "checks": [
                      {
                        "name": "matches-generator"
                      }
                    ]
                  }
                },
                {
                  "id": "operator-words",
                  "heading": "The operator's words",
                  "purpose": "Retain verbatim words from decisions naming this document and the asks they answer.",
                  "presence": "required",
                  "role": {
                    "write": "builder",
                    "review": "reviewer",
                    "clear": "operator"
                  },
                  "synthesis": {
                    "mode": "generated",
                    "generator": "vericairn.estate/operator-words-v1",
                    "inputs": [
                      "version=1"
                    ]
                  },
                  "sources": [
                    {
                      "id": "decisions",
                      "kind": "records",
                      "type": "decision"
                    },
                    {
                      "id": "asks",
                      "kind": "records",
                      "type": "ask"
                    }
                  ],
                  "cohesion": [
                    {
                      "id": "record-derived",
                      "kind": "derived-from",
                      "summary": "The complete section equals fresh generation from its recorded sources."
                    }
                  ],
                  "fidelity": "final",
                  "quality": {
                    "review": "self",
                    "checks": [
                      {
                        "name": "matches-generator"
                      }
                    ]
                  }
                },
                {
                  "id": "answer",
                  "heading": "The answer, and what this document decides",
                  "purpose": "State the answer and exact scope of the design.",
                  "presence": "required",
                  "role": {
                    "write": "builder",
                    "review": "reviewer",
                    "clear": "operator"
                  },
                  "synthesis": {
                    "mode": "authored"
                  },
                  "sources": [],
                  "cohesion": [],
                  "fidelity": "final",
                  "quality": {
                    "review": "independent",
                    "checks": [
                      {
                        "name": "length",
                        "min": 1
                      }
                    ]
                  }
                },
                {
                  "id": "vocabulary",
                  "heading": "Vocabulary",
                  "purpose": "Define the terms the design owns.",
                  "presence": "optional",
                  "role": {
                    "write": "builder",
                    "review": "reviewer",
                    "clear": "operator"
                  },
                  "synthesis": {
                    "mode": "authored"
                  },
                  "sources": [],
                  "cohesion": [],
                  "fidelity": "final",
                  "quality": {
                    "review": "independent",
                    "checks": [
                      {
                        "name": "length",
                        "min": 1
                      }
                    ]
                  }
                },
                {
                  "id": "body",
                  "heading": "Design",
                  "purpose": "Explain the design in the order it needs, preserving authored subsections.",
                  "presence": "required",
                  "role": {
                    "write": "builder",
                    "review": "reviewer",
                    "clear": "operator"
                  },
                  "synthesis": {
                    "mode": "authored"
                  },
                  "sources": [
                    {
                      "id": "answer",
                      "kind": "section",
                      "section": "answer"
                    },
                    {
                      "id": "instruction",
                      "kind": "section",
                      "section": "operator-words"
                    }
                  ],
                  "cohesion": [
                    {
                      "id": "answer-agrees",
                      "kind": "judgment",
                      "summary": "The answer and body agree, and the design addresses the recorded instruction."
                    }
                  ],
                  "fidelity": "final",
                  "quality": {
                    "review": "independent",
                    "checks": [
                      {
                        "name": "length",
                        "min": 1
                      }
                    ]
                  }
                },
                {
                  "id": "records",
                  "heading": "Records and compatibility",
                  "purpose": "State each changed record and its format; distinguish existing contracts from proposed additions.",
                  "presence": {
                    "option": "changes-records",
                    "value": "yes"
                  },
                  "role": {
                    "write": "builder",
                    "review": "reviewer",
                    "clear": "operator"
                  },
                  "synthesis": {
                    "mode": "authored"
                  },
                  "sources": [],
                  "cohesion": [
                    {
                      "id": "record-coverage",
                      "kind": "judgment",
                      "summary": "Every named record type exists in a record-format document or is explicitly new for a named format."
                    }
                  ],
                  "fidelity": "final",
                  "quality": {
                    "review": "independent",
                    "checks": [
                      {
                        "name": "length",
                        "min": 1
                      }
                    ]
                  }
                },
                {
                  "id": "journeys",
                  "heading": "Journeys and tests",
                  "purpose": "Name the journeys and checks proving the design, explicitly marking proposed journeys.",
                  "presence": "required",
                  "role": {
                    "write": "builder",
                    "review": "reviewer",
                    "clear": "operator"
                  },
                  "synthesis": {
                    "mode": "authored"
                  },
                  "sources": [
                    {
                      "id": "journey-catalogue",
                      "kind": "file",
                      "path": "design/journeys.md"
                    }
                  ],
                  "cohesion": [
                    {
                      "id": "journey-coverage",
                      "kind": "judgment",
                      "summary": "Every cited journey exists in the journey catalogue or is explicitly proposed, and the tests cover the design."
                    }
                  ],
                  "fidelity": "final",
                  "quality": {
                    "review": "independent",
                    "checks": [
                      {
                        "name": "length",
                        "min": 1
                      }
                    ]
                  }
                },
                {
                  "id": "implementation-notes",
                  "heading": "Implementation, as work orders",
                  "purpose": "Explain the implementation order; distinguish proposed keys from recorded work orders.",
                  "presence": "required",
                  "role": {
                    "write": "builder",
                    "review": "reviewer",
                    "clear": "operator"
                  },
                  "synthesis": {
                    "mode": "authored"
                  },
                  "sources": [
                    {
                      "id": "recorded-work",
                      "kind": "section",
                      "section": "implementation"
                    }
                  ],
                  "cohesion": [
                    {
                      "id": "work-coverage",
                      "kind": "judgment",
                      "summary": "Every implementation key is recorded or explicitly proposed, with no invented placement."
                    }
                  ],
                  "fidelity": "final",
                  "quality": {
                    "review": "independent",
                    "checks": [
                      {
                        "name": "length",
                        "min": 1
                      }
                    ]
                  }
                },
                {
                  "id": "implementation",
                  "heading": "Recorded work orders",
                  "purpose": "List the work linked by a decision naming this document, or by a work-order input naming its revisions.",
                  "presence": "required",
                  "role": {
                    "write": "builder",
                    "review": "reviewer",
                    "clear": "operator"
                  },
                  "synthesis": {
                    "mode": "generated",
                    "generator": "vericairn.estate/design-work-v1",
                    "inputs": [
                      "version=1"
                    ]
                  },
                  "sources": [
                    {
                      "id": "work-orders",
                      "kind": "records",
                      "type": "work-order"
                    },
                    {
                      "id": "decisions",
                      "kind": "records",
                      "type": "decision"
                    },
                    {
                      "id": "revisions",
                      "kind": "records",
                      "type": "document-revision"
                    }
                  ],
                  "cohesion": [
                    {
                      "id": "record-derived",
                      "kind": "derived-from",
                      "summary": "The complete section equals fresh generation from its recorded sources."
                    }
                  ],
                  "fidelity": "final",
                  "quality": {
                    "review": "self",
                    "checks": [
                      {
                        "name": "matches-generator"
                      }
                    ]
                  }
                },
                {
                  "id": "decisions",
                  "heading": "Recorded decisions",
                  "purpose": "Show decisions naming this design and their answered asks.",
                  "presence": "required",
                  "role": {
                    "write": "builder",
                    "review": "reviewer",
                    "clear": "operator"
                  },
                  "synthesis": {
                    "mode": "generated",
                    "generator": "vericairn.estate/design-decisions-v1",
                    "inputs": [
                      "version=1"
                    ]
                  },
                  "sources": [
                    {
                      "id": "decisions",
                      "kind": "records",
                      "type": "decision"
                    },
                    {
                      "id": "asks",
                      "kind": "records",
                      "type": "ask"
                    }
                  ],
                  "cohesion": [
                    {
                      "id": "record-derived",
                      "kind": "derived-from",
                      "summary": "The complete section equals fresh generation from its recorded sources."
                    }
                  ],
                  "fidelity": "final",
                  "quality": {
                    "review": "self",
                    "checks": [
                      {
                        "name": "matches-generator"
                      }
                    ]
                  }
                }
              ],
              "options": [
                {
                  "id": "changes-records",
                  "values": [
                    "no",
                    "yes"
                  ],
                  "default": "no"
                }
              ]
            }
            """,
            """
            {
              "format": "vericairn.template/1",
              "id": "vericairn.estate/design-index",
              "version": "1.0.0",
              "purpose": "Keep the design inventory complete from document-revision records.",
              "artifact": "document",
              "sections": [
                {
                  "id": "narrative",
                  "heading": "Context",
                  "purpose": "Explain the design set and retain the authored catalogue narrative.",
                  "presence": "optional",
                  "role": {
                    "write": "builder",
                    "review": "reviewer",
                    "clear": "operator"
                  },
                  "synthesis": {
                    "mode": "authored"
                  },
                  "sources": [],
                  "cohesion": [],
                  "fidelity": "final",
                  "quality": {
                    "review": "independent",
                    "checks": [
                      {
                        "name": "length",
                        "min": 1
                      }
                    ]
                  }
                },
                {
                  "id": "designs",
                  "heading": "Design documents",
                  "purpose": "List every currently pinned Markdown design, in path order.",
                  "presence": "required",
                  "role": {
                    "write": "builder",
                    "review": "reviewer",
                    "clear": "operator"
                  },
                  "synthesis": {
                    "mode": "generated",
                    "generator": "vericairn.estate/design-index-v1",
                    "inputs": [
                      "version=1"
                    ]
                  },
                  "sources": [
                    {
                      "id": "revisions",
                      "kind": "records",
                      "type": "document-revision"
                    }
                  ],
                  "cohesion": [
                    {
                      "id": "record-derived",
                      "kind": "derived-from",
                      "summary": "The complete section equals fresh generation from its recorded sources."
                    }
                  ],
                  "fidelity": "final",
                  "quality": {
                    "review": "self",
                    "checks": [
                      {
                        "name": "matches-generator"
                      }
                    ]
                  }
                }
              ]
            }
            """,
            """
            {
              "format": "vericairn.template/1",
              "id": "vericairn.estate/open-decisions",
              "version": "1.0.0",
              "purpose": "Questions remain asks; answers remain decisions, with their exact words and identities.",
              "artifact": "document",
              "sections": [
                {
                  "id": "narrative",
                  "heading": "Context",
                  "purpose": "Explain the decision register and retain historical numbered questions.",
                  "presence": "optional",
                  "role": {
                    "write": "builder",
                    "review": "reviewer",
                    "clear": "operator"
                  },
                  "synthesis": {
                    "mode": "authored"
                  },
                  "sources": [],
                  "cohesion": [],
                  "fidelity": "final",
                  "quality": {
                    "review": "independent",
                    "checks": [
                      {
                        "name": "length",
                        "min": 1
                      }
                    ]
                  }
                },
                {
                  "id": "decisions",
                  "heading": "Asks and decisions",
                  "purpose": "Show every current ask and decision, the recommendation first, consequences and recorded answer links.",
                  "presence": "required",
                  "role": {
                    "write": "builder",
                    "review": "reviewer",
                    "clear": "operator"
                  },
                  "synthesis": {
                    "mode": "generated",
                    "generator": "vericairn.estate/open-decisions-v1",
                    "inputs": [
                      "version=1"
                    ]
                  },
                  "sources": [
                    {
                      "id": "asks",
                      "kind": "records",
                      "type": "ask"
                    },
                    {
                      "id": "decisions",
                      "kind": "records",
                      "type": "decision"
                    }
                  ],
                  "cohesion": [
                    {
                      "id": "record-derived",
                      "kind": "derived-from",
                      "summary": "The complete section equals fresh generation from its recorded sources."
                    }
                  ],
                  "fidelity": "final",
                  "quality": {
                    "review": "self",
                    "checks": [
                      {
                        "name": "matches-generator"
                      }
                    ]
                  }
                }
              ]
            }
            """,
            """
            {
              "format": "vericairn.template/1",
              "id": "vericairn.estate/readme",
              "version": "1.0.0",
              "purpose": "Explain the estate, getting started and navigation, with a record-derived release view.",
              "artifact": "document",
              "sections": [
                {
                  "id": "overview",
                  "heading": "Overview",
                  "purpose": "Explain what this estate is and whom it serves.",
                  "presence": "required",
                  "role": {
                    "write": "builder",
                    "review": "reviewer",
                    "clear": "operator"
                  },
                  "synthesis": {
                    "mode": "authored"
                  },
                  "sources": [],
                  "cohesion": [],
                  "fidelity": "final",
                  "quality": {
                    "review": "independent",
                    "checks": [
                      {
                        "name": "length",
                        "min": 1
                      }
                    ]
                  }
                },
                {
                  "id": "getting-started",
                  "heading": "Getting started",
                  "purpose": "Give usable setup and first-use instructions.",
                  "presence": "required",
                  "role": {
                    "write": "builder",
                    "review": "reviewer",
                    "clear": "operator"
                  },
                  "synthesis": {
                    "mode": "authored"
                  },
                  "sources": [],
                  "cohesion": [],
                  "fidelity": "final",
                  "quality": {
                    "review": "independent",
                    "checks": [
                      {
                        "name": "length",
                        "min": 1
                      }
                    ]
                  }
                },
                {
                  "id": "navigation",
                  "heading": "Where to go next",
                  "purpose": "Link the designs and operational documentation.",
                  "presence": "required",
                  "role": {
                    "write": "builder",
                    "review": "reviewer",
                    "clear": "operator"
                  },
                  "synthesis": {
                    "mode": "authored"
                  },
                  "sources": [],
                  "cohesion": [],
                  "fidelity": "final",
                  "quality": {
                    "review": "independent",
                    "checks": [
                      {
                        "name": "length",
                        "min": 1
                      }
                    ]
                  }
                },
                {
                  "id": "releases",
                  "heading": "Published releases",
                  "purpose": "Show the recorded publications without inferring a release from prose.",
                  "presence": "required",
                  "role": {
                    "write": "builder",
                    "review": "reviewer",
                    "clear": "operator"
                  },
                  "synthesis": {
                    "mode": "generated",
                    "generator": "vericairn.estate/published-releases-v1",
                    "inputs": [
                      "version=1"
                    ]
                  },
                  "sources": [
                    {
                      "id": "releases",
                      "kind": "records",
                      "type": "release"
                    }
                  ],
                  "cohesion": [
                    {
                      "id": "record-derived",
                      "kind": "derived-from",
                      "summary": "The complete section equals fresh generation from its recorded sources."
                    }
                  ],
                  "fidelity": "final",
                  "quality": {
                    "review": "self",
                    "checks": [
                      {
                        "name": "matches-generator"
                      }
                    ]
                  }
                }
              ]
            }
            """,
            """
            {
              "format": "vericairn.template/1",
              "id": "vericairn.estate/release-notes",
              "version": "1.0.0",
              "purpose": "Explain a release, its changes, validation, compatibility and remaining limitations against recorded releases.",
              "artifact": "document",
              "sections": [
                {
                  "id": "overview",
                  "heading": "Release",
                  "purpose": "Name the exact release and explain its outcome.",
                  "presence": "required",
                  "role": {
                    "write": "builder",
                    "review": "reviewer",
                    "clear": "operator"
                  },
                  "synthesis": {
                    "mode": "authored"
                  },
                  "sources": [],
                  "cohesion": [],
                  "fidelity": "final",
                  "quality": {
                    "review": "independent",
                    "checks": [
                      {
                        "name": "length",
                        "min": 1
                      }
                    ]
                  }
                },
                {
                  "id": "changes",
                  "heading": "Changes",
                  "purpose": "Describe the behavior delivered by this release.",
                  "presence": "required",
                  "role": {
                    "write": "builder",
                    "review": "reviewer",
                    "clear": "operator"
                  },
                  "synthesis": {
                    "mode": "authored"
                  },
                  "sources": [],
                  "cohesion": [],
                  "fidelity": "final",
                  "quality": {
                    "review": "independent",
                    "checks": [
                      {
                        "name": "length",
                        "min": 1
                      }
                    ]
                  }
                },
                {
                  "id": "compatibility",
                  "heading": "Compatibility and upgrading",
                  "purpose": "State compatibility and required upgrade actions.",
                  "presence": "required",
                  "role": {
                    "write": "builder",
                    "review": "reviewer",
                    "clear": "operator"
                  },
                  "synthesis": {
                    "mode": "authored"
                  },
                  "sources": [],
                  "cohesion": [],
                  "fidelity": "final",
                  "quality": {
                    "review": "independent",
                    "checks": [
                      {
                        "name": "length",
                        "min": 1
                      }
                    ]
                  }
                },
                {
                  "id": "validation",
                  "heading": "Validation and limitations",
                  "purpose": "State observed checks and remaining limitations.",
                  "presence": "required",
                  "role": {
                    "write": "builder",
                    "review": "reviewer",
                    "clear": "operator"
                  },
                  "synthesis": {
                    "mode": "authored"
                  },
                  "sources": [],
                  "cohesion": [],
                  "fidelity": "final",
                  "quality": {
                    "review": "independent",
                    "checks": [
                      {
                        "name": "length",
                        "min": 1
                      }
                    ]
                  }
                },
                {
                  "id": "releases",
                  "heading": "Published releases",
                  "purpose": "Show recorded published releases; a scope.product choice limits this view to that product.",
                  "presence": "required",
                  "role": {
                    "write": "builder",
                    "review": "reviewer",
                    "clear": "operator"
                  },
                  "synthesis": {
                    "mode": "generated",
                    "generator": "vericairn.estate/published-releases-v1",
                    "inputs": [
                      "version=1"
                    ]
                  },
                  "sources": [
                    {
                      "id": "releases",
                      "kind": "records",
                      "type": "release"
                    }
                  ],
                  "cohesion": [
                    {
                      "id": "record-derived",
                      "kind": "derived-from",
                      "summary": "The complete section equals fresh generation from its recorded sources."
                    }
                  ],
                  "fidelity": "final",
                  "quality": {
                    "review": "self",
                    "checks": [
                      {
                        "name": "matches-generator"
                      }
                    ]
                  }
                }
              ]
            }
            """,
            """
            {
              "format": "vericairn.template/1",
              "id": "vericairn.estate/roadmap",
              "version": "1.0.0",
              "purpose": "An authored direction and a complete release plan derived only from typed work-order placements.",
              "artifact": "document",
              "sections": [
                {
                  "id": "narrative",
                  "heading": "Direction",
                  "purpose": "Explain the direction, sequencing and uncertainty in authored prose.",
                  "presence": "required",
                  "role": {
                    "write": "builder",
                    "review": "reviewer",
                    "clear": "operator"
                  },
                  "synthesis": {
                    "mode": "authored"
                  },
                  "sources": [],
                  "cohesion": [],
                  "fidelity": "final",
                  "quality": {
                    "review": "independent",
                    "checks": [
                      {
                        "name": "length",
                        "min": 1
                      }
                    ]
                  }
                },
                {
                  "id": "releases",
                  "heading": "Release placements",
                  "purpose": "Every release row comes from typed current work-order placements.",
                  "presence": "required",
                  "role": {
                    "write": "builder",
                    "review": "reviewer",
                    "clear": "operator"
                  },
                  "synthesis": {
                    "mode": "generated",
                    "generator": "vericairn.estate/roadmap-releases-v1",
                    "inputs": [
                      "version=1"
                    ]
                  },
                  "sources": [
                    {
                      "id": "work-orders",
                      "kind": "records",
                      "type": "work-order"
                    }
                  ],
                  "cohesion": [
                    {
                      "id": "record-derived",
                      "kind": "derived-from",
                      "summary": "The complete section equals fresh generation from its recorded sources."
                    }
                  ],
                  "fidelity": "final",
                  "quality": {
                    "review": "self",
                    "checks": [
                      {
                        "name": "matches-generator"
                      },
                      {
                        "name": "vericairn.estate/roadmap-placements-v1"
                      }
                    ]
                  }
                }
              ]
            }
            """
        ];
        return contracts.Select(text =>
        {
            using JsonDocument json = JsonDocument.Parse(text);
            return TemplateDefinition.Read(json.RootElement);
        }).ToArray();
    }
}
