# Engine server API

Base URL: `http://127.0.0.1:7899` — runs while the app is open, or standalone via `python server.py`.

## GET /api/state

Everything the UI knows, in one call:

```json
{
  "engines": [{"id": "chatterbox", "name": "Chatterbox", "tier": 3, "clones": true,
               "desc": "...", "sec_per_word": 3.0}],
  "effects": ["None", "Deep Voice", "Radio", "Echo Chamber", "Robotic"],
  "voices": [{"name": "Woody", "file": "Woody.wav", "seconds": 9.4, "transcript": "..."}],
  "kokoro_presets": ["af_bella", "am_michael", "..."],
  "jobs": [{"id": "8d528525", "state": "running", "done": 4, "total": 12,
            "eta_seconds": 210, "status_text": "chunk 4/12", "result": null}],
  "outputs": [{"file": "intro 2026-07-05 183738.mp3", "mtime": 1783387058, "size_kb": 412}]
}
```

## POST /api/generate

```json
{
  "script": "Text to speak. [Woody] Speaker switching works here too.",
  "title": "my narration",
  "engine": "chatterbox | f5 | kokoro",
  "voice": "Woody",
  "kokoro_voice": "am_michael",
  "params": {"exaggeration": 0.5, "cfg": 0.5, "speed": 1.0, "nfe": 32},
  "effect": "None",
  "clean": true,
  "format": "mp3 | wav"
}
```

Returns `{"id": "<job id>"}` immediately — the job queues and renders in order.
Poll `/api/state` until the job's `state` is `done`, then grab `output\<result>`.

Job states: `queued → running → done | error | cancelled`.

## Other endpoints

| Method | Path | What |
|---|---|---|
| POST | `/api/job/{id}/cancel` | cancel a queued/running job |
| DELETE | `/api/job/{id}` | remove a finished job from the list |
| POST | `/api/voices` | multipart form: `file` (audio), `name`, `transcript` — clones a voice |
| DELETE | `/api/voices/{name}` | delete a cloned voice |
| DELETE | `/api/outputs/{file}` | delete a generated file |
| POST | `/api/open-folder` | open `output\` in Explorer |
| GET | `/files/output/{file}` | download a generated file |
| GET | `/files/voices/{file}` | download a voice sample |

## PowerShell example

```powershell
$job = Invoke-RestMethod http://127.0.0.1:7899/api/generate -Method Post -ContentType application/json -Body (@{
    script = Get-Content script.txt -Raw
    engine = "chatterbox"; voice = "Woody"; format = "mp3"; clean = $true
} | ConvertTo-Json)

do { Start-Sleep 5; $s = Invoke-RestMethod http://127.0.0.1:7899/api/state
     $j = $s.jobs | Where-Object id -eq $job.id } while ($j.state -in "queued","running")
"done -> output\$($j.result)"
```
