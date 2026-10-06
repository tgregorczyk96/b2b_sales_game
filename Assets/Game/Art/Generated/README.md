# Generated Art (Black Forest Labs / FLUX)

All AI-generated images live here — never mixed with hand-made art in `Art/Characters` or `Art/Backgrounds`.

## Layout

```
Generated/
  Characters/   character sprites, expression/mouth variants
  Backgrounds/  scene backgrounds
```

## Naming

`<subject>_<variant>_v<NN>.png`, e.g. `customer-cfo_idle_v01.png`, `office-meeting-room_day_v02.png`.
Never overwrite a version — create `v<NN+1>` instead.

## Provenance sidecar (required)

Every image gets a JSON file with the same base name (`customer-cfo_idle_v01.json`):

```json
{
  "provider": "Black Forest Labs",
  "model": "flux-...",
  "prompt": "...",
  "seed": 0,
  "width": 1024,
  "height": 1024,
  "generatedAt": "YYYY-MM-DD",
  "editedAfterGeneration": false,
  "notes": ""
}
```

An image is promoted out of `Generated/` only after manual review/editing (then `editedAfterGeneration: true`).
