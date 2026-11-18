using System.Collections.Generic;
using Godot;

namespace Rts.Game;

/// <summary>
/// Placeholder unit models built from voxels an eighth of a terrain block across (6.25 cm), so units
/// look finer than the 0.5 m terrain. Models face +Z. Voxels marked as team colour are tinted per player
/// in the shader (their vertex colour alpha is 0 and red holds the shade). The forms units evolve into
/// look like the unit they come from with something added, and wear a gold belt. Monsters wear a collar in
/// their owner's colour (the wild's, until a player tames them). "den" is a lair's den and "banner" the flag
/// at a city-state's market, in its ally's colour; neither is a unit.
/// </summary>
public static class UnitModels
{
    public const float VoxelSize = ChunkMesher.BlockSize / 8f;

    private static readonly Color Skin = new(0.86f, 0.66f, 0.5f);
    private static readonly Color Hair = new(0.35f, 0.22f, 0.12f);
    private static readonly Color Pants = new(0.32f, 0.24f, 0.16f);
    private static readonly Color Wood = new(0.5f, 0.33f, 0.16f);
    private static readonly Color Steel = new(0.72f, 0.74f, 0.78f);
    private static readonly Color Dark = new(0.12f, 0.1f, 0.1f);
    private static readonly Color Horse = new(0.45f, 0.3f, 0.18f);
    private static readonly Color PaleHorse = new(0.72f, 0.62f, 0.48f);
    private static readonly Color Leather = new(0.42f, 0.28f, 0.15f);
    private static readonly Color Cloth = new(0.9f, 0.88f, 0.82f);
    private static readonly Color Leaf = new(0.26f, 0.45f, 0.2f);
    private static readonly Color Gold = new(0.95f, 0.75f, 0.25f);
    private static readonly Color Fire = new(1f, 0.5f, 0.12f);
    private static readonly Color Glow = new(0.4f, 0.9f, 1f);
    private static readonly Color Lamp = new(1f, 0.95f, 0.6f);
    private static readonly Color Fur = new(0.46f, 0.44f, 0.41f);
    private static readonly Color PaleFur = new(0.72f, 0.69f, 0.63f);
    private static readonly Color DarkFur = new(0.3f, 0.28f, 0.26f);
    private static readonly Color Rock = new(0.4f, 0.33f, 0.25f);
    private static readonly Color DarkRock = new(0.25f, 0.21f, 0.17f);
    private static readonly Color Moss = new(0.3f, 0.4f, 0.22f);
    private static readonly Color Mule = new(0.48f, 0.4f, 0.33f);
    private static readonly Color GoblinSkin = new(0.42f, 0.62f, 0.25f);
    private static readonly Color GoblinEye = new(1f, 0.85f, 0.2f);
    private static readonly Color OrcSkin = new(0.36f, 0.5f, 0.24f);
    private static readonly Color OrcEye = new(0.95f, 0.25f, 0.12f);
    private static readonly Color Bone = new(0.92f, 0.88f, 0.74f);
    private static readonly Color Hide = new(0.36f, 0.27f, 0.2f);
    private static readonly Color Boar = new(0.3f, 0.22f, 0.17f);
    private static readonly Color Bristle = new(0.17f, 0.13f, 0.1f);
    private static readonly Color Iron = new(0.38f, 0.38f, 0.4f);
    private static readonly Color Toxic = new(0.55f, 1f, 0.3f);
    private static readonly Color DuskBark = new(0.34f, 0.33f, 0.39f);
    private static readonly Color DuskBarkDark = new(0.18f, 0.17f, 0.22f);
    private static readonly Color DuskVein = new(0.3f, 0.9f, 0.95f);
    private static readonly Color DuskLeaf = new(0.55f, 0.33f, 0.7f);
    private static readonly Color Team = new(1f, 0f, 0f, 0f);      // full-strength team colour
    private static readonly Color TeamDark = new(0.7f, 0f, 0f, 0f); // shaded team colour

    private sealed class Builder
    {
        public readonly Dictionary<Vector3I, Color> Voxels = new();

        public Builder Box(int x0, int y0, int z0, int x1, int y1, int z1, Color c)
        {
            for (int x = x0; x <= x1; x++)
            for (int y = y0; y <= y1; y++)
            for (int z = z0; z <= z1; z++)
                Voxels[new Vector3I(x, y, z)] = c;
            return this;
        }

        /// <summary>Removes the voxels in a box.</summary>
        public Builder Clear(int x0, int y0, int z0, int x1, int y1, int z1)
        {
            for (int x = x0; x <= x1; x++)
            for (int y = y0; y <= y1; y++)
            for (int z = z0; z <= z1; z++)
                Voxels.Remove(new Vector3I(x, y, z));
            return this;
        }

