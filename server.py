"""FreeVoice Studio server - FastAPI backend + hand-built frontend in web/.
Run: python server.py   (serves http://127.0.0.1:7899)"""
import os
import shutil
import subprocess
import tempfile
import threading
import time
import traceback
import uuid

from fastapi import FastAPI, File, Form, UploadFile
from fastapi.responses import FileResponse, JSONResponse
from fastapi.staticfiles import StaticFiles
import uvicorn

import core

app = FastAPI(title="FreeVoice")
WEB = os.path.join(core.BASE, "web")

jobs = {}          # id -> job dict
job_order = []     # newest first
_queue = []
_queue_lock = threading.Condition()


def worker():
    while True:
        with _queue_lock:
            while not _queue:
                _queue_lock.wait()
            job_id = _queue.pop(0)
        job = jobs.get(job_id)
        if not job or job.get("cancel"):
            if job:
                job["state"] = "cancelled"
            continue
        job["state"] = "running"
        job["status_text"] = "loading model…"
        try:
            out = core.generate(job)
            job["result"] = os.path.basename(out)
            job["state"] = "done"
            job["status_text"] = f"{job['seconds_audio']/60:.1f} min of audio in {job['seconds_taken']/60:.1f} min"
        except Exception as e:
            traceback.print_exc()
            job["state"] = "cancelled" if str(e) == "cancelled" else "error"
            job["status_text"] = str(e)[:300]


threading.Thread(target=worker, daemon=True, name="FreeVoiceWorker").start()


@app.get("/api/state")
def state():
    return {
        "engines": core.ENGINES,
        "effects": core.EFFECTS,
        "voices": core.list_voices(),
        "kokoro_presets": core.kokoro_presets(),
        "jobs": [jobs[i] for i in job_order],
        "outputs": core.list_outputs()[:60],
    }


@app.post("/api/generate")
async def generate(payload: dict):
    script = (payload.get("script") or "").strip()
    if not script:
        return JSONResponse({"error": "Script is empty."}, status_code=400)
    job = {
        "id": uuid.uuid4().hex[:8],
        "title": payload.get("title") or "",
        "script": script,
        "engine": payload.get("engine", "chatterbox"),
        "voice": payload.get("voice") or "",
        "kokoro_voice": payload.get("kokoro_voice") or "am_michael",
        "params": payload.get("params") or {},
        "effect": payload.get("effect") or "None",
        "clean": bool(payload.get("clean", True)),
        "format": payload.get("format", "mp3"),
        "state": "queued",
        "status_text": "queued",
        "done": 0, "total": 0, "eta_seconds": None,
        "created": time.time(),
        "words": len(script.split()),
    }
    jobs[job["id"]] = job
    job_order.insert(0, job["id"])
    with _queue_lock:
        _queue.append(job["id"])
        _queue_lock.notify()
    return {"id": job["id"]}


@app.post("/api/job/{job_id}/cancel")
def cancel(job_id: str):
    if job_id in jobs:
        jobs[job_id]["cancel"] = True
        if jobs[job_id]["state"] == "queued":
            jobs[job_id]["state"] = "cancelled"
    return {"ok": True}


@app.delete("/api/job/{job_id}")
def remove_job(job_id: str):
    if job_id in jobs and jobs[job_id]["state"] in ("done", "error", "cancelled"):
        del jobs[job_id]
        job_order.remove(job_id)
    return {"ok": True}


@app.post("/api/voices")
async def add_voice(file: UploadFile = File(...), name: str = Form(...), transcript: str = Form("")):
    suffix = os.path.splitext(file.filename or "sample.wav")[1] or ".wav"
    with tempfile.NamedTemporaryFile(delete=False, suffix=suffix) as tmp:
        shutil.copyfileobj(file.file, tmp)
        tmp_path = tmp.name
    try:
        saved = core.save_voice(tmp_path, name, transcript)
        return {"ok": True, "name": saved}
    except Exception as e:
        return JSONResponse({"error": str(e)}, status_code=400)
    finally:
        os.unlink(tmp_path)


@app.delete("/api/voices/{name}")
def remove_voice(name: str):
    core.delete_voice(name)
    return {"ok": True}


@app.delete("/api/outputs/{filename}")
def remove_output(filename: str):
    p = os.path.join(core.OUTPUT_DIR, os.path.basename(filename))
    if os.path.isfile(p):
        os.remove(p)
    return {"ok": True}


@app.post("/api/open-folder")
def open_folder():
    subprocess.Popen(["explorer", core.OUTPUT_DIR])
    return {"ok": True}


@app.get("/")
def index():
    return FileResponse(os.path.join(WEB, "index.html"))


app.mount("/files/output", StaticFiles(directory=core.OUTPUT_DIR), name="output")
app.mount("/files/voices", StaticFiles(directory=core.VOICES_DIR), name="voices")

if __name__ == "__main__":
    uvicorn.run(app, host="127.0.0.1", port=7899, log_level="warning")
