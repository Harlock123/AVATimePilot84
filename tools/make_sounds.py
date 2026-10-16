#!/usr/bin/env python3
"""Synthesises AVATimePilot84's sound effects and tunes into ../src/AVATimePilot84/Sounds as 16-bit mono WAV.

Nothing is sampled from any arcade machine: every effect is built here from square, triangle and
noise voices, and the tunes are new compositions. Run it from anywhere:
    python3 tools/make_sounds.py
"""
import math
import os
import random
import struct
import wave

RATE = 22050
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "src", "AVATimePilot84", "Sounds")
RNG = random.Random(1984)


def write(name, samples, volume=0.7):
    peak = max(1e-9, max(abs(s) for s in samples))
    with wave.open(os.path.join(OUT, name), "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(RATE)
        w.writeframes(b"".join(struct.pack("<h", int(s / peak * volume * 32767)) for s in samples))


def voice(kind, phase):
    p = phase % 1.0
    if kind == "square":
        return 1.0 if p < 0.5 else -1.0
    if kind == "pulse":
        return 1.0 if p < 0.25 else -1.0
    if kind == "tri":
        return 4 * p - 1 if p < 0.5 else 3 - 4 * p
    if kind == "saw":
        return 2 * p - 1
    if kind == "sine":
        return math.sin(2 * math.pi * p)
    return RNG.uniform(-1, 1)


def tone(seconds, f0, f1=None, kind="square", attack=0.004, release=1.0, vibrato=0.0, vib_rate=8.0, gain=1.0):
    """A note gliding from f0 to f1 (exponentially), with a short attack and a shaped decay."""
    n = int(RATE * seconds)
    f1 = f1 or f0
    out, phase, held = [], 0.0, 0.0
    for i in range(n):
        t = i / RATE
        f = f0 * (f1 / f0) ** (i / max(1, n - 1))
        if vibrato:
            f *= 1 + vibrato * math.sin(2 * math.pi * vib_rate * t)
        phase += f / RATE
        if kind == "noise":
            # Noise re-sampled at the voice's rate: a lower 'pitch' sounds rougher.
            if int(phase) != int(phase - f / RATE):
                held = RNG.uniform(-1, 1)
            s = held
        else:
            s = voice(kind, phase)
        env = min(1.0, t / attack) * (1 - i / n) ** release
        out.append(s * env * gain)
    return out


def silence(seconds):
    return [0.0] * int(RATE * seconds)


def cat(*parts):
    out = []
    for p in parts:
        out.extend(p)
    return out


def mix(*parts):
    n = max(len(p) for p in parts)
    out = [0.0] * n
    for p in parts:
        for i, v in enumerate(p):
            out[i] += v
    return out


NOTES = {"C": 0, "D": 2, "E": 4, "F": 5, "G": 7, "A": 9, "B": 11}


def hz(note):
    n = NOTES[note[0]]
    rest = note[1:]
    while rest and rest[0] in "#b":
        n += 1 if rest[0] == "#" else -1
        rest = rest[1:]
    return 440.0 * 2 ** ((12 * (int(rest) + 1) + n - 69) / 12)


def tune(bpm, lead, bass, kind="pulse"):
    """Two voices: a lead and a bass line, as (note, beats) lists; '-' is a rest."""
    beat = 60.0 / bpm

    def line(notes, k, gain, release):
        out = []
        for note, beats in notes:
            d = beats * beat
            out.extend(silence(d) if note == "-" else tone(d, hz(note), kind=k, release=release, gain=gain))
        return out

    return mix(line(lead, kind, 0.55, 0.6), line(bass, "tri", 0.6, 0.3))


def seq(bpm, notes, kind="pulse", gain=0.55, release=0.6):
    beat = 60.0 / bpm
    out = []
    for note, beats in notes:
        d = beats * beat
        out.extend(silence(d) if note == "-" else tone(d, hz(note), kind=kind, release=release, gain=gain))
    return out




def drums(bpm, bars):
    """A marching snare: a roll on the first beat of each bar, single taps on the rest, and a bass drum under it."""
    beat = 60.0 / bpm
    out = []
    for _ in range(bars):
        for b in range(4):
            if b == 0:
                hit = cat(*[tone(beat / 4, 1800, 900, "noise", attack=0.001, release=1.5, gain=0.5) for _ in range(4)])
            else:
                hit = cat(tone(beat / 2, 2000, 700, "noise", attack=0.001, release=2.5, gain=0.6), silence(beat / 2))
            kick = cat(tone(beat / 2, 110, 45, "sine", attack=0.002, release=1.5, gain=0.9), silence(beat / 2)) if b % 2 == 0 else silence(beat)
            out.extend(mix(hit, kick)[: int(RATE * beat)])
    return out


def engine(bpm, bars):
    """The jet's engine: a low steady roar with a slow swell each bar."""
    beat = 60.0 / bpm
    out = []
    for _ in range(bars):
        out.extend(mix(tone(beat * 4, 160, 150, "noise", attack=0.3, release=0.2, gain=0.25), tone(beat * 4, 55, 55, "saw", attack=0.3, release=0.2, gain=0.18))[: int(RATE * beat * 4)])
    return out


def main():
    os.makedirs(OUT, exist_ok=True)
    for f in os.listdir(OUT):
        if f.endswith(".wav"):
            os.remove(os.path.join(OUT, f))
    # The jet's cannon: a fast hard tick. A missile away: a whoosh rising. Locked on: two short high beeps.
    write("Fire.wav", mix(tone(0.06, 3200, 1200, "noise", attack=0.001, release=1.6), tone(0.04, 520, 200, "square", release=1, gain=0.4)), 0.2)
    write("Launch.wav", mix(tone(0.5, 300, 2400, "noise", attack=0.02, release=0.6), tone(0.4, 180, 600, "saw", release=0.8, gain=0.25)), 0.26)
    write("Lock.wav", cat(tone(0.05, 1760, kind="square", release=0.2), silence(0.03), tone(0.05, 1760, kind="square", release=0.2)), 0.16)
    write("Hit.wav", mix(tone(0.08, 1200, 500, "square", release=1.2, gain=0.4), tone(0.06, 2000, 800, "noise", release=1.5, gain=0.5)), 0.24)
    write("Boom.wav", mix(tone(0.45, 1100, 60, "noise", attack=0.002, release=0.5), tone(0.35, 140, 50, "sine", release=0.5, gain=0.8)), 0.38)
    write("BigBoom.wav", cat(mix(tone(1.3, 800, 30, "noise", release=0.3), tone(1.2, 90, 25, "saw", release=0.3, gain=0.6)), tone(0.6, 300, 60, "noise", release=0.7, gain=0.5)), 0.55)
    # The enemy: a duller gun, a rocket's hiss, a formation's rising wail.
    write("EnemyFire.wav", mix(tone(0.08, 1400, 500, "noise", attack=0.001, release=1.2), tone(0.06, 300, 150, "square", release=1, gain=0.35)), 0.2)
    write("Rocket.wav", tone(0.6, 2600, 900, "noise", attack=0.02, release=0.5), 0.2)
    write("Formation.wav", tone(0.9, 300, 900, "saw", attack=0.2, vibrato=0.03, vib_rate=6, release=0.6, gain=0.5), 0.22)
    # The carrier: an alarm, three times. A turret gone: a crash and a falling tone.
    write("CarrierAlarm.wav", cat(*[cat(tone(0.25, 700, 1100, "square", release=0.2), tone(0.25, 1100, 700, "square", release=0.2)) for _ in range(3)]), 0.3)
    write("TurretDown.wav", cat(mix(tone(0.5, 900, 50, "noise", release=0.4), tone(0.4, 120, 40, "sine", release=0.5)), tone(0.3, hz("G5"), hz("C5"), "pulse", release=0.5, gain=0.5)), 0.44)
    write("PlayerDown.wav", cat(tone(0.7, 1400, 150, "saw", vibrato=0.06, vib_rate=12, release=0.3, gain=0.6), mix(tone(0.8, 900, 40, "noise", release=0.35), tone(0.7, 110, 30, "sine", release=0.4))), 0.5)
    write("LevelStart.wav", cat(*[tone(d, hz(n), kind="pulse", release=0.25) for n, d in [("D5", 0.1), ("A5", 0.1), ("D6", 0.3)]]), 0.3)
    write("LevelClear.wav", tune(150, [("D5", 0.5), ("F#5", 0.5), ("A5", 0.5), ("D6", 1), ("C#6", 0.5), ("D6", 2)], [("D3", 1), ("A2", 1), ("D3", 1), ("A2", 0.5), ("D3", 1.5)], kind="pulse"), 0.42)
    write("ExtraLife.wav", cat(*[tone(0.07, hz(n), kind="pulse", release=0.3) for n in ["D6", "A6", "D6", "A6", "D7"]]), 0.36)

    # The dogfight: a driving line over the engine's roar (an original composition), a whole number of beats
    # long so it loops cleanly.
    lead = [("D5", 0.5), ("D5", 0.5), ("F5", 0.5), ("G5", 0.5), ("A5", 1), ("C6", 0.5), ("A5", 0.5),
            ("G5", 0.5), ("F5", 0.5), ("G5", 0.5), ("A5", 0.5), ("D5", 2),
            ("D5", 0.5), ("D5", 0.5), ("F5", 0.5), ("G5", 0.5), ("A5", 1), ("D6", 0.5), ("C6", 0.5),
            ("A5", 0.5), ("G5", 0.5), ("F5", 0.5), ("E5", 0.5), ("D5", 2)]
    bass = [("D3", 0.5), ("D3", 0.5)] * 4 + [("C3", 0.5), ("C3", 0.5)] * 2 + [("D3", 1), ("A2", 1)] \
        + [("D3", 0.5), ("D3", 0.5)] * 4 + [("A#2", 0.5), ("A#2", 0.5)] * 2 + [("A2", 1), ("D3", 1)]
    melody = tune(132, lead, bass, kind="pulse")
    write("Music.wav", mix([v * 0.55 for v in melody], engine(132, 4)), 0.3)

    # The title: the same line, a soaring second strain, and drums.
    title_lead = lead + [("A5", 1), ("D6", 1), ("C6", 0.5), ("A5", 0.5), ("G5", 1), ("F5", 1), ("G5", 1), ("A5", 2),
                         ("F5", 1), ("G5", 1), ("A5", 0.5), ("C6", 0.5), ("D6", 1), ("C6", 0.5), ("A5", 0.5), ("G5", 1), ("D5", 2)]
    title_bass = bass + [("F2", 1), ("C3", 1), ("F2", 1), ("C3", 1), ("G2", 1), ("D3", 1), ("A2", 1), ("A2", 1),
                         ("A#2", 1), ("F2", 1), ("C3", 1), ("G2", 1), ("A2", 1), ("A2", 1), ("D3", 2)]
    write("TitleTune.wav", cat(mix(tune(132, title_lead, title_bass, kind="pulse"), drums(132, 8)), silence(1.0)), 0.4)
    write("StartTune.wav", tune(150, [("A4", 0.5), ("D5", 0.5), ("F5", 0.5), ("A5", 1), ("F5", 0.5), ("D6", 1)], [("D3", 1), ("A2", 1), ("D3", 2)], kind="pulse"), 0.42)
    write("GameOver.wav", tune(66, [("A5", 1), ("F5", 1), ("E5", 1), ("C#5", 1), ("D5", 4)], [("D3", 2), ("A#2", 2), ("A2", 2), ("D2", 2)], kind="tri"), 0.42)


if __name__ == "__main__":
    main()