        /// <summary>
        /// A wolf facing +Z, with a collar in its owner's colour; <paramref name="big"/> makes one large enough
        /// to ride, its back one voxel higher at 8 (a rider sits at lift 5).
        /// </summary>
        public Builder Wolf(Color coat, Color belly, bool big = false)
        {
            int k = big ? 1 : 0;
            int top = 6 + 2 * k, legs = 2 + k, front = 3 + k, back = -5 - k, head = top - 1;
            return Box(-2, legs + 1, back, 1, top, front, coat)                                       // body
                .Box(-2, legs + 1, back + 1, 1, legs + 1, front - 1, belly)
                .Box(-2, 0, back, -1, legs, back + 1, coat).Box(0, 0, back, 1, legs, back + 1, coat)  // hind legs
                .Box(-2, 0, front - 2, -1, legs, front - 1, coat).Box(0, 0, front - 2, 1, legs, front - 1, coat)
                .Box(-2, head, front, 1, head + 3, front + 3, coat)                                   // head
                .Box(-1, head, front + 4, 0, head + 1, front + 6, coat)                               // snout
                .Box(-1, head + 1, front + 6, 0, head + 1, front + 6, Dark)                           // nose
                .Box(-2, head + 2, front + 3, -2, head + 2, front + 3, Dark).Box(1, head + 2, front + 3, 1, head + 2, front + 3, Dark)
                .Box(-2, head + 4, front + 1, -2, head + 5, front + 2, coat).Box(1, head + 4, front + 1, 1, head + 5, front + 2, coat) // ears
                .Box(-1, top - 1, back - 3, 0, top, back - 1, coat)                                   // tail
                .Box(-2, head, front, 1, head + 2, front, Team);                                      // collar
        }

        public Builder Humanoid(int lift = 0, bool legs = true)
        {
            if (legs)
            {
                Box(-2, lift, -1, -1, lift + 4, 0, Pants);
                Box(0, lift, -1, 1, lift + 4, 0, Pants);
            }
            Box(-2, lift + 5, -1, 1, lift + 9, 0, Team);
            Box(-2, lift + 5, -1, 1, lift + 5, 0, Dark);              // belt
            Box(-3, lift + 6, -1, -3, lift + 9, 0, TeamDark);         // arms
            Box(2, lift + 6, -1, 2, lift + 9, 0, TeamDark);
            Box(-3, lift + 5, -1, -3, lift + 5, 0, Skin);             // hands
            Box(2, lift + 5, -1, 2, lift + 5, 0, Skin);
            Box(-2, lift + 10, -2, 1, lift + 13, 1, Skin);            // head
            Box(-2, lift + 13, -2, 1, lift + 13, 1, Hair);
            Box(-2, lift + 11, -2, 1, lift + 12, -2, Hair);
            Box(-1, lift + 11, 1, -1, lift + 11, 1, Dark);            // eyes
            Box(0, lift + 11, 1, 0, lift + 11, 1, Dark);
            return this;
        }

        /// <summary>A goblin: a head shorter than a person, green, with long ears and yellow eyes, in rags of its owner's colour.</summary>
        public Builder Goblin() =>
            Box(-2, 0, -1, -1, 3, 0, Pants).Box(0, 0, -1, 1, 3, 0, Pants)                // legs
            .Box(-2, 4, -1, 1, 7, 0, Team).Box(-2, 4, -1, 1, 4, 0, Dark)                 // body and belt
            .Box(-3, 5, -1, -3, 7, 0, TeamDark).Box(2, 5, -1, 2, 7, 0, TeamDark)         // arms
            .Box(-3, 4, -1, -3, 4, 0, GoblinSkin).Box(2, 4, -1, 2, 4, 0, GoblinSkin)     // hands
            .Box(-2, 8, -2, 1, 11, 1, GoblinSkin)                                        // head
            .Box(-4, 10, -1, -3, 11, -1, GoblinSkin).Box(2, 10, -1, 3, 11, -1, GoblinSkin) // ears
            .Box(-1, 10, 1, -1, 10, 1, GoblinEye).Box(0, 10, 1, 0, 10, 1, GoblinEye)     // eyes
            .Box(-1, 9, 2, 0, 9, 2, GoblinSkin);                                         // nose

        /// <summary>The gold belt every form wears.</summary>
        public Builder Rank(int lift = 0) => Box(-2, lift + 5, -1, 1, lift + 5, 0, Gold);

        /// <summary>A horse, for riders to sit on at lift 6.</summary>
        public Builder Mount(Color coat) =>
            Box(-2, 5, -6, 1, 9, 5, coat)                                                // body
            .Box(-2, 0, -6, -1, 4, -5, coat).Box(0, 0, -6, 1, 4, -5, coat)
            .Box(-2, 0, 3, -1, 4, 4, coat).Box(0, 0, 3, 1, 4, 4, coat)
            .Box(-1, 8, 5, 0, 12, 6, coat).Box(-1, 11, 7, 0, 12, 9, coat)
            .Box(-1, 6, -7, 0, 8, -7, Dark);                                              // tail

        public Builder Bow(int bottom, int top, int x = -4, Color? wood = null) =>
            Box(x, bottom + 1, 1, x, top - 1, 1, wood ?? Wood).Box(x, bottom, 0, x, bottom, 0, wood ?? Wood).Box(x, top, 0, x, top, 0, wood ?? Wood);

        /// <summary>A cape in its owner's colour hanging down the back from the shoulders, so the team shows from behind and above.</summary>
        public Builder Cape(int lift = 0, int bottom = 2) =>
            Box(-3, lift + bottom, -2, 2, lift + 10, -2, TeamDark).Box(-3, lift + 10, -2, 2, lift + 10, -1, Team);

