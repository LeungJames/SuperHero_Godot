using Godot;
using System.Collections.Generic;

/// <summary>Authored audio bank. Existing procedural sounds remain a fallback for uncommon cues.</summary>
public static class AudioPalette
{
	private static readonly Dictionary<string, AudioStream> Streams = new();
	public static AudioStream Load(string path, bool loop = false)
	{
		if (Streams.TryGetValue(path, out AudioStream cached)) return cached;
		if (!ResourceLoader.Exists(path)) return null;
		AudioStream stream = GD.Load<AudioStream>(path);
		if (stream is AudioStreamOggVorbis ogg) ogg.Loop = loop;
		Streams[path] = stream;
		return stream;
	}
	public static AudioStream Shot(WeaponData weapon = null)
	{
		string id = weapon?.Id?.ToLowerInvariant() ?? "rifle";
		string family = id.Contains("shotgun") ? "shotgun" : id.Contains("sniper") ? "sniper"
			: id.Contains("smg") ? "smg" : id == "ar" || id.Contains("assault") || id.Contains("rifle") ? "rifle" : "pistol";
		return Load($"res://audio/sfx/{family}_{GD.RandRange(0, 2)}.wav") ?? ProceduralAudio.Get(SfxKind.Gunshot);
	}
	public static AudioStream Step(string surface) =>
		Load($"res://audio/sfx/step_{surface}_{GD.RandRange(0, 3)}.wav") ?? ProceduralAudio.Get(SfxKind.Footstep);
	public static AudioStream Cue(SfxKind kind)
	{
		string name = kind switch
		{
			SfxKind.ReloadMagOut => "mag_out", SfxKind.ReloadSeat => "mag_seat", SfxKind.ReloadSlide => "slide",
			SfxKind.DryFire => "dry_fire", SfxKind.Hit => "impact", SfxKind.Headshot => "headshot",
			SfxKind.Kill => "kill", SfxKind.Hurt => "hurt", SfxKind.Land => "land", _ => null,
		};
		return name == null ? ProceduralAudio.Get(kind) : Load($"res://audio/sfx/{name}.wav") ?? ProceduralAudio.Get(kind);
	}
	public static void Clear() => Streams.Clear();
}
