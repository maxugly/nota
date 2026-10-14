# Contract: project format v21 — `mixer`

`ProjectService.CurrentFormatVersion` 20 → **21**.

## Shape

```json
{
  "formatVersion": 21,
  "mixer": { "panLaw": "stereo", "panLawLegacy": false },
  ...
}
```

## Migration (append to `ProjectMigrations.Registered`)

```text
v20 -> v21: projects gain a pan law. Every earlier project mixed with the balance law,
so the step writes mixer = { panLaw: "balance", panLawLegacy: true } when `mixer` is
absent (keeps an existing object untouched).
```

## Capture / apply (`ProjectService`)

| Direction | Rule |
|---|---|
| Capture | `doc.Mixer = new MixerDto { PanLaw = engine.PanLaw == Stereo ? "stereo" : "balance", PanLawLegacy = transport.PanLawLegacy }` |
| Apply | after `engine.Reset()` and before tracks: `engine.SetPanLaw(parsed, checkpoint: false)`; `ProjectLoadResult.Transport.PanLawLegacy = doc.Mixer.PanLawLegacy` |
| Fingerprint | includes `mixer` (it is part of the manifest) → a law switch marks the project unsaved |

## Version history (`VersionDiff`)

A change of `mixer.panLaw` between two versions is listed under the *sound* changes as
"Pan law".

## Compatibility

- Older Nota builds refuse v21 files (existing rule: "older builds refuse newer files").
- Every pre-v21 file opens as Balance + legacy → renders bit-identical (SC-002).
