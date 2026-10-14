# Contract: C ABI — mix gestures

Added to `src/native/nota.engine/include/nota/nota_engine.h` (AR-7), next to the undo block.

```c
/* ---- Mix undo (009) ------------------------------------------------------ */
/* Volume / pan / master volume changes become undo steps through gestures. A gesture
 * records its targets' values at begin and, at end, pushes ONE undo step with the values
 * that changed (none changed → no step). If a structural checkpoint was pushed during the
 * gesture (automation recording) the step merges into it. The plain setters never record:
 * automation playback, loads and internal edits make no steps. Message thread only. */
#define NOTA_MIX_TRACK_VOLUME  0
#define NOTA_MIX_TRACK_PAN     1
#define NOTA_MIX_MASTER_VOLUME 2

typedef struct NotaMixTarget {
    int32_t kind;      /* NOTA_MIX_* */
    int32_t track_id;  /* ignored for NOTA_MIX_MASTER_VOLUME */
} NotaMixTarget;

/* Returns a handle > 0, or 0 if no target is valid. */
NOTA_API int32_t nota_mix_gesture_begin(NotaEngine* engine, const NotaMixTarget* targets, int32_t count);
/* Ends the gesture; unknown / already-ended handles are ignored. */
NOTA_API void    nota_mix_gesture_end(NotaEngine* engine, int32_t handle);

/* The master volume (linear), as set by nota_engine_set_master_volume or undo/redo. */
NOTA_API float   nota_engine_master_volume(const NotaEngine* engine);
```

`nota_engine_undo` / `nota_engine_redo` keep their signatures; their comment changes from
"atomic params … are not covered" to "volume, pan and master volume steps from mix gestures
are covered; mute, solo, sends, device params and the transport are not".

## Guarantees (smoke test)

| Sequence | Expect |
|---|---|
| begin(vol t) · set ×50 · end · undo | volume exactly the begin value; redo → exactly the last value |
| begin · end (no set) | `can_undo` unchanged |
| begin · set back to the start value · end | no step |
| add track · gesture vol · set pan law · gesture pan · undo ×4 | reverts in exactly reverse order; redo ×4 forward |
| automation lane in read mode, render 2 s | no step |
| record on · begin · begin_automation_write · set ×n · end_automation_write · end · undo | lane edit and value both reverted by one undo |
| begin(vol t) · set · remove t · end | target dropped, no mix step (the removal is the only new step) |
| gesture vol t · remove t · undo · undo | first undo restores t, second restores its volume (order keeps the track alive) |
| two gestures open at once on different tracks | two steps, in end order |
| begin(master) · set_master_volume · end · undo | `nota_engine_master_volume` = begin value |
| reset with a gesture open · end(handle) | no-op, no step |
