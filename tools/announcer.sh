#!/usr/bin/env bash
# Prepares the announcer's voice lines for the game: from the files as generated
# (assets/audio/announcer/raw/*.mp3) to trimmed, loudness-matched mono MP3
# (assets/audio/announcer/*.mp3), which is what the game loads (Godot imports MP3 as is).
#
#   tools/announcer.sh
#
# - Silence is trimmed from both ends (below -45 dB), keeping a few milliseconds so nothing clicks.
# - Every line is brought to the same loudness (EBU R128, -16 LUFS) by one gain, and a fast
#   limiter holds its peaks under -1.5 dBFS: arcade announcers are compressed and up front, and
#   without it the peakier lines couldn't reach the others' loudness. Loudness is measured with a
#   second of silence after the line, since R128 needs 400 ms and "Draw" is shorter than that.
#
# Rerun it after regenerating any line (see assets/audio/announcer/SOURCE.md). Needs ffmpeg.
set -euo pipefail

root="$(git rev-parse --show-toplevel)/assets/audio/announcer"
target_i=-16
ceiling_tp=-1.5
trim="silenceremove=start_periods=1:start_threshold=-45dB:start_silence=0.01,areverse,silenceremove=start_periods=1:start_threshold=-45dB:start_silence=0.03,areverse"

for raw in "$root"/raw/*.mp3; do
  name="$(basename "$raw" .mp3)"
  summary=$(ffmpeg -hide_banner -nostats -i "$raw" -af "$trim,apad=pad_dur=1,ebur128=peak=true" -f null - 2>&1 | sed -n '/Summary:/,$p')
  loudness=$(echo "$summary" | awk '/^ *I:/ { print $2 }')
  peak=$(echo "$summary" | awk '/^ *Peak:/ { print $2 }')
  gain=$(awk -v i="$loudness" -v t="$target_i" 'BEGIN { printf "%.2f", t - i }')
  limit=$(awk -v c="$ceiling_tp" 'BEGIN { printf "%.3f", 10 ^ (c / 20) }')
  ffmpeg -hide_banner -loglevel error -y -i "$raw" \
    -af "$trim,aresample=44100,volume=${gain}dB,alimiter=limit=$limit:attack=1:release=40:level=disabled" \
    -ac 1 -c:a libmp3lame -q:a 2 "$root/$name.mp3"
  echo "$name: $loudness LUFS, peak $peak dBTP, gain $gain dB"
done
