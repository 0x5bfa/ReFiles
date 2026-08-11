# Adding a storage provider

A storage provider should make a backend look like a normal ReFiles storage model without teaching generic browsing/presentation how that backend works.

## 1. Define identity and addressing first

Specify:

- how locations are represented;
- what makes an item the same item;
- rename/move behavior;
- case sensitivity;
- recovery/re-resolution behavior;
- root containment rules.

Do not start from UI-specific item classes.

## 2. Implement hierarchy/enumeration

Provide folder/item resolution and progressive enumeration. Honor cancellation and avoid buffering the complete directory if the backend can stream results.

## 3. Add capabilities incrementally

Implement only supported features, for example:

- readable streams;
- properties;
- thumbnails;
- preview;
- operations;
- search;
- change observation.

Capability absence is valid.

## 4. Isolate backend constraints

Network I/O, Shell STA requirements, archive-session serialization, authentication, and retry belong inside provider/platform boundaries. Generic callers should not reproduce them.

## 5. Define lifetime

Document ownership of provider sessions, item models, streams, caches, and feature results. Make cleanup work for cancellation and partial construction failure.

## 6. Protect secrets

Credentials/tokens/passwords must not appear in display addresses, logs, exception text intended for telemetry, snapshots, committed fixtures, or CI output.

## 7. Test in layers

Unit-test provider-independent semantics with doubles. Use isolated integration fixtures/servers/platform APIs for actual backend behavior. Do not depend on public network endpoints for normal CI.

## Acceptance checklist

- [ ] Stable/provider-appropriate identity.
- [ ] Address/root normalization.
- [ ] Progressive cancellable enumeration.
- [ ] Optional behavior exposed as capabilities.
- [ ] No provider switches added to generic browse/presentation.
- [ ] Stream/session ownership documented and tested.
- [ ] Failures/cancellation normalized sufficiently for callers.
- [ ] Secrets excluded from diagnostics/test output.
- [ ] Integration tests are isolated and reproducible.
