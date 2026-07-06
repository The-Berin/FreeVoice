# FreeVoice Studio

**Every tier of AI text-to-speech in one native Windows app. Fully local. Free forever.**

Voice cloning from a few seconds of audio, ElevenLabs-grade narration, instant drafts, multi-voice dialog scripts, batch queues, and studio effects — with nothing ever leaving your machine. No account, no subscription, no word limits, no cloud.

| Tier | Engine | Voice cloning | Quality | Speed |
|---|---|---|---|---|
| **3** | [Chatterbox](https://github.com/resemble-ai/chatterbox) | ✓ | Beat ElevenLabs 65%–24% in blind tests | ~3s per word |
| **3** | [F5-TTS](https://github.com/SWivid/F5-TTS) | ✓ | Research-grade cloning specialist | ~4s per word |
| **2** | [Kokoro](https://github.com/thewh1teagle/kokoro-onnx) | presets (50+) | Solid drafts | near-instant |

*Slow is fine — queue a video script, walk away, come back to studio narration that cost $0.*

## Install

**Option A — release installer (recommended)**
1. Install [Python 3.12](https://www.python.org/downloads/) (check "Add to PATH"), then:
   ```
   pip install -r https://raw.githubusercontent.com/The-Berin/FreeVoice/main/requirements.txt
   ```
2. Grab `FreeVoice-Studio-Setup.exe` from [Releases](https://github.com/The-Berin/FreeVoice/releases) and run it.
3. Launch FreeVoice Studio. First generation with each engine downloads its model automatically (Chatterbox ~1 GB, F5 ~1.4 GB, Kokoro ~350 MB).

**Option B — from source**
```powershell
git clone https://github.com/The-Berin/FreeVoice
cd FreeVoice
pip install -r requirements.txt
dotnet build src\FreeVoiceStudio -c Release
& "src\FreeVoiceStudio\bin\Release\net8.0-windows\FreeVoice Studio.exe"
```

The app supervises the Python engine server itself — start the app, everything else is automatic. `ffmpeg` on PATH is needed for MP3 export (WAV works without it).

## Using it

- **Studio** — paste a script, pick an engine + voice, hit Generate. Live word count and render-time estimate. Jobs queue up and run in order with progress and ETA; play results inline.
- **Multi-voice scripts** — start a line with `[VoiceName]` to switch speakers:
  ```
  [Woody] Somebody's poisoned the water hole!
  [Michael] Calm down, it's just a kokoro preset.
  ```
- **Voices** — clone anyone from **7–20 seconds of clean speech** (no music/noise). Add an optional transcript of the sample for better F5 accuracy. Cloned voices work with both Tier 3 engines.
- **Delivery controls** — Emotion & Pace (Chatterbox), Speed (Kokoro/F5), Quality steps (F5).
- **Effects** — Deep Voice, Radio, Echo Chamber, Robotic (ported from VoiceBox).
- **Clean audio** — spectral denoise (kills synthesis hiss) + loudness normalize to −16 LUFS (YouTube standard). On by default.
- **Library** — every generation, playable and deletable, stored in `output/`.

## Automation / API

The engine server listens on `http://127.0.0.1:7899` while the app runs (or run it standalone: `python server.py`).

```
POST /api/generate   {script, engine, voice, params{...}, effect, clean, format, title}
GET  /api/state      engines, voices, job queue with progress, outputs
POST /api/voices     multipart: file, name, transcript
```

Full endpoint reference: [docs/API.md](docs/API.md). There's also a one-shot CLI:

```powershell
python freevoice.py --file script.txt --out output\video.mp3 --voice voices\Woody.wav
```

## Docs

- [docs/SETUP.md](docs/SETUP.md) — detailed install, model storage, updating
- [docs/API.md](docs/API.md) — REST endpoints for pipelines
- [docs/ENGINES.md](docs/ENGINES.md) — engine details, licenses, tuning tips, troubleshooting

## Notes

- 100% offline after models download. Voice data never leaves the machine.
- F5-TTS weights are CC-BY-NC (non-commercial license) — know what that means for your use.
- Chatterbox is MIT. Kokoro is Apache 2.0.
- CPU-only by design; a CUDA GPU would speed things up but is not required.
