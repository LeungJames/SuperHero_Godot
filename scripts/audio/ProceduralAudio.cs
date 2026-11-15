using Godot;
using System;
using System.Collections.Generic;

/// <summary>可合成的音效种类（设计文档 §8.8）。</summary>
public enum SfxKind
{
	Gunshot,
	DryFire,
	Reload,
	ReloadMagOut,
	ReloadSeat,
	ReloadSlide,
	Hit,
	Headshot,
	Hurt,
	Footstep,
	Land,
	Alert,
	Death,
	Kill,
	Melee,
	Whiz,
}

/// <summary>
/// 运行时合成全部音效与循环环境音，缓存为 AudioStreamWav，不依赖任何外部音频素材。
/// 只负责合成，不负责播放调度（§8.8、§9.3）。
/// </summary>
public static class ProceduralAudio
{
	private const int SampleRate = 22050;

	private static readonly Dictionary<SfxKind, AudioStreamWav> Cache = new();

	/// <summary>取一次性音效（首次调用时合成）。</summary>
	public static AudioStreamWav Get(SfxKind kind)
	{
		if (Cache.TryGetValue(kind, out AudioStreamWav cached))
			return cached;

		AudioStreamWav stream = kind switch
		{
			SfxKind.Gunshot => Build(GunshotSamples(), false),
			SfxKind.DryFire => Build(DryFireSamples(), false),
			SfxKind.Reload => Build(ReloadSamples(), false),
			SfxKind.ReloadMagOut => Build(ReloadMagOutSamples(), false),
			SfxKind.ReloadSeat => Build(ReloadSeatSamples(), false),
			SfxKind.ReloadSlide => Build(ReloadSlideSamples(), false),
			SfxKind.Hit => Build(HitSamples(), false),
			SfxKind.Headshot => Build(HeadshotSamples(), false),
			SfxKind.Hurt => Build(HurtSamples(), false),
			SfxKind.Footstep => Build(FootstepSamples(), false),
			SfxKind.Land => Build(LandSamples(), false),
			SfxKind.Alert => Build(AlertSamples(), false),
			SfxKind.Death => Build(DeathSamples(), false),
			SfxKind.Kill => Build(KillSamples(), false),
			SfxKind.Melee => Build(MeleeSamples(), false),
			SfxKind.Whiz => Build(WhizSamples(), false),
			_ => Build(FootstepSamples(), false),
		};

		Cache[kind] = stream;
		return stream;
	}

	private static AudioStreamWav _wind;
	private static AudioStreamWav _waves;
	private static AudioStreamWav _helicopter;
	private static AudioStreamWav _radio;

	/// <summary>环境风声（循环）。</summary>
	public static AudioStreamWav Wind() => _wind ??= Build(WindSamples(), true);

	/// <summary>海浪（循环）。</summary>
	public static AudioStreamWav Waves() => _waves ??= Build(WaveSamples(), true);

	/// <summary>直升机引擎（循环）。</summary>
	public static AudioStreamWav Helicopter() => _helicopter ??= Build(HelicopterSamples(), true);

	/// <summary>无线电提示音。</summary>
	public static AudioStreamWav Radio() => _radio ??= Build(RadioSamples(), false);

	/// <summary>退出时释放静态缓存，避免引擎关闭时报告资源泄漏。</summary>
	public static void ClearCache()
	{
		Cache.Clear();
		_wind = null;
		_waves = null;
		_helicopter = null;
		_radio = null;
	}

	// ---------------------------------------------------------------- 合成

	private static float[] GunshotSamples()
	{
		var rng = new Random(11);
		float[] b = new float[Frames(0.26f)];
		for (int i = 0; i < b.Length; i++)
		{
			float t = i / (float)SampleRate;
			float noise = Noise(rng);
			float crack = noise * Mathf.Exp(-t * 45f);
			float boom = Mathf.Sin(Mathf.Tau * 82f * t) * Mathf.Exp(-t * 14f) * 0.9f;
			float slap = Mathf.Sin(Mathf.Tau * 240f * t) * Mathf.Exp(-t * 55f) * 0.35f;
			b[i] = crack * 0.75f + boom + slap;
		}
		return Normalize(b, 0.92f);
	}

	private static float[] DryFireSamples()
	{
		float[] b = new float[Frames(0.13f)];
		AddClick(b, 0.0f, 2100f, 0.010f, 0.8f);
		AddClick(b, 0.055f, 1700f, 0.008f, 0.6f);
		return Normalize(b, 0.7f);
	}

