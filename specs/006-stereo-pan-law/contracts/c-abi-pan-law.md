# Contract: C ABI — project pan law

Added to `src/native/nota.engine/include/nota/nota_engine.h` (AR-7), implemented in
`nota_engine_core.cpp`.

```c
/* ---- Pan law (006) ------------------------------------------------------- */
/* How a track's or return's pan is applied to a stereo signal. Project-wide, part of
 * the graph snapshot (undo/redo restore it). BALANCE (the default, and every project
 * before format v21): the opposite channel is attenuated. STEREO: the whole image
 * moves — the far channel is folded into the near side, a mono fold at full pan.
 * Identical for mono material and at centre. Groups, clip pan envelopes and rack-chain
 * pans are not affected. */
#define NOTA_PAN_LAW_BALANCE 0
#define NOTA_PAN_LAW_STEREO  1

/* Set the law. checkpoint = 1: one undo step (user edit); 0: no undo entry (project
 * load / new-project baseline). A change while playing crossfades over 10 ms.
 * NOTA_ERR_INVALID_ARG for an unknown law; NOTA_OK (no-op, no undo step) if unchanged.
 * Message thread only. */
NOTA_API NotaResult nota_engine_set_pan_law(NotaEngine* engine, int32_t law, int32_t checkpoint);

/* Current law (authoring graph). NOTA_PAN_LAW_BALANCE for a null engine. */
NOTA_API int32_t    nota_engine_pan_law(const NotaEngine* engine);
```

## Behaviour guarantees (tested by the smoke test)

| Call | Expect |
|---|---|
| fresh engine / after `nota_engine_reset` | `nota_engine_pan_law == BALANCE` |
| `set_pan_law(STEREO, 1)` then `nota_engine_undo` | law back to `BALANCE`; `redo` → `STEREO` |
| `set_pan_law(STEREO, 0)` | `nota_engine_can_undo` unchanged |
| `set_pan_law(same, 1)` | no new undo step |
| `set_pan_law(7, 1)` | `NOTA_ERR_INVALID_ARG`, law unchanged |
| any structural edit after `set_pan_law(STEREO)` | law still `STEREO` |

## Managed mirror

`Nota.Application/Engine/IAudioEngine.cs`:

```csharp
/// <summary>Project pan law (graph-level, undoable). See nota_engine_set_pan_law.</summary>
PanLaw PanLaw { get; }
void SetPanLaw(PanLaw law, bool checkpoint = true);
```

`public enum PanLaw { Balance = 0, Stereo = 1 }` in `Nota.Application`.
P/Invoke in `Nota.Infrastructure/Interop/NativeMethods.cs`, wrapper in `NotaEngine.cs`.
