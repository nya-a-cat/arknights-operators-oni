# Critter render-state hotfix

## Scope

The operator overlay hides vanilla duplicant animation controllers while an operator appearance is active. A carried `Pickupable` can temporarily become a child of the duplicant storage hierarchy, so it must stay outside that suppression list.

The hotfix filters controllers with a `Pickupable` ancestor before applying visibility or tint changes. The public stable branch and Steam Workshop content remain unchanged until the isolated A/B test passes.

## Verification

- WSL Mono build completed successfully.
- Existing localization, animation mapper, appearance catalog, asset resolver, fallback, and resource-index tests passed.
- Required in-game check: move a critter with only this mod enabled, then repeat with the mod pack; compare color before, during, and after transport.