	/// <summary>近战：短促的挥空风声 + 命中闷响。</summary>
	private static float[] MeleeSamples()
	{
		var rng = new Random(41);
		float[] b = new float[Frames(0.30f)];
		AddBurst(b, rng, 0.0f, 0.11f, 0.28f);
		AddClick(b, 0.02f, 240f, 0.05f, 0.25f);
		AddClick(b, 0.13f, 520f, 0.035f, 0.6f);
		return Normalize(b, 0.7f);
	}

	private static float[] ReloadSamples()	{
		var rng = new Random(23);
		float[] b = new float[Frames(0.55f)];
		AddClick(b, 0.0f, 900f, 0.03f, 0.55f);
		AddBurst(b, rng, 0.16f, 0.05f, 0.5f);
		AddClick(b, 0.32f, 1300f, 0.02f, 0.65f);
		AddBurst(b, rng, 0.40f, 0.06f, 0.45f);
		AddClick(b, 0.47f, 1800f, 0.015f, 0.6f);
		return Normalize(b, 0.72f);
	}

	/// <summary>撤匣：金属卡榫松开 + 弹匣拖出，短促。</summary>
	private static float[] ReloadMagOutSamples()
	{
		var rng = new Random(83);
		float[] b = new float[Frames(0.16f)];
		AddClick(b, 0.0f, 720f, 0.02f, 0.5f);
		AddBurst(b, rng, 0.005f, 0.05f, 0.35f);
		AddClick(b, 0.055f, 1650f, 0.012f, 0.65f);
		return Normalize(b, 0.65f);
	}

	/// <summary>顶匣到位：低频闷响 + 金属咬合，比撤匣更重。</summary>
	private static float[] ReloadSeatSamples()
	{
		var rng = new Random(89);
		float[] b = new float[Frames(0.24f)];
		AddClick(b, 0.0f, 190f, 0.035f, 0.85f);
		AddBurst(b, rng, 0.0f, 0.045f, 0.5f);
		AddClick(b, 0.012f, 1450f, 0.014f, 0.75f);
		AddClick(b, 0.06f, 980f, 0.03f, 0.35f);
		return Normalize(b, 0.85f);
	}

	/// <summary>拉套筒：滑动摩擦 + 复进撞击。</summary>
	private static float[] ReloadSlideSamples()
	{
		var rng = new Random(97);
		float[] b = new float[Frames(0.18f)];
		AddClick(b, 0.0f, 1250f, 0.012f, 0.5f);
		AddBurst(b, rng, 0.01f, 0.04f, 0.35f);
		AddClick(b, 0.062f, 2100f, 0.01f, 0.8f);
		AddClick(b, 0.07f, 620f, 0.025f, 0.5f);
		return Normalize(b, 0.8f);
	}

	private static float[] HitSamples()
	{
		var rng = new Random(31);
		float[] b = new float[Frames(0.14f)];
		for (int i = 0; i < b.Length; i++)
		{
			float t = i / (float)SampleRate;
			float thud = Mathf.Sin(Mathf.Tau * 150f * t) * Mathf.Exp(-t * 34f);
			// 高频脆响让命中从枪声里分辨得出来
			float tick = Mathf.Sin(Mathf.Tau * 1650f * t) * Mathf.Exp(-t * 90f) * 0.55f;
			float noise = Noise(rng) * Mathf.Exp(-t * 60f) * 0.45f;
			b[i] = thud + tick + noise;
		}
		return Normalize(b, 0.9f);
	}

	private static float[] HeadshotSamples()
	{
		var rng = new Random(37);
		float[] b = new float[Frames(0.18f)];
		for (int i = 0; i < b.Length; i++)
		{
			float t = i / (float)SampleRate;
			float ding = Mathf.Sin(Mathf.Tau * 1850f * t) * Mathf.Exp(-t * 16f) * 0.6f;
			float ring = Mathf.Sin(Mathf.Tau * 2650f * t) * Mathf.Exp(-t * 26f) * 0.3f;
			float thud = Mathf.Sin(Mathf.Tau * 110f * t) * Mathf.Exp(-t * 30f) * 0.7f;
			float noise = Noise(rng) * Mathf.Exp(-t * 80f) * 0.25f;
			b[i] = ding + ring + thud + noise;
		}
		return Normalize(b, 0.85f);
	}

