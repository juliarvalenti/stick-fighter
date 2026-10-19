# Announcer voice lines

Generated with **ElevenLabs text-to-speech** on Julia's account, on **2026-10-18**, in the voice
Julia picked: **Tyler – Arena Announcer & Event Host** (voice id `GyIXYY876myKNtA1j8NI`).

Every line used the same settings:

- model: the default (no `model_id` passed; documented as `eleven_multilingual_v2`);
- stability 0.35, similarity boost 0.75, style 0.5, speaker boost on, speed 1.0;
- output `mp3_44100_128`.

| file | text sent |
|---|---|
| `round-1.mp3` | `Round one!` |
| `round-2.mp3` | `Round two!` |
| `round-3.mp3` | `Round three!` |
| `final-round.mp3` | `Final round!` |
| `fight.mp3` | `FIGHT!` |
| `ko.mp3` | `K.O.!` |
| `time.mp3` | `Time!` |
| `perfect.mp3` | `Perfect!` |
| `double-ko.mp3` | `Double K.O.!` |
| `draw.mp3` | `Draw!` |
| `p1-wins.mp3` | `Player one wins!` |
| `p2-wins.mp3` | `Player two wins!` |

`raw/` holds the files exactly as generated. The ones the game plays, here, are made from them by
`tools/announcer.sh`: silence trimmed from both ends, and every line brought to the same loudness
(-16 LUFS, peaks limited under -1.5 dBFS). To change a line, or the voice, regenerate it with the
settings above into `raw/` and rerun the script.
