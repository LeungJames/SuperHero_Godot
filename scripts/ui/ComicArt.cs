using Godot;
using System.Collections.Generic;

/// <summary>Four illustrated pages, eleven individually framed story panels.</summary>
public static class ComicArt
{
    private static readonly Dictionary<string, Texture2D> Pages = new();
    public static Texture2D Page(string id)
    {
        if (!Pages.TryGetValue(id, out Texture2D texture))
        {
            string path = $"res://art/narrative/{id}-atlas.png";
            texture = ResourceLoader.Exists(path) ? GD.Load<Texture2D>(path) : null;
            Pages[id] = texture;
        }
        return texture;
    }
    public static Rect2 Region(string id, int index)
    {
        Texture2D texture = Page(id);
        if (texture == null) return new Rect2();
        int count = id == "boss_death" ? 2 : 3;
        float height = texture.GetHeight() / (float)count;
        return new Rect2(2, height * Mathf.Clamp(index, 0, count - 1) + 3, texture.GetWidth() - 4, height - 6);
    }
    public static Texture2D Panel(string id, int index = 0)
    {
        Texture2D texture = Page(id);
        return texture == null ? null : new AtlasTexture { Atlas = texture, Region = Region(id, index) };
    }
    public static string ChapterPage(string id) => id switch { "factory" => "factory_opening", "stronghold" => "boss_intro", "anchor" => "boss_intro", _ => "port_opening" };
}