	/// <summary>子弹掠过：高频噪声快速扫过，短促，只用于"被压制"提示（§2.7）。</summary>
	private static float[] WhizSamples()
	{
		var rng = new Random(131);
		float[] b = new float[Frames(0.20f)];
		for (int i = 0; i < b.Length; i++)
		{
			float t = i / (float)SampleRate;
			float env = Mathf.Sin(Mathf.Pi * Mathf.Clamp(t / 0.20f, 0f, 1f));
			float sweep = Mathf.Sin(Mathf.Tau * (2600f - 1500f * t) * t) * 0.3f;
			b[i] = Noise(rng) * env * 0.75f + sweep * env;
		}
		return Normalize(b, 0.55f);
	}

	private static float[] HurtSamples()
	{
		var rng = new Random(41);
		float[] b = new float[Frames(0.22f)];
		for (int i = 0; i < b.Length; i++)
		{
			float t = i / (float)SampleRate;
			float thud = Mathf.Sin(Mathf.Tau * 92f * t) * Mathf.Exp(-t * 20f);
			float breath = Noise(rng) * Mathf.Exp(-t * 9f) * 0.35f;
			b[i] = thud + breath;
		}
		return Normalize(b, 0.8f);
	}

	private static float[] FootstepSamples()
	{
		var rng = new Random(43);
		float[] b = new float[Frames(0.09f)];
		for (int i = 0; i < b.Length; i++)
		{
			float t = i / (float)SampleRate;
			float env = Mathf.Exp(-t * 55f) * (1f - Mathf.Exp(-t * 400f));
			b[i] = Noise(rng) * env;
		}
		return Normalize(Lowpass(b, 1400f), 0.55f);
	}

	private static float[] LandSamples()
	{
		var rng = new Random(47);
		float[] b = new float[Frames(0.22f)];
		for (int i = 0; i < b.Length; i++)
		{
			float t = i / (float)SampleRate;
			float thud = Mathf.Sin(Mathf.Tau * 68f * t) * Mathf.Exp(-t * 22f);
			float gravel = Noise(rng) * Mathf.Exp(-t * 32f) * 0.4f;
			b[i] = thud + gravel;
		}
		return Normalize(b, 0.85f);
	}

	private static float[] AlertSamples()
	{
		float[] b = new float[Frames(0.32f)];
		float phase = 0f;
		for (int i = 0; i < b.Length; i++)
		{
			float t = i / (float)SampleRate;
			float freq = Mathf.Lerp(300f, 620f, Mathf.Clamp(t / 0.3f, 0f, 1f));
			phase += Mathf.Tau * freq / SampleRate;
			float saw = 2f * (phase / Mathf.Tau - Mathf.Floor(phase / Mathf.Tau + 0.5f));
			float env = Mathf.Min(1f, t * 24f) * Mathf.Exp(-t * 4.5f);
			b[i] = saw * env * 0.35f;
		}
		return Normalize(b, 0.55f);
	}

	private static float[] DeathSamples()
	{
		var rng = new Random(53);
		float[] b = new float[Frames(0.35f)];
		for (int i = 0; i < b.Length; i++)
		{
			float t = i / (float)SampleRate;
			float thud = Mathf.Sin(Mathf.Tau * 78f * t) * Mathf.Exp(-t * 13f);
			float noise = Noise(rng) * Mathf.Exp(-t * 22f) * 0.35f;
			b[i] = thud + noise;
		}
		return Normalize(b, 0.8f);
	}

	/// <summary>击杀确认：低频闷响 + 金属脆响 + 短噪声，比命中更厚、更长尾巴。</summary>
	private static float[] KillSamples()
	{
		var rng = new Random(59);
		float[] b = new float[Frames(0.45f)];
		for (int i = 0; i < b.Length; i++)
		{
			float t = i / (float)SampleRate;
			float boom = Mathf.Sin(Mathf.Tau * 62f * t) * Mathf.Exp(-t * 9f) * 1.0f;
			float ping = Mathf.Sin(Mathf.Tau * 1320f * t) * Mathf.Exp(-t * 11f) * 0.45f;
			float ring = Mathf.Sin(Mathf.Tau * 1980f * t) * Mathf.Exp(-t * 15f) * 0.28f;
			float crack = Noise(rng) * Mathf.Exp(-t * 40f) * 0.5f;
			b[i] = boom + ping + ring + crack;
		}
		return Normalize(b, 0.95f);
	}

	private static float[] WindSamples()
	{
		var rng = new Random(61);
		float[] b = new float[Frames(4f)];
		for (int i = 0; i < b.Length; i++)
		{
			float t = i / (float)SampleRate;
			float gust = 0.55f + 0.45f * Mathf.Sin(Mathf.Tau * 0.18f * t + 0.7f);
			b[i] = Noise(rng) * gust;
		}
		Lowpass(b, 900f);
		return Normalize(b, 0.5f);
	}

