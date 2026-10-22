# Literal country and currency names

At original source 5fac706a7983a6d359b39acbd670e6800afe020e, Countries and
Currencies POST/PUT copy Name and ShortName/LongName directly from plain string
model properties without annotations, initialization or trimming. Projects do
not enable nullable reference types; MVC's inferred required metadata does not
apply to unannotated reference properties. Newtonsoft JSON deserializes strings
without empty-to-null coercion for string properties. EF requires non-null names
with lengths 50 for Country.Name and Currency.LongName and 10 for ShortName.
This is source-derived evidence, not observed original binary/HTTP execution.

This separate candidate sets Required.AllowEmptyStrings on those three existing
request fields. It preserves literal empty, whitespace and padded strings through
POST/PUT responses, detail/list and persistence. Country metadata and the other
currency name retain their supplied values; IDs, existing CreatedDate, sentinel
parents and all association rows remain unchanged. Existing null/omitted-name
400 and length checks stay as target controls; original null/missing/length
failure statuses and envelopes remain open obligations.

Proposed new HTTP/PostgreSQL executions: 21 (18 literal rows across three fields,
three literals, POST/PUT; three per-field null/omitted/oversized/auth controls).
All original 525 execution identities and multiplicities remain; six required
field cases continue null rejection. The shared fixture body remains unchanged.
Full forecast 546. Original 20 diagnostics, exports and receipt; owned assembly
80 percent coverage including generated code and zero exclusions remain intact.

This is a source-review draft integrated against accepted PR87 protected main
`ff3c87a55837a2e12e113b9e2b4ce62d94ce495f`. It is not published, validated or accepted. Local Release,
focused/full tests, formatting/static/security and HTTP/PostgreSQL checks NOT
RUN: no qualified Commerce SDK custody manager/allocation. The owner-approved migration validation-order exception permits reviewed draft
publication before local validation; it waives no required exact-head or main check.
No deployment, source SQL change, cutover or whole-source/migration closure.
