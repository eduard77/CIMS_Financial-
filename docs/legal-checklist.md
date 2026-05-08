\# Legal Checklist — Genera Systems Financials



> Living document. Track status per item. Review at the start of every sprint.

>

> \*\*Not legal advice.\*\* This is an internal checklist of areas to investigate and actions to take. Get a UK technology / SaaS solicitor to interpret each item against your specific circumstances. Keep dated copies of solicitor correspondence in `docs/legal/correspondence/`.

>

> \*\*Owner:\*\* Eduard / Genera Systems Ltd

> \*\*Last reviewed:\*\* `\_\_\_\_\_\_\_\_\_\_\_\_\_` (update on every review)



\---



\## Status legend



| Symbol | Meaning |

|---|---|

| ⬜ | Not started |

| 🟡 | In progress |

| 🟢 | Done — evidence filed in `docs/legal/` |

| ⚪ | Not applicable (record reason) |

| 🔴 | Blocked — see notes |

| 👁 | Watching brief (no action needed yet, monitor) |



\---



\## How this checklist is structured



Items are grouped by the \*\*trigger\*\* that forces the action: a sprint number, a contractual milestone (e.g., first paying customer), or a state change (e.g., taking investment). An item must be cleared before the trigger event, not after. Each item has an ID for cross-referencing in commits, ADRs, and meeting notes.



The blockers in \*\*Tier 0\*\* must be resolved before Sprint 1 lands any persisting code. Everything else cascades from the development plan in `CLAUDE.md`.



\---



\## Tier 0 — Before Sprint 1 (mandatory)



These must be cleared before any persistent data model or business logic lands. They affect schema, dependency choices, and IP ownership.



\### LEG-001 — IP ownership chain inside Genera Systems Ltd ⬜



\*\*Why:\*\* All CIMS, QA, Optimisation, ITPManager, PAFM, and Financials code must be unambiguously owned by the Ltd, not by Eduard personally. Buyers and investors check this first; ambiguity here is the most common reason early-stage SaaS deals collapse in due diligence.



\*\*Action:\*\*

\- Sign a written \*\*IP Assignment Deed\*\* from Eduard (individual) to Genera Systems Ltd, covering all software, documentation, brand assets, and derivative works produced to date.

\- Going forward, all code is authored on behalf of the company. Note this in commit author config (`user.email` set to a company email).

\- Keep the executed deed in `docs/legal/ip-assignment-2026.pdf`.



\*\*Cost:\*\* £200–£500 for a solicitor-drafted deed, or use a reputable template.



\---



\### LEG-002 — UK GDPR / DPA 2018 baseline decisions ⬜



\*\*Why:\*\* Affects schema, retention, hosting region, and the DPA you'll later sign with customers. Cannot be retrofitted cheaply.



\*\*Action — pin these decisions and write them up as ADR-0002:\*\*

\- \*\*Lawful basis\*\* for each category of processing (likely: Contract performance for project data; Legitimate Interest for security/audit logs; Consent for marketing only).

\- \*\*Data residency\*\* — UK-only hosting for HRB projects; document the position for non-HRB.

\- \*\*Retention\*\* — minimum 12 years post-project for HRB golden-thread data (aligned with extended limitation periods under the Defective Premises Act 1972 as amended by BSA 2022). Confirm with solicitor.

\- \*\*Subject Access Request\*\* workflow — define the technical mechanism, target turnaround (one calendar month statutory).

\- \*\*Breach notification path\*\* — 72-hour clock to ICO, customer notification per DPA terms.

\- \*\*Sub-processors\*\* — list any (Azure, AWS, Sentry, etc.) and surface to customers.



\*\*Reference:\*\* ICO Accountability Framework; ICO guidance on cloud computing.



\*\*Watching brief:\*\* UK data protection reform (Data (Use and Access) Act 2025) — confirm current state with solicitor; some provisions may relax certain obligations.



\---



\### LEG-003 — ICO registration as data controller ⬜



\*\*Why:\*\* Statutory requirement for any UK organisation processing personal data (with limited exemptions). Almost certainly applies to Genera Systems Ltd.



\*\*Action:\*\* Register at ico.org.uk; pay annual data protection fee (tiered by organisation size — typically £40–£60 for a small organisation, but verify current fee).



\*\*Cost:\*\* £40–£60/year approx — confirm at point of registration.



\---



\### LEG-004 — Open-source licence audit pipeline ⬜