	private static float[] WaveSamples()
	{
		var rng = new Random(67);
		float[] b = new float[Frames(4f)];
		for (int i = 0; i < b.Length; i++)
		{
			float t = i / (float)SampleRate;
			float swell = Mathf.Pow(0.5f + 0.5f * Mathf.Sin(Mathf.Tau * 0.22f * t), 2f);
			b[i] = Noise(rng) * swell;
		}
		Lowpass(b, 700f);
		return Normalize(b, 0.55f);
	}

	private static float[] HelicopterSamples()
	{
		var rng = new Random(71);
		float[] b = new float[Frames(3f)];
		for (int i = 0; i < b.Length; i++)
		{
			float t = i / (float)SampleRate;
			float rotor = 0.55f + 0.45f * Mathf.Sin(Mathf.Tau * 20f * t);
			float whine = Mathf.Sin(Mathf.Tau * 118f * t) * 0.15f;
			b[i] = Noise(rng) * rotor * 0.8f + whine;
		}
		return Normalize(b, 0.65f);
	}

	private static float[] RadioSamples()
	{
		var rng = new Random(73);
		float[] b = new float[Frames(0.62f)];
		for (int i = 0; i < b.Length; i++)
		{
			float t = i / (float)SampleRate;
			float hiss = Noise(rng) * 0.25f;
			float beep = 0f;
			foreach (float start in new[] { 0.04f, 0.16f, 0.30f })
			{
				float dt = t - start;
				if (dt >= 0f && dt < 0.06f)
					beep += Mathf.Sin(Mathf.Tau * 1250f * dt) * Mathf.Exp(-dt * 26f) * 0.5f;
			}
			float gate = Mathf.Min(1f, t * 30f) * Mathf.Min(1f, (0.62f - t) * 20f);
			b[i] = (hiss + beep) * gate;
		}
		return Normalize(b, 0.65f);
	}

	// ---------------------------------------------------------------- 工具

	private static int Frames(float seconds) => Mathf.Max(1, (int)(seconds * SampleRate));

	private static float Noise(Random rng) => (float)(rng.NextDouble() * 2.0 - 1.0);

	private static void AddClick(float[] buffer, float start, float frequency, float decay, float gain)
	{
		int offset = (int)(start * SampleRate);
		for (int i = offset; i < buffer.Length; i++)
		{
			float t = (i - offset) / (float)SampleRate;
			buffer[i] += Mathf.Sin(Mathf.Tau * frequency * t) * Mathf.Exp(-t / decay) * gain;
		}
	}

	private static void AddBurst(float[] buffer, Random rng, float start, float length, float gain)
	{
		int offset = (int)(start * SampleRate);
		int count = (int)(length * SampleRate);
		for (int i = 0; i < count && offset + i < buffer.Length; i++)
		{
			float k = i / (float)count;
			buffer[offset + i] += Noise(rng) * (1f - k) * gain;
		}
	}

	private static float[] Lowpass(float[] buffer, float cutoff)
	{
		float a = 1f - Mathf.Exp(-Mathf.Tau * cutoff / SampleRate);
		float y = 0f;
		for (int i = 0; i < buffer.Length; i++)
		{
			y += a * (buffer[i] - y);
			buffer[i] = y;
		}
		return buffer;
	}

	private static float[] Normalize(float[] buffer, float peak)
	{
		float max = 0.0001f;
		foreach (float s in buffer)
			max = Mathf.Max(max, Mathf.Abs(s));

		float gain = peak / max;
		for (int i = 0; i < buffer.Length; i++)
			buffer[i] *= gain;

		return buffer;
	}

	private static AudioStreamWav Build(float[] samples, bool loop)
	{
		var data = new byte[samples.Length * 2];
		for (int i = 0; i < samples.Length; i++)
		{
			short value = (short)(Mathf.Clamp(samples[i], -1f, 1f) * 32767f);
			data[i * 2] = (byte)(value & 0xff);
			data[i * 2 + 1] = (byte)((value >> 8) & 0xff);
		}

		var wav = new AudioStreamWav
		{
			Data = data,
			Format = AudioStreamWav.FormatEnum.Format16Bits,
			MixRate = SampleRate,
			Stereo = false,
		};

		if (loop)
		{
			wav.LoopMode = AudioStreamWav.LoopModeEnum.Forward;
			wav.LoopBegin = 0;
			wav.LoopEnd = samples.Length;
		}

		return wav;
	}
}
