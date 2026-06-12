using UnityEngine;

// Procedural SFX factory. The project ships almost no monster/ambient sounds,
// so everything horror-related is synthesised once and cached, same idea as
// ProceduralJumpscareSting in AntonchikFenceEncounter.
public static class HorrorAudio
{
    private const int SampleRate = 44100;

    private static AudioClip radioStatic;
    private static AudioClip pop;
    private static AudioClip chitter;
    private static AudioClip scream;
    private static AudioClip squelch;
    private static AudioClip engineLoop;
    private static AudioClip gateCreak;
    private static AudioClip keyJingle;
    private static AudioClip objectiveBlip;
    private static AudioClip missionSting;
    private static AudioClip ambientDrone;

    public static AudioClip RadioStatic()
    {
        if (radioStatic != null)
        {
            return radioStatic;
        }

        radioStatic = Generate("RadioStatic", 2.4f, (t, rng) =>
        {
            float noise = ((float)rng.NextDouble() * 2f - 1f);
            float crackleGate = (float)rng.NextDouble() < 0.0006f ? 3.5f : 1f;
            float hum = Mathf.Sin(2f * Mathf.PI * 50f * t) * 0.06f;
            return noise * 0.22f * crackleGate + hum;
        });
        return radioStatic;
    }

    public static AudioClip Pop()
    {
        if (pop != null)
        {
            return pop;
        }

        pop = Generate("RadioPop", 0.35f, (t, rng) =>
        {
            float env = Mathf.Exp(-t * 28f);
            float noise = ((float)rng.NextDouble() * 2f - 1f);
            float thump = Mathf.Sin(2f * Mathf.PI * 70f * t) * 0.8f;
            return (noise * 0.7f + thump) * env;
        });
        return pop;
    }

    // Scalapendra: bursts of dry, fast clicks with a faint hiss underneath.
    public static AudioClip Chitter()
    {
        if (chitter != null)
        {
            return chitter;
        }

        chitter = Generate("ScalapendraChitter", 3.2f, (t, rng) =>
        {
            float burstPhase = Mathf.Repeat(t, 0.8f);
            float burstEnv = burstPhase < 0.35f ? 1f : 0f;
            float clickTrain = Mathf.PingPong(t * 38f, 1f) > 0.82f ? 1f : 0f;
            float click = ((float)rng.NextDouble() * 2f - 1f) * clickTrain * burstEnv * 0.5f;
            float hiss = ((float)rng.NextDouble() * 2f - 1f) * 0.035f;
            return click + hiss;
        });
        return chitter;
    }

    public static AudioClip Scream()
    {
        if (scream != null)
        {
            return scream;
        }

        scream = Generate("ScreamerScream", 1.4f, (t, rng) =>
        {
            float progress = t / 1.4f;
            float env = Mathf.Sin(Mathf.Clamp01(progress * 1.1f) * Mathf.PI);
            float baseFreq = Mathf.Lerp(880f, 310f, progress);
            float vibrato = 1f + Mathf.Sin(2f * Mathf.PI * 11f * t) * 0.07f;
            float saw = Mathf.Repeat(t * baseFreq * vibrato, 1f) * 2f - 1f;
            float saw2 = Mathf.Repeat(t * baseFreq * 1.494f, 1f) * 2f - 1f;
            float noise = ((float)rng.NextDouble() * 2f - 1f) * 0.45f;
            return Mathf.Clamp((saw * 0.5f + saw2 * 0.35f + noise) * env, -1f, 1f);
        });
        return scream;
    }

    public static AudioClip Squelch()
    {
        if (squelch != null)
        {
            return squelch;
        }

        squelch = Generate("FleshSquelch", 0.5f, (t, rng) =>
        {
            float env = Mathf.Exp(-t * 11f);
            float noise = ((float)rng.NextDouble() * 2f - 1f);
            float low = Mathf.Sin(2f * Mathf.PI * (120f - t * 90f) * t);
            return (noise * 0.45f + low * 0.5f) * env;
        });
        return squelch;
    }

    public static AudioClip EngineLoop()
    {
        if (engineLoop != null)
        {
            return engineLoop;
        }

        engineLoop = Generate("EngineLoop", 2f, (t, rng) =>
        {
            float cyl = Mathf.Sin(2f * Mathf.PI * 41f * t) * 0.5f
                      + Mathf.Sin(2f * Mathf.PI * 82f * t) * 0.3f
                      + Mathf.Sin(2f * Mathf.PI * 123.5f * t) * 0.14f;
            float rattle = ((float)rng.NextDouble() * 2f - 1f) * 0.08f;
            return cyl + rattle;
        });
        return engineLoop;
    }