\*\*Why:\*\* The single most common way SaaS startups acquire toxic IP is via a transitive GPL-licensed dependency. Once it ships, it's hard to remove. Catch in CI from day one.



\*\*Action:\*\*

\- Add `dotnet-project-licenses` (or FOSSA, Snyk, or equivalent) to the Sprint 0 CI pipeline.

\- Allow-list: MIT, Apache-2.0, BSD-2/3-Clause, MS-PL, ISC.

\- Block-list: GPL-2.0/3.0, AGPL, SSPL — fail the build.

\- Watch-list: LGPL (acceptable for dynamic linking but document each instance), MPL-2.0 (file-level copyleft, document use).

\- File the report at every release in `docs/legal/oss-licence-reports/`.



\*\*Cost:\*\* Free for open-source tools; commercial scanners £0–£1k/year.



\---



\### LEG-005 — AI-assisted code authorship record ⬜



\*\*Why:\*\* Investor and acquirer due diligence increasingly asks how much code was AI-generated and under what terms. Documenting this is cheap; reconstructing it later is impossible.



\*\*Action:\*\*

\- Keep a copy of Anthropic's Commercial Terms of Service (the version in force at the time) in `docs/legal/ai-terms/anthropic-tos-<date>.pdf`. Refresh each time terms change.

\- The same applies to any other AI tool used (GitHub Copilot, Cursor, etc.).

\- The terms (as currently published) confirm customer ownership of outputs for commercial use, but keep dated evidence regardless.

\- Note in commit messages where AI assistance was material (e.g., `\[AI-assisted]` tag for fully generated modules; not needed for line-by-line completion).



\*\*Cost:\*\* £0.



\---



\### LEG-006 — Standards purchase (ISO 19650 series) ⬜



\*\*Why:\*\* You cannot legitimately claim "ISO 19650 alignment" in marketing or pitches without owning the standards. A future Kitemark auditor will ask for licensed copies. Reading the standards is also necessary to implement them correctly.



\*\*Action — buy from BSI Shop:\*\*

\- BS EN ISO 19650-1:2018 (Concepts and principles)

\- BS EN ISO 19650-2:2018 (Delivery phase)

\- BS EN ISO 19650-3:2020 (Operational phase)

\- BS EN ISO 19650-4:2022 (Information exchange)

\- BS EN ISO 19650-5:2020 (Security-minded approach)

\- BS EN ISO 19650-6:2025 (Health and safety information) — once relevant.

\- BS 8536 (Design, manufacture and construction for operability) — referenced by 19650.



\*\*Cost:\*\* Approximately £150–£300 per part. Budget \~£1,200–£1,500 for the full set.



\*\*Watching brief:\*\* ISO 19650 parts 1 and 2 amendments scheduled to publish in 2026; part 3 amendments mid-2026. Re-purchase amended editions when published.



\---



\### LEG-007 — Statute reading ⬜



\*\*Why:\*\* F4 (Valuations) computes statutory deadlines from these acts. Wrong calculations create real legal exposure for customers using the platform. Reading the source material directly, not summaries, is essential.



\*\*Action — read and summarise in `docs/legal/statute-notes/`:\*\*

\- \*\*Housing Grants, Construction and Regeneration Act 1996\*\* (as amended by Local Democracy, Economic Development and Construction Act 2009).

\- \*\*Scheme for Construction Contracts (England and Wales) Regulations 1998\*\* (as amended).

\- \*\*Building Safety Act 2022\*\* — particularly Parts 3 and 4.

\- \*\*Building (Higher-Risk Buildings Procedures) (England) Regulations 2023\*\* — particularly Regulation 31 (golden thread).

\- \*\*Construction (Design and Management) Regulations 2015\*\* (CDM).

\- \*\*Defective Premises Act 1972\*\* (as amended by BSA 2022 — limitation period extended to 30 years for relevant HRB defects).



\*\*Cost:\*\* £0 — all available free at legislation.gov.uk.



\---



\## Tier 1 — Before F3 (Change Management) starts



\### LEG-008 — NEC4 contract form licensing position ⬜



\*\*Why:\*\* F3 implements NEC4 Compensation Event lifecycle. Process and statutory deadlines are not copyrightable; clause text and form templates \*\*are\*\*, owned by NEC publishing (ICE).



\*\*Action:\*\*

\- Confirm with solicitor what F3 can and cannot reproduce.

\- Contact NEC publishing to ask about software-developer licensing for clause references in generated documents (Notification, Quotation, AFI letters).

