# Private Conversation Copilot

Single-user Windows 10/11 (.NET 8, WPF) meeting assistant. Captures the selected microphone and output endpoint without changing system defaults. No bot, accounts, database, or audio recording. The app sends live audio to the selected transcription provider and sends relevant transcript/context to enabled suggestion providers. Use only where you have the right to process the conversation.

See [RUN_ME.txt](RUN_ME.txt) for the Windows artifact and first launch. Builds are not installers; extract the entire artifact before running the executable.

## Development

On Windows with .NET 8 SDK:

```powershell
dotnet restore PrivateConversationCopilot.sln
dotnet build PrivateConversationCopilot.sln -c Release --no-restore
dotnet test tests/Copilot.Tests -c Release --no-build
dotnet test tests/Copilot.Windows.Tests -c Release --no-build
dotnet run --project src/Copilot.App
dotnet publish src/Copilot.App -c Release -r win-x64 --self-contained true -o publish
```

On Linux, the same build/publish commands can cross-compile (`EnableWindowsTargeting` is set), and `Copilot.Tests` runs. WPF execution and `Copilot.Windows.Tests` require Windows. Existing checkouts are already isolated; no Git worktree is needed.

## Workflow and controls

* Settings (gear): save keys; fields clear after saving. Test Connection checks authenticated provider access; this does not prove every model or subscription is enabled. Refresh OpenRouter's catalog, then choose A/B models. The catalog includes only routes advertising text output; DeepSeek entries appear if returned. None disables a box. Sampling controls are capability-driven. Choose a fast inexpensive model for A and a stronger reasoning model for B.
* Context / Notes is one autosaved working area. Separate paragraphs become retrieval chunks. Text present before a session is prepared context; paragraphs added/changed in a session are manual current-call notes. Unknown confidence and repeatability remain unknown. No embeddings service is used.
* Expand Meeting summary for four persistent static facts. State and an extractive rolling summary update asynchronously. Apparent objectives are left unknown unless supplied; no LLM summary is fabricated.
* In Clear mode select a real mic and the output endpoint used by the meeting app. Endpoint loopback captures **all** audio playing on that endpoint. Select an externally processed microphone for echo reduction; headphones are recommended. There is no custom AEC or process-specific loopback in this version.
* Infer mode uses one selected already-mixed microphone/virtual device. It cannot combine mic and output on its own. Select a final transcript line and use Correct selected speaker: UNKNOWN → ME → THEM. For diarized text, the mapping applies to the speaker's existing and future segments. Corrected history is used by subsequent requests.
* Turn detection combines RMS activity, local completeness heuristics and timeout. A transcription final alone does not commit. Help Me Now commits immediately, including available partials. Tune silence/fallback in Settings.
* A/B requests run independently. C uses a separate OpenAI Realtime session fed with THEM/mixed PCM. Only complete responses replace cards. Dismiss clears the current card; the box remains enabled. A model/configuration change cancels its request and invalidates late results.
* Transcript depth limits newest verbatim turns; each box also has a separate retrieval budget. Budget estimation uses UTF-8 bytes as a deliberately conservative upper bound rather than assuming English characters/token. Large newest turns retain a bounded suffix.
* Feed WAV file uses an existing mono/stereo WAV as THEM, paced in real time, through the actual Deepgram/transcript/turn/retrieval/suggestion pipeline. A Deepgram key is required; suggestion keys are optional. Stop live capture before running it. No fake provider output is shown in application mode.

## Local data and diagnostics

`%LOCALAPPDATA%\PrivateConversationCopilot` holds settings, context/summary, encrypted `.key` files and operational JSONL logs. API keys use Windows DPAPI CurrentUser. Plaintext meeting notes are local files, not encrypted by this app. Transcripts/audio are not retained to disk by default. Logs contain stages, IDs, error types/statuses and timings; exception bodies and HTTP authorization headers are not logged. Debug Content Logging explicitly enables sensitive prompt/transcript/response logging, with configured API keys redacted. Close Settings to persist its changes. Disabling content logging does not delete older logs; remove those locally if needed.

Tooltips on completed cards show rolling per-box P50/P95/P99 from the last RMS activity to the WPF render callback. This is a **render-frame/acoustic proxy**, not measured physical monitor paint or independently verified phoneme timing. Request first-data, stable completion, retrieval, STT, audio, turn and UI stages are operational events. False endpoints, errors and stale/cancelled requests are recorded. Hardware latency still needs measurement on representative calls. Content logs and local notes can contain sensitive material; never commit them.

Window-capture exclusion is attempted via WDA_EXCLUDEFROMCAPTURE and failure is harmless. It is OS/application dependent and **not a security guarantee**.

## Limitations and validation

See PROGRESS.txt for exact validation status. No API credentials are required to compile or run mocked core tests. Live STT, Realtime permissions, echo/device-loss behavior, square-monitor usability and full stop/start cycles need Windows/manual verification. STT reconnect is bounded to five failures with backoff, with a visible restart instruction after exhaustion. C can fail independently; restart a session after a C transport failure. A/B continue. AssemblyAI, DeepSeek Direct and process-specific capture are deferred. No alternate endpoints or arbitrary models are exposed.

Windows CI restores, builds, runs core and Windows smoke tests, publishes self-contained win-x64, and uploads the whole app directory. CI success must be checked in Actions; successful Linux cross-builds do not establish Windows execution.
