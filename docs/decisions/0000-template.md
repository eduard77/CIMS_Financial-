# ADR-NNNN: <Short noun phrase title>

> **Filename convention:** `NNNN-kebab-case-title.md` where `NNNN` is the next sequential 4-digit number.
> Copy this template, replace `NNNN` and the title, fill in every section. Delete this blockquote on commit.

- **Status:** Proposed | Accepted | Deprecated | Superseded by [ADR-NNNN](./NNNN-...)
- **Date:** YYYY-MM-DD
- **Deciders:** <names>
- **Sprint:** <sprint number when decided>
- **Related:** ADR-NNNN, LEG-NNN (legal checklist items), F<n> (financial sub-module)

---

## Context

What is the problem we are addressing? Describe the situation factually. Include relevant background: what's already in place, what changed, what's now unclear or contested. Two or three short paragraphs is usually right.

The reader should finish this section understanding *why a decision is needed*. Avoid stating the decision or even the options here — keep this section about the problem, not the solution.

---

## Decision drivers

The criteria that mattered when choosing between options. List them so the reader can later check whether the decision still stands as drivers change.

- Driver 1 — e.g., "must not duplicate CIMS master data"
- Driver 2 — e.g., "must survive CIMS being temporarily unavailable"
- Driver 3 — e.g., "solo developer, must be operationally simple"

---

## Options considered

At least two options. Three is better. Even when the decision feels obvious, writing the alternatives down forces honesty about what was rejected and why.

### Option A: <name>

Brief description. One paragraph.

**Pros:** Bullet list.

**Cons:** Bullet list.

### Option B: <name>

Brief description. One paragraph.

**Pros:** Bullet list.

**Cons:** Bullet list.

### Option C: <name> *(if applicable)*

…

---

## Decision

We chose **Option <X>**.

State the decision in one or two clear sentences. No hedging. If the decision is conditional ("we chose X for now, will revisit when Y"), say that explicitly.

---

## Consequences

What follows from this decision — positive, negative, and neutral. Be honest about the downsides; an ADR that lists only benefits is not credible and not useful when the decision is revisited.

### Positive

- Consequence 1
- Consequence 2

### Negative

- Consequence 1
- Consequence 2

### Neutral / informational

- Consequence 1

---

## Compliance and verification

How will we know we are following this decision? What would catch a violation?

- Code-level check: e.g., "static analysis rule forbidding direct references to other products' DbContexts"
- Review check: e.g., "PR template asks: does this PR introduce a new cross-product call pattern?"
- Test check: e.g., "contract test enforces event schema version"
- Architectural check: e.g., "quarterly review of integration patterns against this ADR"

---

## References

- Plan section: `cims-financial-integration-plan-v0.2.md` §<n>
- Legal checklist: LEG-NNN
- External: <link to standard, RFC, vendor doc, or article>
- Related ADRs: ADR-NNNN, ADR-NNNN

---

## Revision history

| Date | Author | Change |
|---|---|---|
| YYYY-MM-DD | <name> | Initial version |