\- Default position until clarified: F3 implements the \*process\* (event lifecycle, statutory clock periods, valuation routes) and \*\*does not embed clause text or form templates\*\*. Generated documents reference clauses by number only ("CE under clause 60.1(1)") and do not quote clause text.

\- Document the position in ADR-0008.



\*\*Cost:\*\* Solicitor consultation £300–£800; licence cost (if pursued) unknown — request quote from NEC.



\---



\### LEG-009 — JCT contract form licensing position ⬜



\*\*Why:\*\* Same issue as NEC4. JCT Ltd has historically been more restrictive on software reproduction than NEC.



\*\*Action:\*\* Same approach as LEG-008 — confirm scope with solicitor, contact JCT Ltd directly, default to process-only implementation, document in ADR-0009.



\*\*Cost:\*\* Solicitor £300–£800; JCT licence cost unknown.



\---



\### LEG-010 — NRM2 / RICS reproduction position ⬜



\*\*Why:\*\* F1 imports NRM2-format BoQs. NRM2 is a RICS publication. Reading and parsing data structured to NRM2 conventions is fine; reproducing RICS template documents may not be.



\*\*Action:\*\* Confirm with solicitor and/or RICS. Default: parse NRM2-structured input, do not reproduce RICS template forms or guidance text.



\---



\## Tier 2 — Before F4 (Valuations and AFP) starts



\### LEG-011 — Statutory notice template review 🔴 (depends on LEG-007, LEG-008, LEG-009)



\*\*Why:\*\* F4 generates Payment Notices and Pay Less Notices. Wrong format or wrong calculation creates direct liability for the customer.



\*\*Action:\*\*

\- Have a construction-disputes solicitor review the generated notice templates and the calculation logic against the Construction Act, the Scheme, and the standard contract forms (or statutory fallback if contract is non-compliant).

\- Specifically validate: due-date calculation, Payment Notice deadline (5 working days from due date by default), Pay Less Notice deadline (typically 7 days before final date for payment, but contract-dependent), and "smash and grab" adjudication exposure if notices are missed.

\- Document template approval in `docs/legal/notice-templates/`.



\*\*Cost:\*\* Construction solicitor specialist review £1,500–£3,500.



\---



\### LEG-012 — F4 ITP-gating mechanism — contractual basis ⬜



\*\*Why:\*\* A core CIMS Financials feature is that AFP measured-work lines are blocked unless the corresponding ITP is signed off in QA. This is a strong commercial feature but needs to be aligned with the contract — the contract, not the software, determines what is "due."



\*\*Action:\*\* Confirm with solicitor that the gating is positioned as a \*\*commercial control\*\* (preventing the contractor from billing for work not yet inspection-passed) rather than a \*\*legal block\*\* on contractual entitlement. UI language must avoid suggesting the platform determines payment entitlement.



\---



\## Tier 3 — Before F5 (Subcontract / CIS) starts



\### LEG-013 — HMRC developer registration for CIS API ⬜



\*\*Why:\*\* F5 verifies subcontractors via HMRC's CIS API. Developer access is gated.



\*\*Action:\*\*

\- Register at developer.service.hmrc.gov.uk.

\- Apply for production access for the CIS API.

\- Accept HMRC's Terms of Use; keep a dated copy.

\- Plan for software recognition process if applicable.



\*\*Cost:\*\* £0 to register; effort cost in compliance work.



\---



\### LEG-014 — Reverse Charge VAT logic ⬜



\*\*Why:\*\* Reverse Charge VAT for construction services (in force March 2021) has specific scope (which services, which trader types, which projects). Wrong application creates customer VAT exposure.



\*\*Action:\*\*

\- Engage a construction-VAT-aware accountant to validate the F5 tax engine logic against current HMRC guidance (VAT Notice 735).

\- Cover edge cases: end-user notification, intermediary suppliers, mixed supplies, residential supplies.

\- Document validation in `docs/legal/vat-engine-validation/`.



\*\*Cost:\*\* Specialist accountant review £600–£1,500.



\---



\### LEG-015 — Making Tax Digital compatibility 👁



\*\*Why:\*\* If the platform claims MTD compatibility, it must meet HMRC software requirements. Otherwise, position as "MTD-compatible accounting software integration" via Xero/Sage/QuickBooks.



\*\*Action:\*\* Default position — F8 integrates with MTD-compatible accounting software; Financials itself is not the MTD-recognised software. Confirm with solicitor / accountant.



\---



\## Tier 4 — Before first paying customer



\### LEG-016 — Customer-facing legal pack drafted ⬜



