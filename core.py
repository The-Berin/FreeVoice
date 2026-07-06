"""FreeVoice core - engines, voice library, and the audio pipeline.
Shared by the studio server (server.py) and the CLI (freevoice.py)."""
import os
import re
import threading
import time

import numpy as np

BASE = os.path.dirname(os.path.abspath(__file__))
VOICES_DIR = os.path.join(BASE, "voices")
OUTPUT_DIR = os.path.join(BASE, "output")
KOKORO_DIR = os.path.join(BASE, "models", "kokoro")
os.makedirs(VOICES_DIR, exist_ok=True)
os.makedirs(OUTPUT_DIR, exist_ok=True)

_lock = threading.Lock()
_models = {}

ENGINES = [
    {"id": "chatterbox", "name": "Chatterbox", "tier": 3, "clones": True,
     "desc": "Beat ElevenLabs in blind tests. Breaths, presence, emotion control.",
     "sec_per_word": 3.0},
    {"id": "f5", "name": "F5-TTS", "tier": 3, "clones": True,
     "desc": "Cloning specialist. Needs a voice sample. Slowest, very faithful.",
     "sec_per_word": 4.0},
    {"id": "kokoro", "name": "Kokoro", "tier": 2, "clones": False,
     "desc": "Near-instant on CPU. 50+ preset voices. Great for drafts.",
     "sec_per_word": 0.15},
]

EFFECTS = ["None", "Deep Voice", "Radio", "Echo Chamber", "Robotic"]

# ------------------------------------------------------------------ engines

def get_chatterbox():
    with _lock:
        if "chatterbox" not in _models:
            import torch
            from chatterbox.tts import ChatterboxTTS
            torch.set_num_threads(os.cpu_count() or 4)
            _models["chatterbox"] = ChatterboxTTS.from_pretrained(device="cpu")
        return _models["chatterbox"]

def get_f5():
    with _lock:
        if "f5" not in _models:
            # utils_infer MUST load before f5_tts.api: api.py imports cached_path
            # first, whose native deps collide with torch's DLLs on this machine
            # (0xC0000005 access violation). Loading the torch stack first wins.
            import f5_tts.infer.utils_infer  # noqa: F401
            from f5_tts.api import F5TTS
            _models["f5"] = F5TTS(device="cpu")
        return _models["f5"]

KOKORO_FILES = {
    "kokoro-v1.0.onnx": "https://github.com/thewh1teagle/kokoro-onnx/releases/download/model-files-v1.0/kokoro-v1.0.onnx",
    "voices-v1.0.bin": "https://github.com/thewh1teagle/kokoro-onnx/releases/download/model-files-v1.0/voices-v1.0.bin",
}

def get_kokoro():
    with _lock:
        if "kokoro" not in _models:
            os.makedirs(KOKORO_DIR, exist_ok=True)
            for fname, url in KOKORO_FILES.items():
                path = os.path.join(KOKORO_DIR, fname)
                if not os.path.isfile(path):
                    print(f"[kokoro] downloading {fname}…", flush=True)
                    import urllib.request
                    urllib.request.urlretrieve(url, path + ".part")
                    os.replace(path + ".part", path)
            from kokoro_onnx import Kokoro
            _models["kokoro"] = Kokoro(
                os.path.join(KOKORO_DIR, "kokoro-v1.0.onnx"),
                os.path.join(KOKORO_DIR, "voices-v1.0.bin"))
        return _models["kokoro"]

def kokoro_presets():
    try:
        return sorted(get_kokoro().get_voices())
    except Exception:
        return ["am_michael", "af_heart", "af_bella", "am_adam", "bf_emma", "bm_george"]

# ------------------------------------------------------------------ voices

def list_voices():
    out = []
    for f in sorted(os.listdir(VOICES_DIR)):
        name, ext = os.path.splitext(f)
        if ext.lower() in (".wav", ".mp3", ".flac"):
            path = os.path.join(VOICES_DIR, f)
            try:
                import soundfile as sf
                info = sf.info(path)
                dur = info.frames / info.samplerate
            except Exception:
                dur = 0
            out.append({"name": name, "file": f, "seconds": round(dur, 1),
                        "transcript": voice_transcript(name)})
    return out

def voice_path(name):
    for ext in (".wav", ".mp3", ".flac"):
        p = os.path.join(VOICES_DIR, name + ext)
        if os.path.isfile(p):
            return p
    return None

