# Epic 24.0 — owner/legal approval register

**Technical changes may be reviewed independently. PUBLIC LAUNCH BLOCKED. MUNICIPAL OUTREACH BLOCKED.** Neither a green test suite nor Admin review is legal approval. Privacy and Terms are clearly marked local drafts; no publication/deployment is authorized in this Epic.

## Owner facts and unresolved decisions

| Item | Evidence / required owner action |
|---|---|
| Controller | Owner stated Jernej Furman s.p., represented by Jernej Furman, Slovenia. Confirm exact registered legal identity and supply the actual registered postal address. Do not publish the earlier bracketed placeholder. |
| Privacy contact | Owner confirmed admin@doggydrop.app is monitored. Verify operational handling, identity checks, response ownership and public configuration; no test email sent. |
| Purpose/legal basis | No approved basis. Decide separately for account/auth, profiles/dogs, exact walk recording, optional social/Nearby/community aggregation, route providers, contributions/public photos, confirmations, achievements, security/rate handling and operational logs. Browser permission and Terms checkbox are not blanket GDPR consent. |
| Retention | Approve durations/criteria and deletion triggers for each audit matrix row, including retained infrastructure evidence, rejected uploads, logs, orphan assets and backups. Current display windows are not retention. |
| Providers | Confirm active Render/PG/media/SMTP accounts and regions, Google/CDN/map/ORS/weather/Overpass flows, role allocation, DPAs/sub-processors and applicable transfer safeguards. No conclusion from a product name alone. |
| Rights | Approve access/correction/export/erasure/restriction/objection/portability process, verification, exceptions and response deadlines; establish complaint information and appropriate supervisory authority details. Self-service is not the entirety of rights handling. |
| Children | No approved minimum age; do not invent 13+/16+. Decide intended audience, consent/guardian requirements and handling. |
| Risk assessment | Review precise movement trails, social features, photos and location providers; determine need for DPIA and other governance measures. Technical audit is not a GDPR certification. |
| Terms | Approve service scope, contribution license, moderation, lawful limitations of liability, termination/changes/notice, consumer rights where applicable. Do not restore an unlimited promotional-photo waiver. |
| OSM/ODbL | Resolve actual database use, attribution, derivative/collective database and share-alike/access obligations for intended public use and municipal distribution. See osm-data-use.md. |
| Credentials | Historical exposed administrator credential remains exposed in Git history. Owner rotation/session action is separate; no history rewrite or production password test performed. |
| Dependency security | Unchanged MailKit/MimeKit NU1902 advisories remain. Review applicability/remediation separately before launch; this privacy change does not certify dependencies as vulnerability-free. |
| Operations | Verify media/log/backup deletion and incident/contact handling, transport/access controls, recovery testing and live provider configuration without sending secrets to this review. |

## Proposed narrow contribution permission — not approved legal text

For legal review: a non-exclusive permission limited to receiving, technically resizing/encoding, storing, moderating and displaying submitted material within DoggyDrop and the service's ordinary delivery infrastructure. No transfer of ownership; no waiver of moral rights; no separate advertising/social-media promotional use by default. Clarify duration, removal requests, accepted public-infrastructure evidence and necessary provider sublicensing with legal counsel. This is a drafting input, not a license automatically imposed by code.

The pre-existing mandatory registration `AcceptTerms` checkbox and its server validation remain unchanged. No new privacy consent checkbox, legal acceptance ledger, versioned consent record or migration was introduced. Final Terms replacement and acceptance/version strategy require a distinct approved launch decision; do not imply the current checkbox evidences approval of this draft or all processing.

## Source framework for owner/legal review

The [GDPR official text](https://eur-lex.europa.eu/eli/reg/2016/679/) is the reference for transparency, lawful-basis, rights, retention, processor and transfer decisions. Article 13/14 information and source-derived records require an actual assessment; a technical inventory alone is insufficient. [OSMF attribution guidance](https://osmfoundation.org/wiki/Licence/Attribution_Guidelines) and [licensing FAQ](https://osmfoundation.org/wiki/Licence/Licence_and_Legal_FAQ) inform the separate ODbL review. These references do not establish compliance for this deployment.

## Release gates

1. Confirm controller/address/contact and all provider facts.
2. Approve purpose/basis/retention/children/rights decisions and implement any resulting concrete changes in a separate reviewed scope.
3. Approve final Slovenian Privacy/Terms, content license and ODbL treatment; remove draft warnings only after actual approval.
4. Recheck device behavior and self-service, verify owner operational setup and deletion/backups procedure.
5. Obtain explicit authorization for commit/push and launch; no deployment is part of Epic 24.0.