        /// <summary>
        /// An orc: a head taller and a voxel broader than a person, hunched, with a heavy jaw, tusks and red
        /// eyes, bare green arms, and a body wrapped in its owner's colour from the shoulders to the knees.
        /// Its hands are at x = -4 and 3, at lift + 5; its head reaches lift + 15.
        /// </summary>
        public Builder Orc(int lift = 0, bool legs = true)
        {
            if (legs)
            {
                Box(-3, lift, -1, -2, lift + 4, 0, Hide);
                Box(1, lift, -1, 2, lift + 4, 0, Hide);
                Box(-3, lift + 3, -1, 2, lift + 4, 1, TeamDark);                        // kilt
            }
            Box(-3, lift + 5, -1, 2, lift + 10, 1, Team);                               // broad body
            Box(-3, lift + 5, -1, 2, lift + 5, 1, Leather);                             // belt
            Box(-1, lift + 5, 2, 0, lift + 5, 2, Bone);                                 // buckle
            Box(-4, lift + 6, -1, -4, lift + 9, 0, OrcSkin);                            // bare arms
            Box(3, lift + 6, -1, 3, lift + 9, 0, OrcSkin);
            Box(-4, lift + 5, -1, -4, lift + 5, 0, OrcSkin);                            // hands
            Box(3, lift + 5, -1, 3, lift + 5, 0, OrcSkin);
            Box(-4, lift + 10, -2, -3, lift + 11, 1, TeamDark);                         // shoulder pads
            Box(2, lift + 10, -2, 3, lift + 11, 1, TeamDark);
            Box(-2, lift + 11, -1, 1, lift + 14, 2, OrcSkin);                           // head, thrust forward
            Box(-2, lift + 10, 1, 1, lift + 10, 3, OrcSkin);                            // heavy jaw
            Box(-2, lift + 11, 3, -2, lift + 12, 3, Bone).Box(1, lift + 11, 3, 1, lift + 12, 3, Bone); // tusks
            Box(-2, lift + 14, 2, 1, lift + 14, 2, Dark);                               // brow
            Box(-1, lift + 13, 2, -1, lift + 13, 2, OrcEye).Box(0, lift + 13, 2, 0, lift + 13, 2, OrcEye);
            Box(-3, lift + 12, 0, -3, lift + 13, 0, OrcSkin).Box(2, lift + 12, 0, 2, lift + 13, 0, OrcSkin); // ears
            return this;
        }

        /// <summary>A war boar, for orc riders to sit on at lift 7: low, broad and bristly, with tusks.</summary>
        public Builder WarBoar() =>
            Box(-3, 3, -6, 2, 8, 4, Boar)                                                // body
            .Box(-1, 9, -5, 0, 9, 3, Bristle)                                            // bristles along the back
            .Box(-3, 0, -5, -2, 2, -4, Boar).Box(1, 0, -5, 2, 2, -4, Boar)               // hind legs
            .Box(-3, 0, 2, -2, 2, 3, Boar).Box(1, 0, 2, 2, 2, 3, Boar)                   // forelegs
            .Box(-2, 3, 5, 1, 7, 8, Boar)                                                // head
            .Box(-1, 3, 9, 0, 5, 9, Hide).Box(-1, 4, 10, 0, 4, 10, Dark)                 // snout
            .Box(-3, 4, 8, -3, 6, 10, Bone).Box(2, 4, 8, 2, 6, 10, Bone)                 // tusks
            .Box(-2, 6, 8, -2, 6, 8, Dark).Box(1, 6, 8, 1, 6, 8, Dark)                   // eyes
            .Box(-3, 7, 5, -3, 8, 6, Boar).Box(2, 7, 5, 2, 8, 6, Boar)                   // ears
            .Box(-1, 6, -7, 0, 7, -7, Bristle);                                          // tail

        /// <summary>The mage's robe and pointed hat.</summary>
        public Builder Robe() => Humanoid(legs: false).Box(-2, 0, -1, 1, 4, 0, TeamDark);
    }

    /// <summary>
    /// The model a faction's units of a type are drawn with: the faction's own version ("orcs/spearman")
    /// where there is one, else the shared one.
    /// </summary>
    public static string ForFaction(string model, string faction) =>
        Has($"{faction}/{model}") ? $"{faction}/{model}" : model;

    public static bool Has(string model) => Make(model) != null;

    public static ArrayMesh Build(string model) => Mesh(Make(model) ?? new Builder().Humanoid());