\*\*Why:\*\* Cannot bill, cannot sign a customer, without these in place. Bad templates create disputes and slow sales.



\*\*Action — get a UK SaaS solicitor to draft or review:\*\*

\- \*\*Master SaaS Agreement\*\* / Terms of Service — licence, fees, term, termination, IP, warranties, liability cap, indemnities, governing law.

\- \*\*Data Processing Agreement\*\* (DPA) — Article 28 GDPR-compliant; controller/processor terms; sub-processor list; SCCs / IDTA for any non-UK processing.

\- \*\*Service Level Agreement\*\* (SLA) — uptime target, support hours, response times, remedies (service credits).

\- \*\*Acceptable Use Policy\*\* (AUP) — prohibited uses, security, fair use.

\- \*\*Privacy Policy\*\* — public-facing, ICO-aligned.

\- \*\*Cookie Policy\*\* — if Web app uses any non-essential cookies.



\*\*Cost:\*\* £1,500–£3,500 for a competent UK SaaS solicitor to draft or review a complete pack from a sensible template baseline.



\---



\### LEG-017 — Liability cap and PI insurance alignment ⬜



\*\*Why:\*\* The liability cap in the SaaS terms must be backed by Professional Indemnity insurance with a coverage limit at least matching it. Otherwise the cap is unenforceable in some scenarios and uninsured in others.



\*\*Action:\*\*

\- Quote PI insurance (specialist tech / SaaS PI broker; typical entry-level cover £1m–£5m).

\- Ensure separate cover from any consultancy PI — they are different risks; the consultancy's PM PI does not cover the SaaS product.

\- Align the contractual liability cap to the PI coverage limit.



\*\*Cost:\*\* PI insurance £800–£3,000/year for a small SaaS at entry coverage levels — varies considerably with risk profile.



\---



\### LEG-018 — Trademark protection (selective) ⬜



\*\*Why:\*\* "Genera Systems" and any product brand names worth registering. CIMS itself is generic and unlikely to be registrable. Public sector buyers expect brand stability.



\*\*Action:\*\*

\- Search UK IPO and EUIPO for clashes.

\- Apply for UK trademark on "Genera Systems" if clear; consider brand-mark registration for distinctive product logos.

\- Domain hygiene: own genera-systems.com / .co.uk and obvious variants.



\*\*Cost:\*\* £170 per UK class (DIY filing); £500–£1,200 with a trademark attorney for one mark in a couple of classes.



\---



\## Tier 5 — Before public sector pitches



\### LEG-019 — Cyber Essentials Plus ⬜



\*\*Why:\*\* Effectively a hard prerequisite for most UK public sector buying. Many private sector clients also require it.



\*\*Action:\*\*

\- Engage an accredited certification body (NCSC list).

\- Self-assess against Cyber Essentials baseline first.

\- Book Cyber Essentials Plus assessment (the audited version).

\- Plan for annual renewal.



\*\*Cost:\*\* Cyber Essentials self-assessment \~£300–£500. Plus assessment \~£1,500–£3,000 first year. Renewal annually.



\---



\### LEG-020 — G-Cloud / Digital Marketplace listing 👁



\*\*Why:\*\* Direct route to public sector buyers under £100k+ contracts via G-Cloud framework.



\*\*Action:\*\*

\- Watch for next G-Cloud iteration (calls open periodically).

\- Read the framework agreement carefully — call-off contract terms, pricing constraints, exit obligations.

\- Apply when CIMS + Financials are demonstrably saleable and Cyber Essentials Plus is in place.



\*\*Cost:\*\* Application is free; effort is significant.



\---



\### LEG-021 — Procurement Act 2023 alignment 👁



\*\*Why:\*\* The Procurement Act 2023 has been replacing the Public Contracts Regulations 2015 for above-threshold procurement. Affects how you bid, how frameworks are structured, transparency obligations.



\*\*Action:\*\* Confirm current state with solicitor at the point of first public sector bid; understand the Procurement Review Unit's role; understand new transparency obligations (procurement notices, KPI publication for major contracts).



\---



\## Tier 6 — Ongoing / triggered by company events



\### LEG-022 — ISO 27001 certification roadmap ⬜



\*\*Why:\*\* Universally requested by enterprise buyers for security assurance. Cyber Essentials Plus is the floor; ISO 27001 is the ceiling for the segment Genera Systems is targeting.



\*\*Action:\*\* Plan post first 2–3 paying customers. Engage a UKAS-accredited certification body (BSI, LRQA, NQA, BM TRADA, etc.). Build the Information Security Management System (ISMS) before audit; expect 6–12 months of preparation.



