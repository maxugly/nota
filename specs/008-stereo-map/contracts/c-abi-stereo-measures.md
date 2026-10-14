# Contract: C ABI — stereo measurements

Added to `src/native/nota.engine/include/nota/nota_engine.h` (AR-7), implemented in
`nota_engine_core.cpp` (or `nota_engine_track.cpp`, next to the meter getters).

```c
/* ---- Stereo measurements (008) ------------------------------------------- */
/* What each track, return and the master sends to the master, post-fader and post-pan,
 * averaged over ~300 ms: level, where it sits (balance on the pan scale: a mono source
 * panned to p measures p), how wide it is (1 - correlation, clamped 0..1) and the
 * correlation. Groups are not measured. Message thread; lock-free reads. */
typedef struct NotaStereoMeasure {
    int32_t track_id;     /* a track / return id, or nota_engine_master_track_id() */
    float   level_db;     /* RMS of the pair, dBFS; -200 for silence */
    float   balance;      /* -1 (left) .. +1 (right) */
    float   width;        /* 0 (mono) .. 1 (unrelated or anti-phase) */
    float   correlation;  /* -1 .. +1; 0 when either side is silent */
} NotaStereoMeasure;

/* Fills up to cap rows (tracks and returns in track order, the master last) and returns
 * the number available (call again with a bigger buffer if it exceeds cap). */
NOTA_API int32_t nota_engine_stereo_measures(const NotaEngine* engine, NotaStereoMeasure* out, int32_t cap);
```

## Guarantees (smoke test, WAV material at 48 kHz, Stereo pan law, after ≥ 1.5 s of play)

| Material | Expect |
|---|---|
| mono 440 Hz, pan L50 | balance −0.50 ± 0.05, width < 0.05, correlation > 0.95 |
| L 440 Hz / R 1 kHz (unrelated), pan C | balance 0 ± 0.05, width > 0.95, \|correlation\| < 0.05 |
| L = sine, R = −sine | correlation < −0.95, width = 1 |
| silence | level_db < −60 |
| the unrelated pair as the only track | the master row matches the track row (± 0.05) |
| muted track | its level drops ≥ 20 dB within 1.5 s (τ = 0.3 s; the map hides a muted cloud at once from the mute flag) |
| unmuted again | within 3 dB of the steady level after 0.5 s (SC-005) |