def voice_transcript(name):
    p = os.path.join(VOICES_DIR, name + ".txt")
    return open(p, encoding="utf-8").read().strip() if os.path.isfile(p) else ""

def save_voice(tmp_audio_path, name, transcript=""):
    name = re.sub(r"[^\w\- ]", "", (name or "").strip())
    if not name:
        raise ValueError("Give the voice a name.")
    import librosa
    import soundfile as sf
    y, sr = librosa.load(tmp_audio_path, sr=24000, mono=True)
    dur = len(y) / sr
    if dur < 3:
        raise ValueError(f"Sample too short ({dur:.1f}s) - use 7-20s of clean speech.")
    if dur > 30:
        y = y[: 30 * sr]
    sf.write(os.path.join(VOICES_DIR, name + ".wav"), y, sr)
    if transcript.strip():
        with open(os.path.join(VOICES_DIR, name + ".txt"), "w", encoding="utf-8") as f:
            f.write(transcript.strip())
    return name

def delete_voice(name):
    for ext in (".wav", ".mp3", ".flac", ".txt"):
        p = os.path.join(VOICES_DIR, name + ext)
        if os.path.isfile(p):
            os.remove(p)

# ------------------------------------------------------------------ text

def split_chunks(text, max_chars=300):
    text = re.sub(r"[ \t]+", " ", text.strip())
    sentences = [s for s in re.split(r"(?<=[.!?])\s+", text) if s]
    chunks, cur = [], ""
    for s in sentences:
        if len(cur) + len(s) + 1 <= max_chars:
            cur = f"{cur} {s}".strip()
        else:
            if cur:
                chunks.append(cur)
            cur = s
    if cur:
        chunks.append(cur)
    return chunks or [text]

def parse_script(script, default_voice):
    segments = []
    for line in script.splitlines():
        line = line.strip()
        if not line:
            continue
        m = re.match(r"^\[([^\]]+)\]\s*(.*)$", line)
        if m and m.group(2):
            segments.append((m.group(1).strip(), m.group(2)))
        elif not m:
            if segments and segments[-1][0] == default_voice:
                segments[-1] = (default_voice, segments[-1][1] + " " + line)
            else:
                segments.append((default_voice, line))
    return segments

# ------------------------------------------------------------------ audio

def crossfade_concat(pieces, sr, fade_ms=50, gap_ms=140):
    if not pieces:
        return np.zeros(1, dtype=np.float32)
    fade = int(sr * fade_ms / 1000)
    gap = np.zeros(int(sr * gap_ms / 1000), dtype=np.float32)
    out = pieces[0]
    for p in pieces[1:]:
        out = np.concatenate([out, gap])
        if fade > 0 and len(out) > fade and len(p) > fade:
            ramp = np.linspace(0, 1, fade, dtype=np.float32)
            out[-fade:] = out[-fade:] * (1 - ramp) + p[:fade] * ramp
            out = np.concatenate([out, p[fade:]])
        else:
            out = np.concatenate([out, p])
    return out

def clean_audio(y, sr, strength=0.8):
    try:
        import noisereduce as nr
        y = nr.reduce_noise(y=y, sr=sr, stationary=True, prop_decrease=strength)
    except Exception as e:
        print("[clean] noisereduce skipped:", e)
    try:
        import pyloudnorm as pyln
        meter = pyln.Meter(sr)
        loud = meter.integrated_loudness(y.astype(np.float64))
        if np.isfinite(loud):
            y = pyln.normalize.loudness(y.astype(np.float64), loud, -16.0)
        peak = np.abs(y).max()
        if peak > 0.98:
            y = y * (0.98 / peak)
    except Exception as e:
        print("[clean] loudnorm skipped:", e)
    return y.astype(np.float32)

def apply_effect(y, sr, effect):
    if effect in (None, "", "None"):
        return y
    try:
        import pedalboard as pb
    except Exception:
        return y
    chains = {
        "Deep Voice": [pb.PitchShift(semitones=-3.0), pb.LowpassFilter(6000),
                       pb.Compressor(threshold_db=-18, ratio=3)],
        "Radio": [pb.HighpassFilter(300), pb.LowpassFilter(3500),
                  pb.Compressor(threshold_db=-20, ratio=4), pb.Gain(gain_db=3)],
        "Echo Chamber": [pb.Reverb(room_size=0.85, damping=0.3, wet_level=0.45, dry_level=0.55),
                         pb.Delay(delay_seconds=0.25, feedback=0.3, mix=0.25)],
        "Robotic": [pb.Chorus(rate_hz=0.2, depth=1.0, feedback=0.35, centre_delay_ms=7.0, mix=0.5)],
    }
    if effect not in chains:
        return y
    return pb.Pedalboard(chains[effect])(y, sr)