\*\*Cost:\*\* ISMS build effort + certification £8k–£20k first year, £4k–£8k surveillance years 2 and 3.



\---



\### LEG-023 — BSI Kitemark for BIM software 👁



\*\*Why:\*\* Industry-recognised mark proving ISO 19650 alignment of the software. See earlier discussion — pursue once revenue, ISO 27001, and reference customers exist.



\*\*Action:\*\* Defer until post-Series A or equivalent revenue milestone. Watching brief on the scheme's evolution in the meantime.



\---



\### LEG-024 — IP / contributor agreements for hires ⬜



\*\*Why:\*\* When the first developer joins (employee, contractor, or freelance), every line of their code must be unambiguously owned by Genera Systems Ltd. This requires a written IP assignment in their contract.



\*\*Action:\*\* Have employment / contractor agreement templates ready before any hire. Solicitor-reviewed. Cover IP, confidentiality, post-termination restrictions, moral rights waiver.



\*\*Cost:\*\* £400–£800 for solicitor-reviewed templates.



\---



\### LEG-025 — Investment readiness pack 👁



\*\*Why:\*\* When fundraising, due diligence will go through everything in this checklist. Better to have it organised in advance than to assemble it under deal pressure.



\*\*Action:\*\* Maintain a "data room" folder structure mirroring this checklist; refresh quarterly. Particular attention to LEG-001 (IP chain), LEG-005 (AI authorship), and LEG-016 (customer contracts).



\---



\### LEG-026 — Limitation period awareness for HRB work ⬜



\*\*Why:\*\* The Building Safety Act 2022 extended the limitation period under the Defective Premises Act 1972 to \*\*30 years retrospectively\*\* for relevant HRB defects, and 15 years prospectively. If software contributes to a compliance failure on an HRB, the exposure window is unusually long.



\*\*Action:\*\*

\- Discuss with PI broker whether long-tail cover is available.

\- Consider contractual limitation clauses in customer SaaS terms (subject to UCTA / consumer reasonableness tests).

\- Document retention for golden-thread evidence aligned with the longer of: contract retention + 12 years; or HRB-relevant retention as advised.



\---



\## Watching brief — emerging UK regulation



| Item | Status | Notes |

|---|---|---|

| Data (Use and Access) Act 2025 — UK data protection reform | 👁 | Some provisions may affect cookie consent, legitimate interest grounds, automated decision-making. |

| AI regulation (UK sectoral approach) | 👁 | Watch for any obligations on AI-augmented decision support in a regulated sector like construction safety. |

| Procurement Act 2023 implementation | 👁 | Affects all public sector bidding routes. |

| EU AI Act (extraterritorial reach) | 👁 | If serving any EU customer, AI features may trigger obligations. |

| NIS2 / UK cyber resilience successors | 👁 | If platform ever falls within "essential" or "important" service definitions. |

| Building Safety Regulator operational changes | 👁 | Practitioner registers, gateway delays — affects customer urgency and product priorities. |



\---



\## Review discipline



\- \*\*Every sprint review\*\* — confirm no items have moved into "must clear before next sprint" without action.

\- \*\*Quarterly\*\* — full re-read of this document; update statuses, refresh standards purchases, update the watching brief table.

\- \*\*Trigger events\*\* — first paying customer, first hire, first investor conversation, first HRB project, first public sector bid — re-read the relevant tier in full before proceeding.



\---



\## Where each item ends up



| Folder | Contents |

|---|---|

| `docs/legal/` | All executed documents, standards purchase receipts, statute notes, ADRs related to legal decisions. |

| `docs/legal/correspondence/` | Dated solicitor and counterparty correspondence. |

| `docs/legal/oss-licence-reports/` | Per-release licence audit outputs. |

| `docs/legal/ai-terms/` | Dated copies of AI tool terms in force at time of use. |

| `docs/legal/statute-notes/` | Internal summaries of statutes, with section references and last-reviewed dates. |

| `docs/legal/notice-templates/` | Solicitor-approved generated-document templates (F4 notices, F5 certificates). |

| `docs/legal/vat-engine-validation/` | Accountant sign-off on F5 tax engine logic. |

| `docs/decisions/` | ADRs that capture legal positions affecting architecture (e.g., NEC4 clause-text policy). |



\---



\*Last full review: `\_\_\_\_\_\_\_\_\_\_\_\_\_`. Next review due: every sprint demo + quarterly deep review.\*

