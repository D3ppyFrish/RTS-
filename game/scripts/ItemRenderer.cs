using System;
using System.Collections.Generic;
using Godot;
using Rts.Sim;
using Rts.Sim.Units;
using Rts.Sim.World;

namespace Rts.Game;

/// <summary>
/// Draws the unique items lying on the ground (see <see cref="DroppedItemSystem"/>): each relic or the Dusk
/// Staff as its pixel icon standing up over the block where its bearer fell, bobbing a little with a soft
/// light, so players can see something worth picking up. Only where the player can see. Over each old ruin you
/// have seen that still holds its find (see <see cref="RuinSystem"/>) floats a carved stone tablet, so the ruins
/// worth searching stand out; it is gone once you search it, or once you see that someone else has.
/// </summary>
public partial class ItemRenderer : Node3D
{
    private Simulation _sim = null!;
    private FogOfWar _fog = null!;
    private readonly List<(Sprite3D Sprite, OmniLight3D Light)> _pool = new();
    private float _sliceY = 100000f;
    private float _time;

    // 12 picture pixels across a block and a half for an item, so it shows from the usual camera; three blocks for a ruin's tablet.
    private static readonly float ItemSize = 1.5f * ChunkMesher.BlockSize / (12f * UiIcons.Scale);
    private static readonly float RuinSize = 3f * ChunkMesher.BlockSize / (12f * UiIcons.Scale);

    public void Init(Simulation sim, FogOfWar fog)
    {
        _sim = sim;
        _fog = fog;
    }

    /// <summary>Items above the height slice are hidden, as the ground there is.</summary>
    public void SetSlice(float y) => _sliceY = y;

    /// <summary>The icon the item shows: its relic's, or the Dusk Bloom's for the staff.</summary>
    public static string IconOf(DroppedItem item) =>
        item.Relic is { } r ? (string.IsNullOrEmpty(r.Icon) ? r.Id : r.Icon) : "dusk_bloom";

    public void UpdateFrame(float delta)
    {
        _time += delta;
        float s = ChunkMesher.BlockSize;
        int shown = 0;
        foreach (var item in _sim.Items.Items)
        {
            float bottom = item.Y * s;
            if (bottom > _sliceY || !_fog.Sees(item.X, item.Z)) continue;
            if (shown == _pool.Count) _pool.Add(Make());
            var (sprite, light) = _pool[shown++];
            sprite.Texture = UiIcons.Get(IconOf(item));
            sprite.PixelSize = ItemSize;
            float bob = 0.08f * s * MathF.Sin(_time * 2.2f + item.Id);
            sprite.Position = new Vector3((item.X + 0.5f) * s, bottom + 0.9f * s + bob, (item.Z + 0.5f) * s);
            light.Position = sprite.Position;
            light.LightColor = item.Staff ? new Color(0.55f, 0.95f, 1f) : new Color(1f, 0.85f, 0.5f);
            sprite.Visible = light.Visible = true;
        }
        foreach (var ruin in _sim.Ruins.Ruins)
        {
            float bottom = (ruin.Y + 6) * s;
            if (bottom > _sliceY || !_fog.HasSeen(ruin.X, ruin.Z)) continue;
            if (ruin.Searched && (ruin.SearchedBy == PlayerControl.LocalPlayer || _fog.Sees(ruin.X, ruin.Z))) continue;
            if (shown == _pool.Count) _pool.Add(Make());
            var (sprite, light) = _pool[shown++];
            sprite.Texture = UiIcons.Get("ruin");
            sprite.PixelSize = RuinSize;
            float bob = 0.12f * s * MathF.Sin(_time * 1.4f + ruin.Index);
            sprite.Position = new Vector3((ruin.X + 0.5f) * s, bottom + bob, (ruin.Z + 0.5f) * s);
            light.Position = sprite.Position;
            light.LightColor = new Color(1f, 0.92f, 0.65f);
            sprite.Visible = light.Visible = true;
        }
        for (int k = shown; k < _pool.Count; k++) _pool[k].Sprite.Visible = _pool[k].Light.Visible = false;
    }

    private (Sprite3D, OmniLight3D) Make()
    {
        var sprite = new Sprite3D
        {
            Billboard = BaseMaterial3D.BillboardModeEnum.FixedY,
            TextureFilter = BaseMaterial3D.TextureFilterEnum.Nearest,
            Shaded = false,
            AlphaCut = SpriteBase3D.AlphaCutMode.Discard,
            PixelSize = ItemSize,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        var light = new OmniLight3D { OmniRange = 3f * ChunkMesher.BlockSize, LightEnergy = 1.5f, ShadowEnabled = false };
        AddChild(sprite);
        AddChild(light);
        return (sprite, light);
    }
}