# ------------------------------------------------------------------ synthesis

def synth_segment(engine_id, text, voice_name, kokoro_voice, params):
    if engine_id == "chatterbox":
        model = get_chatterbox()
        kwargs = dict(exaggeration=params.get("exaggeration", 0.5),
                      cfg_weight=params.get("cfg", 0.5))
        ref = voice_path(voice_name) if voice_name else None
        if ref:
            kwargs["audio_prompt_path"] = ref
        wav = model.generate(text, **kwargs)
        return wav.squeeze(0).numpy().astype(np.float32), model.sr

    if engine_id == "f5":
        ref = voice_path(voice_name) if voice_name else None
        if not ref:
            raise RuntimeError("F5-TTS needs a cloned voice - add one in Voices.")
        model = get_f5()
        wav, sr, _ = model.infer(ref_file=ref, ref_text=voice_transcript(voice_name),
                                 gen_text=text, nfe_step=int(params.get("nfe", 32)),
                                 speed=params.get("speed", 1.0), remove_silence=False)
        return np.asarray(wav, dtype=np.float32), sr

    if engine_id == "kokoro":
        model = get_kokoro()
        samples, sr = model.create(text, voice=kokoro_voice or "am_michael",
                                   speed=params.get("speed", 1.0))
        return np.asarray(samples, dtype=np.float32), sr

    raise RuntimeError(f"unknown engine {engine_id}")

def generate(job):
    """job: dict with script/engine/voice/kokoro_voice/params/effect/clean/format/
    title. Mutates job with progress. Returns output file path."""
    import soundfile as sf
    from datetime import datetime

    script = job["script"]
    engine_id = job["engine"]
    voice = job.get("voice") or ""
    segments = parse_script(script, voice)
    work = []
    for seg_voice, seg_text in segments:
        for chunk in split_chunks(seg_text):
            work.append((seg_voice, chunk))
    job["total"] = len(work)
    job["done"] = 0

    t0 = time.time()
    pieces, sr = [], 24000
    for i, (seg_voice, chunk) in enumerate(work):
        if job.get("cancel"):
            raise RuntimeError("cancelled")
        job["status_text"] = f"chunk {i+1}/{len(work)}" + (f" — {seg_voice}" if seg_voice else "")
        y, sr = synth_segment(engine_id, chunk, seg_voice, job.get("kokoro_voice"), job.get("params", {}))
        pieces.append(y)
        job["done"] = i + 1
        if i + 1 < len(work):
            per = (time.time() - t0) / (i + 1)
            job["eta_seconds"] = int(per * (len(work) - i - 1))

    job["status_text"] = "mixing…"
    audio = crossfade_concat(pieces, sr)
    audio = apply_effect(audio, sr, job.get("effect"))
    if job.get("clean", True):
        audio = clean_audio(audio, sr)

    stamp = datetime.now().strftime("%Y-%m-%d %H%M%S")
    title = re.sub(r"[^\w\- ]", "", job.get("title") or voice or "narration").strip() or "narration"
    base = os.path.join(OUTPUT_DIR, f"{title} {stamp}")
    wav_path = base + ".wav"
    sf.write(wav_path, audio, sr)
    out_path = wav_path
    if job.get("format", "mp3") == "mp3":
        import subprocess
        out_path = base + ".mp3"
        subprocess.run(["ffmpeg", "-y", "-i", wav_path, "-b:a", "192k", out_path],
                       check=True, capture_output=True)
        os.remove(wav_path)
    job["seconds_audio"] = round(len(audio) / sr, 1)
    job["seconds_taken"] = round(time.time() - t0, 1)
    return out_path

def list_outputs():
    out = []
    for f in os.listdir(OUTPUT_DIR):
        if f.lower().endswith((".wav", ".mp3")):
            p = os.path.join(OUTPUT_DIR, f)
            out.append({"file": f, "mtime": os.path.getmtime(p),
                        "size_kb": os.path.getsize(p) // 1024})
    return sorted(out, key=lambda x: -x["mtime"])