    public static AudioClip GateCreak()
    {
        if (gateCreak != null)
        {
            return gateCreak;
        }

        gateCreak = Generate("GateCreak", 3.4f, (t, rng) =>
        {
            float progress = t / 3.4f;
            float env = Mathf.Sin(Mathf.Clamp01(progress) * Mathf.PI);
            float freq = 620f + Mathf.PerlinNoise(t * 3.1f, 0.37f) * 480f;
            float squeal = Mathf.Sin(2f * Mathf.PI * freq * t) * 0.22f;
            float stickSlip = Mathf.PerlinNoise(t * 17f, 4.2f) > 0.62f ? 1f : 0.25f;
            float groan = Mathf.Sin(2f * Mathf.PI * 68f * t) * 0.2f;
            float noise = ((float)rng.NextDouble() * 2f - 1f) * 0.05f;
            return (squeal * stickSlip + groan + noise) * env;
        });
        return gateCreak;
    }

    public static AudioClip KeyJingle()
    {
        if (keyJingle != null)
        {
            return keyJingle;
        }

        keyJingle = Generate("KeyJingle", 0.7f, (t, rng) =>
        {
            float sample = 0f;
            // three little metallic pings at staggered times
            for (int i = 0; i < 3; i++)
            {
                float start = i * 0.16f;
                if (t < start)
                {
                    continue;
                }

                float local = t - start;
                float env = Mathf.Exp(-local * 26f);
                float freq = 2900f + i * 740f;
                sample += Mathf.Sin(2f * Mathf.PI * freq * local) * env * 0.3f;
            }

            return sample;
        });
        return keyJingle;
    }

    public static AudioClip ObjectiveBlip()
    {
        if (objectiveBlip != null)
        {
            return objectiveBlip;
        }

        objectiveBlip = Generate("ObjectiveBlip", 0.3f, (t, rng) =>
        {
            float env = t < 0.12f ? Mathf.Exp(-t * 24f) : Mathf.Exp(-(t - 0.14f) * 24f);
            float freq = t < 0.12f ? 1100f : 1480f;
            if (t >= 0.12f && t < 0.14f)
            {
                return 0f;
            }

            return Mathf.Sin(2f * Mathf.PI * freq * t) * env * 0.4f;
        });
        return objectiveBlip;
    }

    public static AudioClip MissionSting()
    {
        if (missionSting != null)
        {
            return missionSting;
        }

        missionSting = Generate("MissionSting", 1.8f, (t, rng) =>
        {
            float env = Mathf.Exp(-t * 1.6f);
            float chord =
                Mathf.Sin(2f * Mathf.PI * 196f * t) * 0.3f +
                Mathf.Sin(2f * Mathf.PI * 246.9f * t) * 0.26f +
                Mathf.Sin(2f * Mathf.PI * 293.7f * t) * 0.24f;
            return chord * env;
        });
        return missionSting;
    }

    public static AudioClip AmbientDrone()
    {
        if (ambientDrone != null)
        {
            return ambientDrone;
        }

        ambientDrone = Generate("AmbientDrone", 6f, (t, rng) =>
        {
            float slow = Mathf.Sin(2f * Mathf.PI * 0.17f * t);
            float drone =
                Mathf.Sin(2f * Mathf.PI * 48f * t) * 0.16f +
                Mathf.Sin(2f * Mathf.PI * 50.7f * t) * 0.14f +
                Mathf.Sin(2f * Mathf.PI * 96.3f * t) * 0.05f;
            float whisperNoise = ((float)rng.NextDouble() * 2f - 1f) * 0.02f;
            return drone * (0.7f + slow * 0.3f) + whisperNoise;
        });
        return ambientDrone;
    }

    private static AudioClip Generate(string name, float duration, System.Func<float, System.Random, float> sampler)
    {
        int sampleCount = Mathf.RoundToInt(SampleRate * duration);
        float[] samples = new float[sampleCount];
        System.Random rng = new System.Random(name.GetHashCode());

        for (int i = 0; i < sampleCount; i++)
        {
            float t = i / (float)SampleRate;
            samples[i] = Mathf.Clamp(sampler(t, rng), -1f, 1f);
        }

        // short fade at both ends so loops don't click
        int fade = Mathf.Min(512, sampleCount / 4);
        for (int i = 0; i < fade; i++)
        {
            float k = i / (float)fade;
            samples[i] *= k;
            samples[sampleCount - 1 - i] *= k;
        }

        AudioClip clip = AudioClip.Create(name, sampleCount, 1, SampleRate, false);
        clip.SetData(samples, 0);
        return clip;
    }
}
