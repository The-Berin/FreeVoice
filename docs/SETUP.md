# Setup

## Requirements

- Windows 10/11 x64
- [Python 3.12](https://www.python.org/downloads/) on PATH (`python --version` should work in a terminal)
- ~8 GB free disk (packages + models), 16 GB+ RAM recommended
- `ffmpeg` on PATH for MP3 export (WAV export needs nothing)
- .NET 8 SDK only if building from source — the release exe is self-contained

## Fresh machine, step by step

```powershell
# 1. Python packages (one-time, ~4 GB with torch)
pip install -r requirements.txt

# 2. Run the app — installer version or built from source
#    It starts the engine server itself and reports any problem it hits.
```

First use of each engine downloads its model once:

| Engine | Download | Where it lands |
|---|---|---|
| Chatterbox | ~1 GB | `%USERPROFILE%\.cache\huggingface` |
| F5-TTS | ~1.4 GB | `%USERPROFILE%\.cache\huggingface` |
| Kokoro | ~350 MB | `models\kokoro\` next to `server.py` |

## Folder layout

```
FreeVoice\
  server.py, core.py     engine server (Python)
  freevoice.py           one-shot CLI
  voices\                your cloned voice samples (.wav + optional .txt transcript)
  output\                everything you generate
  models\kokoro\         Kokoro model files
  src\FreeVoiceStudio\   the native app (C# / WinForms)
```

Your voices and outputs are plain files — back them up by copying the folders.

## Updating

```powershell
git pull
pip install -r requirements.txt --upgrade
dotnet build src\FreeVoiceStudio -c Release
```

## Troubleshooting

**"Couldn't start Python"** — Python isn't on PATH. Reinstall Python 3.12 with "Add python.exe to PATH" checked.

**"Engine server exited immediately"** — run `python server.py` in the FreeVoice folder yourself and read the error; it's almost always a missing package (`pip install -r requirements.txt`).

**F5-TTS crashes the server silently on some machines** — a DLL load-order conflict between its dependencies. FreeVoice already works around it (see `core.get_f5`); if you hit it in your own scripts, `import f5_tts.infer.utils_infer` *before* `from f5_tts.api import F5TTS`.

**MP3 export fails** — install ffmpeg (`winget install Gyan.FFmpeg`) or switch format to WAV.