    private static Builder? Make(string model) => model switch
    {
        "worker" => new Builder().Humanoid()
            .Box(3, 3, 0, 3, 11, 0, Wood).Box(2, 11, -1, 4, 11, 1, Steel),               // pickaxe
        "spearman" => new Builder().Humanoid().Cape(bottom: 3)
            .Box(-2, 13, -2, 1, 14, 1, Steel).Box(-1, 15, -2, 0, 15, 1, Team)            // helmet and crest
            .Box(3, 1, 1, 3, 17, 1, Wood).Box(3, 18, 1, 3, 20, 1, Steel)                 // spear
            .Box(-4, 5, -2, -4, 10, 1, TeamDark),                                        // shield
        "archer" => new Builder().Humanoid()
            .Box(-2, 13, -3, 1, 14, 1, TeamDark)                                         // hood
            .Bow(4, 14)
            .Box(0, 6, -2, 1, 11, -2, Wood),                                             // quiver
        "sapper" => new Builder().Humanoid()
            .Box(-2, 13, -2, 1, 14, 1, Steel).Box(-2, 13, -2, 1, 13, 1, Team)           // helmet with a band
            .Box(3, 4, 0, 3, 11, 0, Wood).Box(3, 1, -1, 3, 3, 1, Steel),                 // shovel
        "mage" => new Builder().Robe()
            .Box(-2, 14, -2, 1, 14, 1, TeamDark).Box(-1, 15, -1, 0, 16, 0, TeamDark).Box(-1, 17, -1, -1, 18, -1, Team)
            .Box(3, 0, 1, 3, 15, 1, Wood).Box(3, 16, 1, 3, 16, 1, Glow),
        "healer" => new Builder().Robe()
            .Box(-2, 13, -3, 1, 14, 1, Cloth).Box(-2, 10, -3, 1, 12, -3, Cloth)          // white hood
            .Box(-2, 6, 1, 1, 9, 1, Cloth).Box(-1, 6, 1, 0, 9, 1, Team).Box(-2, 8, 1, 1, 8, 1, Team) // apron with a cross
            .Box(-4, 4, -1, -4, 6, 0, Leather).Box(-4, 7, -1, -4, 7, 0, Leaf)            // satchel of herbs
            .Box(3, 0, 1, 3, 13, 1, Wood).Box(3, 14, 0, 3, 14, 2, Leaf).Box(3, 15, 1, 3, 15, 1, Lamp), // staff tied with leaves
        "rider" => new Builder().Mount(Horse)
            .Box(-3, 5, -4, 2, 9, 1, TeamDark)                                           // caparison down the flanks
            .Humanoid(lift: 6, legs: false)
            .Box(3, 12, 1, 3, 24, 1, Wood).Box(3, 25, 1, 3, 26, 1, Steel),               // lance

        // The Orcs' versions of the trained types: bigger, green, tusked, with crude bone and iron gear.
        "orcs/worker" => new Builder().Orc()
            .Box(3, 3, 0, 3, 12, 0, Wood).Box(2, 12, -1, 4, 13, 1, Rock)                 // stone pick
            .Box(-2, 6, -3, 1, 10, -2, Hide),                                            // hide sack on the back
        "orcs/spearman" => new Builder().Orc()
            .Box(-2, 15, -1, 1, 15, 2, Iron)                                             // horned helm
            .Box(-3, 15, 0, -3, 17, 0, Bone).Box(2, 15, 0, 2, 17, 0, Bone)
            .Box(3, 1, 1, 3, 20, 1, Wood).Box(3, 21, 1, 3, 23, 1, Bone)                  // bone-tipped spear
            .Box(-5, 4, -2, -5, 11, 2, TeamDark).Box(-6, 7, 0, -6, 8, 0, Bone),          // round shield with a boss
        "orcs/archer" => new Builder().Orc()
            .Box(-2, 15, -1, 1, 15, 2, Team).Box(1, 16, -1, 1, 18, -1, Bone)             // headband and a bone
            .Bow(2, 17, -5, Bone)                                                        // big horn bow
            .Box(0, 7, -2, 2, 13, -2, Hide),                                             // quiver
        "orcs/rider" => new Builder().WarBoar()
            .Box(-4, 4, -4, 3, 9, 1, TeamDark)                                           // hide cloth down the flanks
            .Orc(lift: 5, legs: false)
            .Box(-2, 20, -1, 1, 20, 2, Iron).Box(-3, 20, 0, -3, 22, 0, Bone).Box(2, 20, 0, 2, 22, 0, Bone)
            .Box(3, 8, 1, 3, 22, 1, Wood).Box(3, 19, 2, 3, 23, 4, Steel),                // war axe
        "orcs/sapper" => new Builder().Orc()
            .Box(-2, 15, -1, 1, 15, 2, Leather).Box(-2, 14, 2, 1, 14, 2, Lamp)           // cap and goggles
            .Box(3, 4, 0, 3, 13, 0, Wood).Box(3, 0, -1, 3, 3, 1, Iron)                   // big shovel
            .Box(-3, 6, -4, 2, 11, -2, Leather).Box(-1, 12, -3, -1, 13, -3, Fire),       // powder sack with a fuse
        "orcs/mage" => new Builder().Orc(legs: false)
            .Box(-3, 0, -1, 2, 4, 1, TeamDark)                                           // long hide skirt
            .Box(-2, 11, 2, 1, 14, 2, Bone).Box(-1, 13, 2, -1, 13, 2, Toxic).Box(0, 13, 2, 0, 13, 2, Toxic) // skull mask
            .Box(-3, 15, -1, 2, 15, 2, Team).Box(-3, 16, -1, -3, 18, -1, Team).Box(2, 16, -1, 2, 18, -1, Team).Box(-1, 16, -1, 0, 19, -1, Team) // feathered crown
            .Box(3, 0, 1, 3, 17, 1, Bone).Box(2, 18, 0, 4, 20, 2, Bone).Box(3, 19, 3, 3, 19, 3, Toxic), // bone staff with a skull
        "orcs/healer" => new Builder().Orc(legs: false)
            .Box(-3, 0, -1, 2, 4, 1, TeamDark)                                           // long hide skirt
            .Box(-2, 15, -1, 1, 15, 2, Moss).Box(-2, 16, 0, 1, 16, 1, Leaf)              // moss cap
            .Box(-4, 6, -1, -4, 9, 1, Hide).Box(-4, 10, 0, -4, 10, 0, Leaf)              // pouch of herbs
            .Box(3, 0, 1, 3, 16, 1, Wood).Box(2, 17, 0, 4, 18, 2, Leaf).Box(3, 19, 1, 3, 19, 1, Toxic), // branch staff

        // Settler forms.
        "foreman" => new Builder().Humanoid().Rank()
            .Box(-2, 13, -2, 1, 14, 1, Leather).Box(-2, 13, 2, 1, 13, 2, Leather)        // cap with a brim
            .Box(-2, 6, 1, 1, 9, 1, Leather)                                             // apron
            .Box(3, 3, 0, 3, 10, 0, Wood).Box(3, 10, -1, 3, 12, 2, Steel),               // hammer
        "militia" => new Builder().Humanoid().Rank()
            .Box(-2, 13, -2, 1, 13, 1, Leather)                                          // leather cap
            .Box(3, 1, 1, 3, 15, 1, Wood).Box(3, 16, 1, 3, 17, 1, Steel),                // short spear
        "knight" => new Builder().Humanoid().Rank().Cape(bottom: 1)
            .Box(-2, 10, -2, 1, 14, 1, Steel).Box(-2, 11, 1, 1, 11, 1, Dark)             // great helm and visor
            .Box(-1, 15, -2, 0, 17, 1, Team)                                             // plume
            .Box(-2, 7, 1, 1, 9, 1, Steel)                                               // breastplate
            .Box(-4, 9, -2, -3, 10, 1, Steel).Box(2, 9, -2, 3, 10, 1, Steel)             // broad pauldrons
            .Box(3, 4, 1, 3, 5, 1, Dark).Box(3, 6, 0, 3, 6, 2, Steel).Box(3, 7, 1, 3, 15, 1, Steel) // longsword
            .Box(-4, 4, -2, -4, 11, 2, TeamDark),                                        // big shield
        "pathfinder" => new Builder().Humanoid().Rank()
            .Box(-2, 13, -2, 1, 13, 1, Leaf).Box(-2, 13, 2, 1, 13, 2, Leaf)              // cap with a brim
            .Box(1, 14, -2, 1, 16, -2, Team)                                             // feather
            .Box(-2, 5, -2, 1, 10, -2, TeamDark)                                         // short cloak
            .Box(3, 1, 1, 3, 17, 1, Wood).Box(3, 18, 1, 3, 19, 1, Steel),                // spear
        "wolfheart" => new Builder().Humanoid().Rank()
            .Box(-2, 12, -3, 1, 14, 1, Fur).Box(-2, 14, 1, 1, 14, 2, Fur)               // wolf's head worn as a hood, snout over the brow
            .Box(-2, 15, -2, -2, 16, -2, DarkFur).Box(1, 15, -2, 1, 16, -2, DarkFur)    // ears
            .Box(-1, 13, 2, -1, 13, 2, Glow).Box(0, 13, 2, 0, 13, 2, Glow)              // the pelt's pale eyes
            .Box(-3, 4, -2, 2, 11, -2, Fur).Box(-3, 4, -3, 2, 6, -3, PaleFur)           // pelt down the back
            .Box(-3, 9, -2, 2, 9, 1, DarkFur)                                           // ruff over the shoulders
            .Box(3, 1, 1, 3, 17, 1, Wood).Box(3, 18, 1, 3, 20, 1, Steel).Box(3, 15, 0, 3, 15, 2, PaleFur), // spear with a fur tassel
        "battle_priest" => new Builder().Humanoid().Rank()
            .Box(-2, 13, -3, 1, 14, 1, Cloth)                                            // white hood
            .Box(-2, 6, 1, 1, 9, 1, Cloth).Box(-1, 6, 1, 0, 9, 1, Team).Box(-2, 8, 1, 1, 8, 1, Team) // tabard and cross
            .Box(3, 4, 1, 3, 10, 1, Wood).Box(3, 11, 0, 3, 13, 2, Steel),                // mace
        "siege_engineer" => new Builder().Humanoid().Rank()
            .Box(-2, 13, -2, 1, 13, 1, Leather)                                          // leather cap
            .Box(-2, 5, -5, 1, 10, -2, Wood).Box(-2, 7, -5, 1, 7, -2, Dark)              // crate of fire pots
            .Box(-1, 11, -4, -1, 11, -4, Fire).Box(0, 11, -3, 0, 11, -3, Fire)
            .Box(3, 3, -1, 4, 5, 0, Dark).Box(3, 6, -1, 3, 6, -1, Fire),                 // a lit pot in hand
        "longbowman" => new Builder().Humanoid().Rank()
            .Box(-2, 13, -3, 1, 14, 1, TeamDark)                                         // hood
            .Bow(1, 17)                                                                  // tall bow
            .Box(0, 6, -2, 1, 12, -2, Wood),                                             // quiver
        "ranger" => new Builder().Humanoid().Rank()
            .Box(-2, 13, -3, 1, 14, 1, Leaf).Box(-2, 5, -2, 1, 12, -2, Leaf)             // green hood and cloak
            .Bow(4, 14)
            .Box(0, 6, -3, 1, 11, -3, Wood),                                             // quiver over the cloak
        "lancer" => new Builder().Mount(Horse)
            .Box(-3, 5, 2, 2, 9, 5, Steel).Box(-1, 11, 7, 0, 12, 9, Steel)               // barding and chanfron
            .Box(-3, 4, -6, 2, 9, 1, TeamDark)                                           // long caparison
            .Humanoid(lift: 6, legs: false).Rank(6).Cape(6, 4)
            .Box(-2, 16, -2, 1, 20, 1, Steel).Box(-2, 17, 1, 1, 17, 1, Dark)             // helm and visor
            .Box(3, 12, 1, 3, 28, 1, Wood).Box(3, 29, 1, 3, 31, 1, Steel).Box(3, 12, 0, 3, 13, 2, Steel), // heavy lance
        "outrider" => new Builder().Mount(PaleHorse)
            .Box(-2, 8, -1, 1, 9, 0, TeamDark)                                           // light saddle cloth
            .Humanoid(lift: 6, legs: false).Rank(6)
            .Box(-2, 19, -2, 1, 19, 1, Leather)                                          // leather cap
            .Box(3, 12, 1, 3, 24, 1, Wood).Box(3, 25, 1, 3, 26, 1, Steel)
            .Box(3, 21, 2, 3, 23, 4, Team),                                              // pennant
        "tunneller" => new Builder().Humanoid().Rank()
            .Box(-2, 13, -2, 1, 14, 1, Steel).Box(-1, 13, 2, 0, 13, 2, Lamp)             // helmet with a lamp
            .Box(3, 3, 0, 3, 11, 0, Wood).Box(2, 11, -1, 4, 11, 1, Steel)                // pick
            .Box(-1, 4, -2, -1, 12, -2, Wood).Box(-2, 1, -2, 0, 3, -2, Steel),           // shovel on the back
        "demolisher" => new Builder().Humanoid().Rank()
            .Box(-2, 13, -2, 1, 14, 1, Steel)
            .Box(-2, 5, -5, 1, 10, -2, Dark).Box(-2, 6, -5, 1, 6, -2, Wood).Box(-2, 9, -5, 1, 9, -2, Wood) // powder keg
            .Box(-1, 11, -4, -1, 12, -4, Fire)                                           // fuse
            .Box(3, 4, 0, 3, 11, 0, Wood).Box(3, 1, -1, 3, 3, 1, Steel),                 // shovel
        "battle_mage" => new Builder().Robe().Rank()
            .Box(-3, 14, -3, 2, 14, 2, TeamDark).Box(-2, 15, -2, 1, 16, 1, TeamDark)     // wide hat
            .Box(-1, 17, -1, 0, 18, 0, Team).Box(0, 19, 0, 0, 20, 0, Team)
            .Box(-3, 9, -1, -3, 9, 0, Steel).Box(2, 9, -1, 2, 9, 0, Steel)               // pauldrons
            .Box(3, 0, 1, 3, 15, 1, Wood).Box(2, 16, 0, 4, 18, 2, Fire),                 // staff with a fire orb
        "seer" => new Builder().Robe().Rank()
            .Box(-2, 13, -3, 1, 14, 1, TeamDark).Box(-2, 10, -3, 1, 12, -3, TeamDark)    // deep hood
            .Box(-1, 11, 1, 0, 11, 1, Glow)                                              // shining eyes
            .Box(3, 0, 1, 3, 17, 1, Wood).Box(2, 18, 0, 4, 20, 2, Glow),                 // tall staff with a crystal

        // Monsters, and what taming them gives.
        "wolf" => new Builder().Wolf(Fur, PaleFur),
        "wolf_rider" => new Builder().Wolf(DarkFur, Fur, big: true)
            .Box(-3, 6, -4, 2, 9, 1, TeamDark)                                           // saddle cloth down the flanks
            .Orc(lift: 4, legs: false)
            .Box(-2, 19, -1, 1, 19, 2, Iron).Box(-3, 19, 0, -3, 21, 0, Bone).Box(2, 19, 0, 2, 21, 0, Bone) // horned helm
            .Box(3, 7, 1, 3, 24, 1, Wood).Box(3, 25, 1, 3, 27, 1, Bone),                 // and bone-tipped spear

        // The dusk tree's guardian: a hulking husk of grey bark half again a person's height, cyan light in
        // its cracks and eye sockets, a skull for a face under antler-like branches, and long bark claws.
        "dusk_warden" => new Builder()
            .Box(-4, 0, -2, -2, 7, 1, DuskBark).Box(1, 0, -2, 3, 7, 1, DuskBark)            // legs
            .Box(-4, 0, 2, -2, 0, 3, DuskBarkDark).Box(1, 0, 2, 3, 0, 3, DuskBarkDark)      // root toes
            .Box(-5, 8, -3, 4, 17, 2, DuskBark)                                             // trunk of a body
            .Box(-5, 8, -3, 4, 8, 2, DuskBarkDark).Box(-1, 10, 3, 0, 16, 3, DuskVein)       // a glowing crack down the chest
            .Box(-3, 12, 3, -3, 14, 3, DuskVein).Box(2, 9, 3, 2, 11, 3, DuskVein)
            .Box(-7, 12, -2, -6, 17, 1, DuskBarkDark).Box(5, 12, -2, 6, 17, 1, DuskBarkDark) // hunched shoulders
            .Box(-8, 4, -1, -7, 15, 0, DuskBark).Box(6, 4, -1, 7, 15, 0, DuskBark)          // long arms
            .Box(-8, 1, 0, -8, 3, 2, DuskBarkDark).Box(7, 1, 0, 7, 3, 2, DuskBarkDark)      // claws
            .Box(-8, 2, 2, -8, 2, 3, Bone).Box(7, 2, 2, 7, 2, 3, Bone)
            .Box(-3, 18, -2, 2, 22, 2, Bone)                                                // skull
            .Box(-2, 20, 3, -2, 20, 3, DuskVein).Box(1, 20, 3, 1, 20, 3, DuskVein)          // eyes
            .Box(-2, 18, 3, 1, 18, 3, Dark).Box(-1, 19, 3, 0, 19, 3, Dark)                  // teeth and nose
            .Box(-4, 22, 0, -4, 26, 0, DuskBarkDark).Box(-6, 25, 0, -5, 25, 0, DuskBarkDark).Box(-5, 27, 0, -5, 28, 0, DuskBarkDark) // antlers
            .Box(3, 22, 0, 3, 26, 0, DuskBarkDark).Box(4, 25, 0, 5, 25, 0, DuskBarkDark).Box(4, 27, 0, 4, 28, 0, DuskBarkDark)
            .Box(-5, 15, -3, 4, 15, -3, TeamDark),                                          // a sash, once it is someone's
        // A battle priest who healed the dusk tree: the Dusk Staff in hand, a gnarled grey staff crowned with
        // violet leaves round a cyan light, instead of the mace.
        "dusk_staff_bearer" => new Builder().Humanoid().Rank()
            .Box(-2, 13, -3, 1, 14, 1, Cloth)                                            // white hood
            .Box(-2, 6, 1, 1, 9, 1, Cloth).Box(-1, 6, 1, 0, 9, 1, Team).Box(-2, 8, 1, 1, 8, 1, Team) // tabard and cross
            .Box(-2, 6, -2, 1, 9, -2, DuskLeaf)                                          // a violet mantle
            .Box(3, 0, 1, 3, 17, 1, DuskBark).Box(3, 4, 1, 3, 5, 1, DuskVein).Box(3, 10, 1, 3, 11, 1, DuskVein) // a grey staff veined in cyan
            .Box(4, 15, 1, 4, 16, 1, DuskBarkDark).Box(2, 13, 1, 2, 14, 1, DuskBarkDark)
            .Box(1, 18, -1, 5, 21, 3, DuskLeaf)                                          // a violet bloom on top
            .Box(2, 19, -1, 4, 20, 3, DuskVein).Box(1, 19, 0, 5, 20, 2, DuskVein).Box(3, 22, 1, 3, 23, 1, DuskVein), // round a cyan light

        // Trade with the city-states: a pack mule with saddlebags in its owner's colour (what it carries goes on
        // its back), and the goblins' mercenaries.
        "caravan" => new Builder()
            .Box(-2, 4, -5, 1, 7, 3, Mule)                                               // body
            .Box(-2, 0, -5, -1, 3, -4, Mule).Box(0, 0, -5, 1, 3, -4, Mule)               // hind legs
            .Box(-2, 0, 2, -1, 3, 3, Mule).Box(0, 0, 2, 1, 3, 3, Mule)                   // forelegs
            .Box(-1, 6, 4, 0, 9, 5, Mule).Box(-1, 8, 6, 0, 9, 8, Mule)                   // neck and head
            .Box(-1, 8, 8, 0, 8, 8, Dark)                                                // nose
            .Box(-1, 10, 5, -1, 12, 5, Mule).Box(0, 10, 5, 0, 12, 5, Mule)               // long ears
            .Box(-1, 5, -6, 0, 6, -6, Dark)                                              // tail
            .Box(-2, 8, -3, 1, 8, 1, Leather)                                            // pack saddle
            .Box(-4, 4, -3, -3, 7, 1, TeamDark).Box(2, 4, -3, 3, 7, 1, TeamDark),        // saddlebags
        "goblin_cutthroat" => new Builder().Goblin()
            .Box(-2, 11, -2, 1, 11, 1, Dark)                                             // bandana
            .Box(3, 3, 1, 3, 4, 1, Dark).Box(3, 5, 1, 3, 8, 1, Steel)                    // long knife
            .Box(-4, 3, 0, -4, 6, 0, Steel),                                             // and a short one
        "goblin_slinger" => new Builder().Goblin()
            .Box(-2, 11, -2, 1, 12, 1, Leather)                                          // leather cap
            .Box(3, 4, 0, 3, 9, 0, Leather).Box(3, 10, -1, 3, 10, 1, Rock)               // sling and stone
            .Box(-2, 5, 1, -1, 6, 1, Leather),                                           // pouch of stones

        // A city-state's banner, at its market: a pole on a stone foot with a flag in its ally's colour.
        // A city-state's totem, drawn over the pole of its totem building a block at a time (see
        // UnitRenderer): carved bands with a stripe of the holder's colour, and on top a goblin head with
        // yellow eyes, a toothy mouth, long ears painted in the holder's colour and bone horns.
        "totem_band" => new Builder()
            .Box(-5, 0, -5, 4, 7, 4, Wood).Box(-5, 3, -5, 4, 4, 4, Team)
            .Box(-5, 1, -5, 4, 1, 4, Leather).Box(-5, 6, -5, 4, 6, 4, Leather),
        "totem_head" => new Builder()
            .Box(-5, 0, -5, 4, 7, 4, Wood).Box(-5, 0, -5, 4, 0, 4, Leather)
            .Box(-3, 4, 5, -2, 5, 5, GoblinEye).Box(1, 4, 5, 2, 5, 5, GoblinEye).Box(-1, 3, 5, 0, 4, 5, Leather)   // eyes, nose
            .Box(-3, 1, 5, 2, 2, 5, Dark).Box(-3, 2, 5, -3, 2, 5, Bone).Box(-1, 2, 5, -1, 2, 5, Bone).Box(1, 2, 5, 1, 2, 5, Bone)
            .Box(-3, 4, -6, -2, 5, -6, GoblinEye).Box(1, 4, -6, 2, 5, -6, GoblinEye).Box(-3, 1, -6, 2, 2, -6, Dark)  // the back face
            .Box(-9, 4, -1, -6, 6, 0, Team).Box(-8, 6, -1, -7, 7, 0, TeamDark)                                    // ears
            .Box(5, 4, -1, 8, 6, 0, Team).Box(6, 6, -1, 7, 7, 0, TeamDark)
            .Box(-4, 8, -1, -3, 10, 0, Bone).Box(2, 8, -1, 3, 10, 0, Bone).Box(-4, 11, -1, -4, 11, 0, Bone).Box(3, 11, -1, 3, 11, 0, Bone),

        "banner" => new Builder()
            .Box(-2, 0, -2, 2, 1, 2, Rock).Box(0, 2, 0, 0, 44, 0, Wood)
            .Box(1, 30, 0, 12, 43, 0, Team).Box(1, 30, 0, 12, 31, 0, TeamDark).Box(1, 42, 0, 12, 43, 0, TeamDark),

        // A lair's den: a mound of rocks two and a half blocks across (so that the pack around it stands
        // clear of it) with a dark way in on the +Z side, and bones.
        "den" => new Builder()
            .Box(-10, 0, -10, 9, 2, 9, DarkRock).Box(-8, 3, -8, 7, 5, 7, Rock)
            .Box(-6, 6, -6, 5, 7, 5, Rock).Box(-4, 8, -4, 3, 9, 3, Rock).Box(-4, 10, -4, 3, 10, -1, Moss)
            .Clear(-3, 0, 2, 2, 4, 10).Box(-3, 0, 1, 2, 4, 1, Dark)                      // the way in
            .Box(4, 0, 11, 7, 0, 11, Cloth).Box(-7, 0, 12, -7, 0, 15, Cloth).Box(-8, 0, 13, -6, 0, 13, Cloth), // bones

        // What a worker carries, on its back: tinted in the colour of the goods (see UnitRenderer.LoadColor).
        "load" => new Builder().Box(-2, 6, -4, 1, 10, -2, Team).Box(-2, 11, -4, 1, 11, -2, TeamDark),
        _ => null,
    };

