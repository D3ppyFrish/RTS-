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
    private static readonly Color OgreSkin = new(0.62f, 0.55f, 0.4f);
    private static readonly Color OgreSkinDark = new(0.48f, 0.42f, 0.3f);
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

        /// <summary>
        /// An ogre: half again a person's height and twice as broad, sallow and pot-bellied, with a small head
        /// thrust forward, tusks, long arms and a loincloth and shoulder pad in its owner's colour.
        /// </summary>
        public Builder Ogre() =>
            Box(-4, 0, -2, -1, 7, 1, OgreSkinDark).Box(1, 0, -2, 4, 7, 1, OgreSkinDark)  // legs
            .Box(-5, 0, -2, -1, 1, 2, Leather).Box(1, 0, -2, 5, 1, 2, Leather)          // wrapped feet
            .Box(-5, 8, -3, 5, 17, 2, OgreSkin)                                         // body
            .Box(-4, 9, 3, 4, 14, 3, OgreSkin)                                          // pot belly
            .Box(-5, 6, -3, 5, 9, 3, Team).Box(-5, 9, -3, 5, 9, 3, Leather)             // loincloth and belt
            .Box(-2, 4, 2, 2, 6, 2, TeamDark)                                           // its front flap
            .Box(-8, 7, -2, -6, 17, 1, OgreSkin).Box(6, 7, -2, 8, 17, 1, OgreSkin)      // long arms
            .Box(-8, 5, -2, -6, 6, 1, OgreSkinDark).Box(6, 5, -2, 8, 6, 1, OgreSkinDark) // fists
            .Box(-8, 16, -3, -4, 19, 2, TeamDark).Box(-8, 19, -2, -5, 19, 1, Iron)      // shoulder pad
            .Box(-3, 16, -1, 2, 21, 4, OgreSkin)                                        // head, low and forward
            .Box(-3, 20, 4, 2, 20, 4, OgreSkinDark)                                     // heavy brow
            .Box(-2, 19, 4, -2, 19, 4, Dark).Box(1, 19, 4, 1, 19, 4, Dark)              // eyes
            .Box(-2, 16, 5, -2, 17, 5, Bone).Box(1, 16, 5, 1, 17, 5, Bone)              // tusks
            .Box(-1, 17, 5, 0, 18, 5, OgreSkinDark)                                     // nose
            .Box(-1, 22, 0, 0, 22, 2, Hair);                                            // a tuft of hair

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
        // A wild boar: the orcs' war boar, with a collar in its owner's colour once it is someone's.
        "boar" => new Builder().WarBoar().Box(-2, 6, 4, 1, 7, 4, Team),
        // A stone giant: a hunched hulk of rock half again as tall as a house's door, moss on its shoulders,
        // glowing eyes and fists like boulders. The golem's sculpt (Detail/Sculpted) shows instead unless
        // detailed units are off.
        "stone_giant" => new Builder()
            .Box(-7, 0, -3, -3, 14, 3, Rock).Box(2, 0, -3, 6, 14, 3, Rock)                      // legs
            .Box(-8, 0, -3, -2, 2, 4, DarkRock).Box(1, 0, -3, 7, 2, 4, DarkRock)                // feet
            .Box(-8, 14, -5, 7, 20, 5, Rock)                                                    // hips
            .Box(-10, 21, -6, 9, 33, 7, Rock)                                                   // a barrel of a chest
            .Box(-11, 30, -8, 10, 35, 3, DarkRock)                                              // hunched back
            .Box(-7, 20, -6, 6, 21, 6, TeamDark)                                                // a sash, once it is someone's
            .Box(-12, 34, -5, -6, 37, 3, Leaf).Box(5, 34, -5, 11, 37, 3, Leaf)                  // moss on the shoulders
            .Box(-15, 18, -3, -11, 34, 3, Rock).Box(10, 18, -3, 14, 34, 3, Rock)                // arms
            .Box(-16, 10, -4, -10, 17, 4, DarkRock).Box(9, 10, -4, 15, 17, 4, DarkRock)         // fists
            .Box(-4, 33, 2, 3, 40, 9, Rock)                                                     // head, thrust forward
            .Box(-4, 38, 9, 3, 39, 9, DarkRock)                                                 // brow
            .Box(-3, 36, 10, -2, 36, 10, Fire).Box(1, 36, 10, 2, 36, 10, Fire),                 // eyes
        "wolf_rider" => new Builder().Wolf(DarkFur, Fur, big: true)
            .Box(-3, 6, -4, 2, 9, 1, TeamDark)                                           // saddle cloth down the flanks
            .Orc(lift: 4, legs: false)
            .Box(-2, 19, -1, 1, 19, 2, Iron).Box(-3, 19, 0, -3, 21, 0, Bone).Box(2, 19, 0, 2, 21, 0, Bone) // horned helm
            .Box(3, 7, 1, 3, 24, 1, Wood).Box(3, 25, 1, 3, 27, 1, Bone),                 // and bone-tipped spear

        // The dusk tree's guardian: a hulking husk of grey bark taller than a house's door, cyan light in its
        // cracks, a glowing heart behind bone ribs, a stag's skull for a face under branching antlers, and long
        // claws of root. Drawn here at the size of its sculpt (Detail/Sculpted.Warden), which is what shows
        // unless detailed units are off.
        "dusk_warden" => new Builder()
            .Box(-8, 0, -2, -5, 17, 2, DuskBark).Box(4, 0, -2, 7, 17, 2, DuskBark)                // legs
            .Box(-9, 0, -2, -4, 3, 3, DuskBark).Box(3, 0, -2, 8, 3, 3, DuskBark)                  // stumps
            .Box(-8, 0, 4, -6, 0, 7, DuskBarkDark).Box(5, 0, 4, 7, 0, 7, DuskBarkDark)            // root toes
            .Box(-11, 0, 0, -10, 0, 1, DuskBarkDark).Box(9, 0, 0, 10, 0, 1, DuskBarkDark)
            .Box(-7, 0, -6, -6, 0, -3, DuskBarkDark).Box(5, 0, -6, 6, 0, -3, DuskBarkDark)
            .Box(-7, 10, 3, -7, 13, 3, DuskVein).Box(6, 6, 3, 6, 9, 3, DuskVein)                  // cracks in the legs
            .Box(-7, 16, -5, 6, 23, 5, DuskBark)                                                  // hips
            .Box(-8, 24, -6, 7, 33, 7, DuskBark)                                                  // a barrel of a body
            .Box(-9, 30, -8, 8, 37, 4, DuskBark).Box(-6, 33, -9, 5, 37, -9, DuskBarkDark)         // hunched back
            .Box(-3, 25, 7, 2, 31, 7, DuskBarkDark).Box(-1, 27, 7, 0, 29, 7, DuskVein)            // the wound and its heart
            .Box(-3, 26, 8, 2, 26, 8, Bone).Box(-3, 28, 8, -2, 28, 8, Bone).Box(1, 28, 8, 2, 28, 8, Bone)
            .Box(-3, 30, 8, 2, 30, 8, Bone)                                                       // ribs
            .Box(-5, 32, 7, -4, 34, 7, DuskVein).Box(3, 21, 6, 3, 24, 6, DuskVein)
            .Box(-2, 31, -10, -2, 35, -10, DuskVein)
            .Box(-7, 21, -6, 6, 22, 6, TeamDark)                                                  // a sash, once it is someone's
            .Box(-11, 35, -4, -6, 38, 3, DuskLeaf).Box(5, 35, -4, 10, 38, 3, DuskLeaf)            // violet moss on the shoulders
            .Box(-10, 29, 2, -10, 34, 2, DuskLeaf).Box(9, 28, 2, 9, 34, 2, DuskLeaf)              // and vines
            .Box(-14, 22, -3, -11, 36, 3, DuskBark).Box(10, 22, -3, 13, 36, 3, DuskBark)          // upper arms
            .Box(-15, 12, -1, -12, 23, 5, DuskBark).Box(11, 12, -1, 14, 23, 5, DuskBark)          // forearms
            .Box(-15, 6, 2, -15, 11, 6, DuskBarkDark).Box(-13, 6, 2, -13, 11, 6, DuskBarkDark)     // claws
            .Box(14, 6, 2, 14, 11, 6, DuskBarkDark).Box(12, 6, 2, 12, 11, 6, DuskBarkDark)
            .Box(-15, 5, 6, -12, 5, 7, Bone).Box(11, 5, 6, 14, 5, 7, Bone)
            .Box(-4, 35, 3, 3, 42, 8, DuskBark)                                                   // hood
            .Box(-3, 36, 8, 2, 42, 11, Bone).Box(-2, 34, 10, 1, 37, 14, Bone)                     // skull and snout
            .Box(-2, 39, 11, -2, 39, 11, DuskVein).Box(1, 39, 11, 1, 39, 11, DuskVein)            // eyes
            .Box(-1, 35, 14, 0, 35, 14, Dark)
            .Box(-3, 43, 6, -3, 46, 6, DuskBarkDark).Box(-7, 46, 5, -4, 47, 5, DuskBarkDark)      // antlers
            .Box(-8, 48, 4, -8, 51, 4, DuskBarkDark).Box(-5, 48, 7, -5, 50, 8, DuskBarkDark)
            .Box(2, 43, 6, 2, 46, 6, DuskBarkDark).Box(3, 46, 5, 6, 47, 5, DuskBarkDark)
            .Box(7, 48, 4, 7, 51, 4, DuskBarkDark).Box(4, 48, 7, 4, 50, 8, DuskBarkDark)
            .Box(-9, 52, 3, -8, 53, 4, DuskLeaf).Box(7, 52, 3, 8, 53, 4, DuskLeaf)
            .Box(-6, 51, 8, -5, 51, 9, DuskLeaf).Box(4, 51, 8, 5, 51, 9, DuskLeaf),
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

        "ogre_brute" => new Builder().Ogre()
            .Box(7, 3, 2, 8, 16, 3, Wood)                                                // a tree-trunk club
            .Box(6, 16, 1, 9, 23, 4, Wood).Box(6, 19, 1, 9, 19, 4, Leather)             // its knotted head
            .Box(5, 22, 2, 5, 22, 2, Iron).Box(10, 18, 3, 10, 18, 3, Iron),             // and nails in it
        "ogre_hurler" => new Builder().Ogre()
            .Box(-3, 9, 4, 2, 14, 8, Rock).Box(-2, 10, 9, 1, 13, 9, DarkRock)           // a boulder held to its belly
            .Box(-4, 9, -6, 3, 16, -4, Leather)                                         // a sack on its back
            .Box(-3, 16, -6, -1, 18, -4, Rock).Box(1, 16, -6, 2, 17, -5, DarkRock),     // full of more rocks

        // A city-state's banner, at its market: a pole on a stone foot with a flag in its ally's colour.
        // A city-state's totem, drawn over the pole of its totem building a block at a time (see
        // UnitRenderer): carved bands with a stripe of the holder's colour, and on top a goblin head with
        // yellow eyes, a toothy mouth, long ears painted in the holder's colour and bone horns.
        "goblin_totem_band" => new Builder()
            .Box(-5, 0, -5, 4, 7, 4, Wood).Box(-5, 3, -5, 4, 4, 4, Team)
            .Box(-5, 1, -5, 4, 1, 4, Leather).Box(-5, 6, -5, 4, 6, 4, Leather),
        "goblin_totem_head" => new Builder()
            .Box(-5, 0, -5, 4, 7, 4, Wood).Box(-5, 0, -5, 4, 0, 4, Leather)
            .Box(-3, 4, 5, -2, 5, 5, GoblinEye).Box(1, 4, 5, 2, 5, 5, GoblinEye).Box(-1, 3, 5, 0, 4, 5, Leather)   // eyes, nose
            .Box(-3, 1, 5, 2, 2, 5, Dark).Box(-3, 2, 5, -3, 2, 5, Bone).Box(-1, 2, 5, -1, 2, 5, Bone).Box(1, 2, 5, 1, 2, 5, Bone)
            .Box(-3, 4, -6, -2, 5, -6, GoblinEye).Box(1, 4, -6, 2, 5, -6, GoblinEye).Box(-3, 1, -6, 2, 2, -6, Dark)  // the back face
            .Box(-9, 4, -1, -6, 6, 0, Team).Box(-8, 6, -1, -7, 7, 0, TeamDark)                                    // ears
            .Box(5, 4, -1, 8, 6, 0, Team).Box(6, 6, -1, 7, 7, 0, TeamDark)
            .Box(-4, 8, -1, -3, 10, 0, Bone).Box(2, 8, -1, 3, 10, 0, Bone).Box(-4, 11, -1, -4, 11, 0, Bone).Box(3, 11, -1, 3, 11, 0, Bone),

        // The ogres' idol, over its pole of stone: rough stone bands with a painted stripe, and on top a broad
        // ogre face with a heavy brow, glaring eyes, an underbite of tusks, a brow band in the holder's colour
        // and a pair of horns.
        "ogre_idol_band" => new Builder()
            .Box(-5, 0, -5, 4, 7, 4, Rock).Box(-5, 2, -5, 4, 2, 4, DarkRock).Box(-5, 5, -5, 4, 5, 4, DarkRock)
            .Box(-5, 3, -5, 4, 4, 4, Team).Box(-6, 0, -1, -6, 7, 0, DarkRock).Box(5, 0, -1, 5, 7, 0, DarkRock),
        "ogre_idol_head" => new Builder()
            .Box(-6, 0, -6, 5, 9, 5, Rock).Box(-6, 0, -6, 5, 0, 5, DarkRock)
            .Box(-6, 7, -6, 5, 8, 5, Team)                                                                        // brow band
            .Box(-5, 6, 6, 4, 6, 6, DarkRock)                                                                     // heavy brow
            .Box(-4, 4, 6, -2, 5, 6, Fire).Box(1, 4, 6, 3, 5, 6, Fire)                                            // glaring eyes
            .Box(-1, 3, 6, 0, 4, 7, DarkRock)                                                                     // nose
            .Box(-4, 1, 6, 3, 1, 6, Dark).Box(-4, 2, 6, -4, 3, 7, Bone).Box(3, 2, 6, 3, 3, 7, Bone)               // mouth and tusks
            .Box(-4, 4, -7, -2, 5, -7, Fire).Box(1, 4, -7, 3, 5, -7, Fire).Box(-4, 1, -7, 3, 1, -7, Dark)         // the back face
            .Box(-9, 8, -1, -7, 9, 0, Bone).Box(-10, 10, -1, -9, 13, 0, Bone)                                     // horns
            .Box(6, 8, -1, 8, 9, 0, Bone).Box(8, 10, -1, 9, 13, 0, Bone),

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
