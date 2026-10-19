# 2026-10-20 startup and terminal performance comparison

## Environment

- Linux host, Xvfb `:99` at 1280x800, 100% scaling, warm page cache.
- .NET SDK `10.0.402`; Avalonia `12.1.4`; Iciclecreek Terminal `4.1.0`.
- Python `3.8.10` from the repository `venv`, PyQt5 and pyte.
- Python was started from source; Avalonia was measured as a published
  single-file executable. Both used separate temporary `HOME` directories.
- SSH tests used the local OpenSSH test server at `127.0.0.1:2222`.

These are Linux/Xvfb measurements. They isolate application/framework costs,
but Windows cold-start and antivirus scanning still need verification on the
target desktop.

## Startup to first main window

Five warm runs per build, median reported.

| Build | Startup | Notes |
| --- | ---: | --- |
| Python source | 0.56-0.60 s | PyQt5 imports and main window |
| Avalonia, previous release (single-file + compression) | 1.54 s | current Beta package before this change |
| Avalonia, `dotnet` DLL | 1.32 s | no single-file extraction |
| Avalonia, R2R + compression | 1.12 s | smaller on-disk executable |
| Avalonia, R2R + no compression | **0.69-0.70 s** | new release packaging |
| Avalonia, R2R folder publish | 0.87 s | no single-file; not used for the release |
| Avalonia, Native AOT (Linux experiment) | **0.32 s** | experimental; configuration loading failed |

The current release uses ReadyToRun with single-file compression disabled.
That removes most JIT work and avoids repeatedly decompressing the native
payload. The zip grows from about 50 MB to about 72 MB; the executable grows
to about 176 MB on disk.

## Terminal parsing

Both implementations parsed the same repeated ASCII line stream. Avalonia used
`TerminalEmulator` at 120x40 with 10,000 scrollback lines. Python used
`FastHistoryScreen` at 120x40 with 10,000 history lines.

| Payload | Avalonia | Python/pyte | Relative |
| --- | ---: | ---: | ---: |
| 1 MB | 103-126 ms | 550-662 ms | about 5.7x faster |
| 10 MB | 226-248 ms | 4.82-5.40 s | about 21x faster |

## SSH output end-to-end

A local SSH shell streamed 10 MB of text and then printed an ANSI magenta
marker. Two separate Xvfb sessions were used; the Avalonia viewport was
1000x650 and the Python viewport was 850x500. The marker was detected from
screenshots, so the values include a small polling/rendering detection
overhead.

| Run | Avalonia | Python |
| --- | ---: | ---: |
| 1 | 1.238 s | 7.859 s |
| 2 | 0.999 s | 8.138 s |
| 3 | 1.058 s | 7.551 s |
| 4 | 1.016 s | 8.800 s |
| 5 | 1.105 s | 9.260 s |

Avalonia completed the 10 MB stream about 7-8x faster. For small outputs the
difference is not perceptible; the previous release's slow startup dominated
the first impression.

## Memory observation

- Idle, no tabs: Python about 170 MB RSS; Avalonia R2R package about 182 MB.
- After the 10 MB SSH runs, the Python process was about 214 MB and the
  current Avalonia process about 292 MB in that particular test. The terminal
  scrollback and render caches are the likely contributors. This needs a
  separate investigation before treating memory as an Avalonia advantage.

## Native AOT experiment

A Linux `PublishAot=true` build was also produced to evaluate the ceiling:

- output: 43 MB directory (29 MB native executable plus Skia/HarfBuzz/PTY/
  serial native libraries);
- startup: 0.325 s;
- idle RSS: about 116 MB.

It is not a release candidate yet:

- the AOT binary started, but did not load the existing `config.json`
  (a 1000x650 window and its SSH session were ignored); `System.Text.Json`
  reflection metadata had been trimmed;
- configuration/session JSON needs source-generated serializers;
- the terminal and theme resources still emit AOT/trim warnings and need
  runtime coverage;
- Native AOT cannot be cross-compiled from Linux to `win-x64`; the Windows
  build needs MSVC Build Tools plus the Windows SDK or a Windows CI runner;
- the output is a folder rather than a single executable, so the installer
  and desktop shortcut flow would need adjustment.

The R2R build is therefore the release baseline; Native AOT remains a
follow-up experiment, not part of the Beta package.
