"""
FreeVoice - fully local tier-3 text-to-speech, powered by Chatterbox (Resemble AI).
Runs on CPU. Quality over speed. Free forever, commercial-safe (MIT model license).

Usage:
    python freevoice.py --text "Hello world." --out output/hello.wav
    python freevoice.py --file script.txt --out output/video.wav --voice voices/me.wav
    python freevoice.py --file script.txt --out output/video.mp3 --exaggeration 0.7

Voice cloning: drop a clean 7-20s WAV of the target voice in voices/ and pass it
with --voice. Omit --voice for Chatterbox's built-in default narrator.
"""
import argparse
import os
import re
import sys
import time
import wave
import contextlib

def log(msg):
    print(f"[FreeVoice] {msg}", flush=True)

def split_into_chunks(text, max_chars=280):
    """Chatterbox is happiest with sentence-sized chunks. Split on sentence
    boundaries, then pack sentences up to max_chars so we make as few (slow)
    generation calls as possible without starving prosody."""
    text = re.sub(r"\s+", " ", text.strip())
    # keep paragraph breaks as hard splits so pacing survives
    paragraphs = re.split(r"\n\s*\n", text) if "\n" in text else [text]
    sentences = []
    for para in paragraphs:
        parts = re.split(r"(?<=[.!?])\s+", para.strip())
        sentences.extend(p for p in parts if p)
        sentences.append("<PARA>")
    chunks, cur = [], ""
    for s in sentences:
        if s == "<PARA>":
            if cur:
                chunks.append(cur.strip())
                cur = ""
            continue
        if len(cur) + len(s) + 1 <= max_chars:
            cur = f"{cur} {s}".strip()
        else:
            if cur:
                chunks.append(cur.strip())
            # a single sentence longer than max_chars: hand it over whole
            cur = s
    if cur:
        chunks.append(cur.strip())
    return [c for c in chunks if c]

def main():
    ap = argparse.ArgumentParser(description="FreeVoice - local tier-3 TTS")
    src = ap.add_mutually_exclusive_group(required=True)
    src.add_argument("--text", help="text to speak")
    src.add_argument("--file", help="path to a .txt script")
    ap.add_argument("--out", required=True, help="output .wav or .mp3")
    ap.add_argument("--voice", help="reference voice .wav to clone (optional)")
    ap.add_argument("--exaggeration", type=float, default=0.5,
                    help="emotion intensity 0.25-1.0 (default 0.5; higher = more dramatic)")
    ap.add_argument("--cfg", type=float, default=0.5,
                    help="pacing/faithfulness 0.2-0.8 (lower = slower, more deliberate)")
    ap.add_argument("--max-chars", type=int, default=280, help="chunk size")
    ap.add_argument("--seed", type=int, default=0, help="fixed seed for reproducible takes")
    args = ap.parse_args()

    text = args.text if args.text else open(args.file, encoding="utf-8").read()
    if not text.strip():
        log("nothing to speak")
        sys.exit(1)

    if args.voice and not os.path.isfile(args.voice):
        log(f"voice file not found: {args.voice}")
        sys.exit(1)

    log("loading Chatterbox (first run downloads ~1 GB of weights)...")
    t0 = time.time()
    import torch
    import torchaudio as ta
    from chatterbox.tts import ChatterboxTTS

    if args.seed:
        torch.manual_seed(args.seed)

    torch.set_num_threads(os.cpu_count() or 4)
    model = ChatterboxTTS.from_pretrained(device="cpu")
    log(f"model ready in {time.time() - t0:.1f}s (sr={model.sr})")

    chunks = split_into_chunks(text, args.max_chars)
    total_words = len(text.split())
    log(f"{total_words} words -> {len(chunks)} chunks")

    gen_kwargs = dict(exaggeration=args.exaggeration, cfg_weight=args.cfg)
    if args.voice:
        gen_kwargs["audio_prompt_path"] = args.voice
        log(f"cloning voice from {args.voice}")

    os.makedirs(os.path.dirname(os.path.abspath(args.out)), exist_ok=True)
    wav_out = args.out if args.out.lower().endswith(".wav") else args.out + ".tmp.wav"

    pieces = []
    start = time.time()
    for i, chunk in enumerate(chunks, 1):
        c0 = time.time()
        wav = model.generate(chunk, **gen_kwargs)
        pieces.append(wav)
        # small silence between chunks so sentences breathe
        pieces.append(torch.zeros(1, int(model.sr * 0.18)))
        done = i / len(chunks)
        elapsed = time.time() - start
        eta = elapsed / done - elapsed
        log(f"chunk {i}/{len(chunks)} ({time.time()-c0:.1f}s) | {done*100:.0f}% | ETA {eta/60:.1f} min")

    audio = torch.cat(pieces, dim=-1)
    ta.save(wav_out, audio, model.sr)
    dur = audio.shape[-1] / model.sr
    log(f"wrote {wav_out} ({dur/60:.1f} min of audio, took {(time.time()-start)/60:.1f} min)")

    if not args.out.lower().endswith(".wav"):
        import subprocess
        log(f"encoding {args.out} ...")
        subprocess.run(["ffmpeg", "-y", "-i", wav_out, "-b:a", "192k", args.out],
                       check=True, capture_output=True)
        os.remove(wav_out)
        log(f"wrote {args.out}")

if __name__ == "__main__":
    main()
