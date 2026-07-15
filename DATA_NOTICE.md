# Data notice

`arknights_oni_mod_work/AmiyaDuplicantMod/assets/catalog/operator_appearances_20260604.json` is a lightweight development snapshot derived from public PRTS operator metadata on 2026-06-04. It contains stable identifiers, Chinese display names, the English and Japanese names exposed by the PRTS operator-page template, redirect aliases, skin/model labels, resource prefixes and version strings. It contains no image, texture, atlas or skeleton payload.

The current 449-operator snapshot has explicit English and Japanese fields for 401 entries and redirect aliases for 204 entries. Missing language fields remain empty and fall back to another available display name at runtime.

- Source site: `https://prts.wiki/`
- Operator-name source: the `干员页面名` template in each PRTS operator encyclopedia page, plus MediaWiki redirects
- Runtime asset host used by the resolver: `https://torappu.prts.wiki/`
- Refresh tool: `arknights_oni_mod_work/AmiyaDuplicantMod/tools/update_operator_appearance_catalog.py`

The snapshot is kept separate from original program code and is not covered by any license that may later be selected for that code. Arknights-related names and metadata remain subject to their respective rights and source-site terms. Refreshing the snapshot should preserve its source date and avoid silently overwriting the historical audit record.
