# Engines

## Chatterbox — Tier 3, the default

[Resemble AI's open model](https://github.com/resemble-ai/chatterbox) (0.5B params, **MIT license — commercial use fine**). Preferred **65.3% vs ElevenLabs' 24.5%** in blind listening tests. Natural breaths and presence; clones voices from a short sample.

Tuning:
- **Emotion** (`exaggeration`, 0.25–1.0) — 0.3–0.4 calm documentary narrator, 0.5 neutral, 0.7+ dramatic.
- **Pace** (`cfg`, 0.2–0.8) — lower reads slower and more deliberately. 0.35–0.45 suits serious narration.
- Known quirk: faint background hiss — the **Clean audio** toggle removes it.

## F5-TTS — Tier 3, cloning specialist

[SWivid's research model](https://github.com/SWivid/F5-TTS). Extremely faithful voice cloning; **requires** a cloned voice (no default narrator). Weights are **CC-BY-NC — non-commercial license**.

Tuning:
- **Quality steps** (`nfe`, 16–64) — 32 is the sweet spot; 48–64 for final takes, 16 for quick previews.
- Give your voice sample a transcript (Voices tab) — noticeably better output.
- Windows note: this repo works around a DLL load-order crash in F5's imports (see `core.get_f5`).

## Kokoro — Tier 2, instant

[Kokoro-82M](https://github.com/thewh1teagle/kokoro-onnx) via ONNX (Apache 2.0). Near-realtime on any CPU. No cloning — 50+ built-in voices (`am_michael`, `af_heart`, `bm_george`…). Use it to draft pacing/wording before burning tier-3 render time.

## Voice cloning tips

- 7–20 seconds of **one person talking, no music, no room echo**.
- Match energy: clone a calm sample for calm narration — the clone inherits delivery.
- The sample is auto-converted to 24 kHz mono and trimmed to 30s max.

## Adding another engine

`core.py` is the only file that knows about engines: add an entry to `ENGINES`,
a loader (`get_x()`), and a branch in `synth_segment()`. The app and API pick it up automatically.
