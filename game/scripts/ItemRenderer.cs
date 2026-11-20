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
/// worth searching stand out; it is gone once you search it, or once you see that someone else has. With dominion on
/// (see <see cref="DominionSystem"/>) each ruin you have seen is an ancient site and flies a banner on a tall pole: in
/// its holder's colour, pale while nobody holds it, with a line over it while a side is taking it ("Orcs taking 40%").
/// </summary>
public partial class ItemRenderer : Node3D
{
    private Simulation _sim = null!;
    private FogOfWar _fog = null!;
    private readonly List<(Sprite3D Sprite, OmniLight3D Light)> _pool = new();
    private float _sliceY = 100000f;
    private readonly List<(MeshInstance3D Pole, MeshInstance3D Flag, Label3D Label)> _banners = new();
    private static readonly Color Unheld = new(0.88f, 0.86f, 0.8f);
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
        UpdateBanners();
    }

    /// <summary>The ancient sites' banners: see the class notes.</summary>
    private void UpdateBanners()
    {
        float s = ChunkMesher.BlockSize;
        int shown = 0;
        if (_sim.Sites.Active)
            foreach (var ruin in _sim.Ruins.Ruins)
            {
                float bottom = (ruin.Y + 1) * s;
                if (bottom > _sliceY || !_fog.HasSeen(ruin.X, ruin.Z)) continue;
                if (shown == _banners.Count) _banners.Add(MakeBanner());
                var (pole, flag, label) = _banners[shown++];
                // The pole stands in a corner of the floor, so the tablet over the middle stays clear.
                var foot = new Vector3((ruin.X - RuinSystem.Reach + 0.5f) * s, bottom, (ruin.Z - RuinSystem.Reach + 0.5f) * s);
                pole.Position = foot + new Vector3(0, 4f * s, 0);
                var colour = ruin.Holder >= 0 ? UnitRenderer.TeamColors[ruin.Holder & 7] : Unheld;
                ((StandardMaterial3D)flag.MaterialOverride).AlbedoColor = colour;
                float wave = 0.06f * MathF.Sin(_time * 3f + ruin.Index);
                flag.Position = foot + new Vector3(0.9f * s, 7.2f * s, 0);
                flag.Rotation = new Vector3(0, wave, 0);
                label.Position = foot + new Vector3(0.9f * s, 8.6f * s, 0);
                if (ruin.Claimant >= 0 && _fog.Sees(ruin.X, ruin.Z))
                {
                    string who = ruin.Claimant == PlayerControl.LocalPlayer ? "Taking" : _sim.Economy.Player(ruin.Claimant).Faction.Name + " taking";
                    label.Text = $"{who} {ruin.Claim * 100 / DominionSystem.ClaimTicks}%";
                    label.Modulate = UnitRenderer.TeamColors[ruin.Claimant & 7].Lightened(0.3f);
                    label.Visible = true;
                }
                else label.Visible = false;
                pole.Visible = flag.Visible = true;
            }
        for (int k = shown; k < _banners.Count; k++) _banners[k].Pole.Visible = _banners[k].Flag.Visible = _banners[k].Label.Visible = false;
    }

    private (MeshInstance3D, MeshInstance3D, Label3D) MakeBanner()
    {
        float s = ChunkMesher.BlockSize;
        var pole = new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(0.18f * s, 8f * s, 0.18f * s) },
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.35f, 0.25f, 0.16f) },
        };
        var flag = new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(1.8f * s, 1.3f * s, 0.08f * s) },
            MaterialOverride = new StandardMaterial3D { AlbedoColor = Unheld },
        };
        var label = new Label3D
        {
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            PixelSize = 0.009f * s,
            FontSize = 48,
            OutlineSize = 10,
            NoDepthTest = true,
            Visible = false,
        };
        AddChild(pole);
        AddChild(flag);
        AddChild(label);
        return (pole, flag, label);
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