    private static readonly Vector3I[] Dirs =
    {
        new(1, 0, 0), new(-1, 0, 0), new(0, 1, 0), new(0, -1, 0), new(0, 0, 1), new(0, 0, -1),
    };

    private static readonly Vector3[][] Quads =
    {
        new Vector3[] { new(1,0,0), new(1,1,0), new(1,1,1), new(1,0,1) },
        new Vector3[] { new(0,0,1), new(0,1,1), new(0,1,0), new(0,0,0) },
        new Vector3[] { new(0,1,0), new(0,1,1), new(1,1,1), new(1,1,0) },
        new Vector3[] { new(0,0,1), new(0,0,0), new(1,0,0), new(1,0,1) },
        new Vector3[] { new(1,0,1), new(1,1,1), new(0,1,1), new(0,0,1) },
        new Vector3[] { new(0,0,0), new(0,1,0), new(1,1,0), new(1,0,0) },
    };

    private static readonly float[] Shade = { 0.85f, 0.85f, 1f, 0.55f, 0.75f, 0.7f };

    private static ArrayMesh Mesh(Builder b)
    {
        var verts = new List<Vector3>();
        var normals = new List<Vector3>();
        var colors = new List<Color>();
        var indices = new List<int>();
        foreach (var (p, c) in b.Voxels)
        {
            for (int f = 0; f < 6; f++)
            {
                if (b.Voxels.ContainsKey(p + Dirs[f])) continue;
                int start = verts.Count;
                var shaded = c.A < 0.5f ? new Color(c.R * Shade[f], 0, 0, 0) : new Color(c.R * Shade[f], c.G * Shade[f], c.B * Shade[f], 1);
                foreach (var q in Quads[f])
                {
                    // Voxel coordinates are already centred on x/z, with the feet at y = 0.
                    verts.Add((new Vector3(p.X, p.Y, p.Z) + q) * VoxelSize);
                    normals.Add(new Vector3(Dirs[f].X, Dirs[f].Y, Dirs[f].Z));
                    colors.Add(shaded);
                }
                indices.AddRange(new[] { start, start + 2, start + 1, start, start + 3, start + 2 });
            }
        }
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Godot.Mesh.ArrayType.Max);
        arrays[(int)Godot.Mesh.ArrayType.Vertex] = verts.ToArray();
        arrays[(int)Godot.Mesh.ArrayType.Normal] = normals.ToArray();
        arrays[(int)Godot.Mesh.ArrayType.Color] = colors.ToArray();
        arrays[(int)Godot.Mesh.ArrayType.Index] = indices.ToArray();
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Godot.Mesh.PrimitiveType.Triangles, arrays);
        return mesh;
    }
}
