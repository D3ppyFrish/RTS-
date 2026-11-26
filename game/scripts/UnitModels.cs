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
    private static readonly Color KoboldScale = new(0.66f, 0.32f, 0.2f);
    private static readonly Color KoboldBelly = new(0.82f, 0.58f, 0.36f);
    private static readonly Color OgreSkin = new(0.62f, 0.55f, 0.4f);
    private static readonly Color OgreSkinDark = new(0.48f, 0.42f, 0.3f);
    private static readonly Color TrollSkin = new(0.42f, 0.5f, 0.42f);
    private static readonly Color TrollSkinDark = new(0.3f, 0.37f, 0.31f);
    private static readonly Color OrcSkin = new(0.36f, 0.5f, 0.24f);
    private static readonly Color OrcEye = new(0.95f, 0.25f, 0.12f);
    private static readonly Color Bone = new(0.92f, 0.88f, 0.74f);
    private static readonly Color Silk = new(0.88f, 0.88f, 0.84f);
    private static readonly Color Hide = new(0.36f, 0.27f, 0.2f);
    private static readonly Color Boar = new(0.3f, 0.22f, 0.17f);
    private static readonly Color DeerCoat = new(0.62f, 0.42f, 0.29f);
    private static readonly Color DeerMane = new(0.45f, 0.32f, 0.23f);
    private static readonly Color DeerBelly = new(0.9f, 0.82f, 0.66f);
    private static readonly Color Antler = new(0.78f, 0.7f, 0.55f);
    private static readonly Color BogMud = new(0.27f, 0.25f, 0.2f);
    private static readonly Color BogSlime = new(0.38f, 0.3f, 0.42f);
    private static readonly Color Reed = new(0.45f, 0.5f, 0.25f);
    private static readonly Color BogLight = new(0.55f, 1f, 0.45f);
    private static readonly Color CrabShell = new(0.75f, 0.3f, 0.18f);
    private static readonly Color CrabDark = new(0.5f, 0.17f, 0.1f);
    private static readonly Color CrabBelly = new(0.9f, 0.75f, 0.55f);
    private static readonly Color MerScale = new(0.2f, 0.55f, 0.55f);
    private static readonly Color MerScaleDark = new(0.12f, 0.36f, 0.4f);
    private static readonly Color MerSkin = new(0.55f, 0.75f, 0.72f);
    private static readonly Color MerFin = new(0.35f, 0.72f, 0.8f);
    private static readonly Color Shell = new(0.93f, 0.8f, 0.72f);
    private static readonly Color ShellPink = new(0.88f, 0.55f, 0.52f);
    private static readonly Color Pearl = new(0.95f, 0.95f, 0.9f);
    // The dark power's corrupted beasts: black hide and stone veined with violet, and ember eyes.
    private static readonly Color BlightHide = new(0.12f, 0.09f, 0.14f);
    private static readonly Color BlightStone = new(0.2f, 0.16f, 0.24f);
    private static readonly Color BlightVein = new(0.62f, 0.2f, 0.85f);
    private static readonly Color BlightEye = new(1f, 0.32f, 0.1f);
    // The shadowed wilds' shade stalkers: blue-black fur, a paler grey-blue under it, and cold pale eyes.
    private static readonly Color ShadeFur = new(0.14f, 0.17f, 0.25f);
    private static readonly Color ShadePale = new(0.38f, 0.44f, 0.55f);
    private static readonly Color ShadeEye = new(0.7f, 0.92f, 1f);
    private static readonly Color Bristle = new(0.17f, 0.13f, 0.1f);
    private static readonly Color Iron = new(0.38f, 0.38f, 0.4f);
    private static readonly Color Toxic = new(0.55f, 1f, 0.3f);
    private static readonly Color DuskBark = new(0.34f, 0.33f, 0.39f);
    private static readonly Color DuskBarkDark = new(0.18f, 0.17f, 0.22f);
    private static readonly Color DuskVein = new(0.3f, 0.9f, 0.95f);
    private static readonly Color DuskLeaf = new(0.55f, 0.33f, 0.7f);
    private static readonly Color SpiderHair = new(0.24f, 0.18f, 0.13f);
    private static readonly Color SpiderDark = new(0.1f, 0.08f, 0.07f);
    private static readonly Color SpiderMark = new(0.86f, 0.78f, 0.55f);
    private static readonly Color SpiderEye = new(0.95f, 0.18f, 0.1f);
    private static readonly Color Chitin = new(0.78f, 0.76f, 0.7f);
    private static readonly Color ChitinDark = new(0.5f, 0.48f, 0.44f);
    private static readonly Color HiveChitin = new(0.27f, 0.22f, 0.2f);
    private static readonly Color HiveChitinDark = new(0.16f, 0.13f, 0.12f);
    private static readonly Color HiveEye = new(1f, 0.7f, 0.15f);
    private static readonly Color HiveAcidSac = new(0.45f, 0.55f, 0.18f);
    private static readonly Color HiveAcid = new(0.7f, 1f, 0.25f);
    private static readonly Color WyrmHide = new(0.3f, 0.29f, 0.34f);
    private static readonly Color WyrmBelly = new(0.55f, 0.52f, 0.5f);
    private static readonly Color Crystal = new(0.7f, 0.45f, 1f);
    private static readonly Color Hull = new(0.42f, 0.27f, 0.14f);
    private static readonly Color HullDark = new(0.3f, 0.19f, 0.1f);
    private static readonly Color Deck = new(0.62f, 0.47f, 0.28f);
    private static readonly Color Sailcloth = new(0.88f, 0.86f, 0.78f);
    private static readonly Color ElfSkin = new(0.93f, 0.82f, 0.7f);
    private static readonly Color ElfHair = new(0.88f, 0.8f, 0.52f);
    private static readonly Color ElfGreen = new(0.24f, 0.4f, 0.22f);
    private static readonly Color ElfGreenDark = new(0.16f, 0.28f, 0.15f);
    private static readonly Color Yew = new(0.42f, 0.24f, 0.12f);
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
        /// A kobold: smaller than a goblin, rust-scaled, with a dog's snout, little bone horns, glowing yellow eyes and a
        /// tail, in a loincloth and vest of its owner's colour.
        /// </summary>
        public Builder Kobold() =>
            Box(-2, 0, -1, -1, 2, 0, KoboldScale).Box(0, 0, -1, 1, 2, 0, KoboldScale)    // legs
            .Box(-2, 0, 1, -1, 0, 1, KoboldScale).Box(0, 0, 1, 1, 0, 1, KoboldScale)     // clawed feet
            .Box(-2, 3, -1, 1, 6, 0, Team).Box(-1, 4, 1, 0, 6, 1, KoboldBelly)           // vest and scaly front
            .Box(-2, 3, -1, 1, 3, 0, Leather)                                            // belt
            .Box(-3, 4, -1, -3, 6, 0, KoboldScale).Box(2, 4, -1, 2, 6, 0, KoboldScale)   // arms
            .Box(-2, 7, -2, 1, 9, 1, KoboldScale)                                        // head
            .Box(-1, 7, 2, 0, 8, 3, KoboldScale).Box(-1, 7, 3, 0, 7, 3, KoboldBelly)     // snout and jaw
            .Box(-1, 8, 4, 0, 8, 4, Dark)                                                // nose
            .Box(-2, 9, 1, -2, 9, 1, GoblinEye).Box(1, 9, 1, 1, 9, 1, GoblinEye)         // eyes
            .Box(-2, 10, -2, -2, 11, -2, Bone).Box(1, 10, -2, 1, 11, -2, Bone)           // horns
            .Box(-1, 2, -2, 0, 3, -2, KoboldScale).Box(-1, 1, -3, 0, 1, -4, KoboldScale) // tail
            .Box(0, 0, -5, 0, 0, -5, KoboldScale);

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

        /// <summary>
        /// A troll: as tall as an ogre but lean and hunched, grey-green with a stony, mossy back, a long drooping nose,
        /// a jutting jaw with tusks, small pale eyes, long arms with big hands and a hide kilt in its owner's colour.
        /// </summary>
        public Builder Troll() =>
            Box(-4, 0, -2, -2, 8, 0, TrollSkinDark).Box(1, 0, -2, 3, 8, 0, TrollSkinDark)   // long legs
            .Box(-5, 0, -2, -2, 0, 2, TrollSkinDark).Box(1, 0, -2, 4, 0, 2, TrollSkinDark)  // broad bare feet
            .Box(-4, 7, -2, 3, 10, 1, Team).Box(-4, 10, -2, 3, 10, 1, Hide)                 // kilt and belt
            .Box(-4, 11, -3, 3, 18, 1, TrollSkin)                                          // body, bent forward
            .Box(-4, 15, -4, 3, 19, -4, DarkRock).Box(-3, 16, -5, 2, 18, -5, Rock)          // stony back
            .Box(-2, 19, -4, 1, 19, -3, Moss).Box(-4, 14, -4, -3, 14, -4, Moss)             // moss on it
            .Box(-7, 8, -1, -5, 18, 1, TrollSkin).Box(5, 8, -1, 7, 18, 1, TrollSkin)        // long arms
            .Box(-8, 5, -1, -5, 7, 2, TrollSkinDark).Box(5, 5, -1, 8, 7, 2, TrollSkinDark)  // big hands
            .Box(-3, 17, 1, 2, 21, 4, TrollSkin)                                           // head, thrust forward
            .Box(-3, 21, 1, 2, 21, 3, TrollSkinDark)                                       // heavy brow
            .Box(-2, 20, 4, -2, 20, 4, Lamp).Box(1, 20, 4, 1, 20, 4, Lamp)                 // small pale eyes
            .Box(-1, 17, 5, 0, 20, 6, TrollSkinDark).Box(-1, 16, 6, 0, 16, 6, TrollSkinDark) // long drooping nose
            .Box(-3, 16, 2, 2, 16, 4, TrollSkinDark)                                       // jutting jaw
            .Box(-3, 17, 5, -3, 18, 5, Bone).Box(2, 17, 5, 2, 18, 5, Bone)                 // tusks
            .Box(-1, 22, 0, 0, 23, 2, Moss);                                               // a mossy tuft

        /// <summary>
        /// A centaur facing +Z: a horse's body and legs with a person's upper body rising from its front instead of a
        /// neck, bare-armed, with a sash in its owner's colour and a mane of hair down its back.
        /// </summary>
        public Builder Centaur(Color coat) =>
            Box(-2, 5, -6, 1, 9, 3, coat)                                                // horse body
            .Box(-2, 0, -6, -1, 4, -5, coat).Box(0, 0, -6, 1, 4, -5, coat)              // hind legs
            .Box(-2, 0, 2, -1, 4, 3, coat).Box(0, 0, 2, 1, 4, 3, coat)                  // forelegs
            .Box(-2, 0, 3, -1, 0, 3, Dark).Box(0, 0, 3, 1, 0, 3, Dark)                  // hooves
            .Box(-1, 6, -7, 0, 9, -7, Hair).Box(-1, 4, -8, 0, 6, -8, Hair)              // tail
            .Box(-2, 10, 1, 1, 14, 3, Skin)                                             // upper body
            .Box(-2, 10, 1, 1, 10, 3, coat)                                             // where it meets the horse
            .Box(-2, 12, 1, 1, 13, 3, Team).Box(-2, 11, 2, -1, 14, 2, TeamDark)         // sash
            .Box(-3, 11, 2, -3, 14, 2, Skin).Box(2, 11, 2, 2, 14, 2, Skin)              // bare arms
            .Box(-2, 15, 0, 1, 18, 3, Skin)                                             // head
            .Box(-2, 18, 0, 1, 18, 3, Hair).Box(-2, 16, 0, 1, 17, 0, Hair)              // hair
            .Box(-1, 12, 0, 0, 16, 0, Hair)                                             // mane down the back
            .Box(-1, 16, 3, -1, 16, 3, Dark).Box(0, 16, 3, 0, 16, 3, Dark);             // eyes

        /// <summary>
        /// A merfolk facing +Z: a pale sea-green person whose body runs down into a scaled fish tail instead of legs,
        /// coiled under it to stand on with its fluke curled up behind, a fin crest on the head, and a sash of its
        /// owner's colour over a scaled front.
        /// </summary>
        public Builder Merfolk() =>
            Box(-2, 0, -3, 1, 1, 2, MerScale)                                            // the tail coiled under it
            .Box(-2, 2, -1, 1, 4, 1, MerScale).Box(-1, 2, 2, 0, 4, 2, MerScaleDark)      // rising into the body
            .Box(-1, 1, -4, 0, 2, -5, MerScale).Box(-1, 3, -6, 0, 4, -6, MerScale)       // the tail's end curling up behind
            .Box(-2, 5, -7, 1, 6, -7, MerFin)                                            // its fluke
            .Box(-2, 5, -1, 1, 9, 0, MerSkin).Box(-1, 5, 1, 0, 8, 1, MerScale)           // body and scaled front
            .Box(-2, 6, -1, 1, 7, 0, Team).Box(-2, 5, -1, -1, 9, -1, TeamDark)           // sash
            .Box(-3, 6, -1, -3, 9, 0, MerSkin).Box(2, 6, -1, 2, 9, 0, MerSkin)           // arms
            .Box(-2, 10, -2, 1, 13, 1, MerSkin)                                          // head
            .Box(-1, 13, -2, 0, 15, 0, MerFin).Box(-3, 11, -1, -3, 12, 0, MerFin).Box(2, 11, -1, 2, 12, 0, MerFin) // fin crest and ear fins
            .Box(-2, 10, -2, 1, 12, -2, MerScaleDark)                                    // dark hair like weed down the back
            .Box(-1, 11, 1, -1, 11, 1, Dark).Box(0, 11, 1, 0, 11, 1, Dark);              // eyes

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

        /// <summary>
        /// An elf: a person's height, slimmer in the leg, pale, with long fair hair down the back and pointed ears
        /// sweeping up past the head, in leaf-green leggings and a tunic of its owner's colour. Hands at x = -3 and 2.
        /// </summary>
        public Builder Elf(bool legs = true)
        {
            if (legs)
            {
                Box(-2, 0, -1, -2, 4, 0, ElfGreen);
                Box(1, 0, -1, 1, 4, 0, ElfGreen);
                Box(-2, 0, 0, -2, 0, 1, Leather).Box(1, 0, 0, 1, 0, 1, Leather);      // soft boots
            }
            Box(-2, 5, -1, 1, 9, 0, Team);
            Box(-2, 5, -1, 1, 5, 0, ElfGreenDark);                               // belt
            Box(-2, 6, 1, 1, 6, 1, TeamDark).Box(-1, 7, 1, 0, 8, 1, ElfGreen);   // a leaf clasp on the tunic
            Box(-3, 6, -1, -3, 9, 0, TeamDark);                                  // arms
            Box(2, 6, -1, 2, 9, 0, TeamDark);
            Box(-3, 5, -1, -3, 5, 0, ElfSkin);                                   // hands
            Box(2, 5, -1, 2, 5, 0, ElfSkin);
            Box(-2, 10, -2, 1, 13, 1, ElfSkin);                                  // head
            Box(-2, 13, -2, 1, 13, 1, ElfHair);
            Box(-2, 9, -2, 1, 12, -2, ElfHair);                                  // long hair down the back
            Box(-3, 11, -1, -3, 13, -1, ElfSkin).Box(2, 11, -1, 2, 13, -1, ElfSkin); // pointed ears
            Box(-3, 14, -1, -3, 14, -1, ElfSkin).Box(2, 14, -1, 2, 14, -1, ElfSkin);
            Box(-1, 11, 1, -1, 11, 1, ElfGreenDark);                             // green eyes
            Box(0, 11, 1, 0, 11, 1, ElfGreenDark);
            return this;
        }

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
        // The Elves' own: slim, fair-haired, in leaf green with their owner's colour.
        // A glade warden: a leaf-bladed sword and a leaf-shaped shield, a green cloak.
        "glade_warden" => new Builder().Elf().Cape(bottom: 3)
            .Box(-2, 14, -2, 1, 14, 1, Steel).Box(-2, 13, -2, -2, 13, 1, Steel).Box(1, 13, -2, 1, 13, 1, Steel) // circlet helm
            .Box(2, 4, 1, 2, 5, 1, Gold).Box(2, 6, 1, 2, 12, 1, Steel).Box(2, 13, 1, 2, 13, 1, Steel)            // leaf blade
            .Box(-4, 5, -1, -4, 10, 1, ElfGreen).Box(-4, 6, 2, -4, 9, 2, ElfGreen).Box(-4, 7, -1, -4, 8, 1, Team), // leaf shield
        // An elf archer: a hood, a longbow as tall as she is, a quiver.
        "elf_archer" => new Builder().Elf()
            .Box(-2, 13, -3, 1, 14, 1, ElfGreenDark).Box(-1, 15, -2, 0, 15, -1, ElfGreenDark)                  // pointed hood
            .Bow(1, 16, x: -4, wood: Yew)
            .Box(0, 6, -2, 1, 12, -2, Leather).Box(0, 13, -2, 1, 13, -2, Cloth),                                 // quiver and fletchings
        // A druid: a long robe, a crown of leaves, and a crooked staff with green growing from its head.
        "druid" => new Builder().Elf(legs: false).Box(-2, 0, -1, 1, 4, 0, TeamDark).Box(-2, 0, 1, 1, 1, 1, TeamDark)
            .Box(-2, 9, 1, 1, 9, 1, ElfGreenDark)                                                             // mantle
            .Box(-2, 14, -2, 1, 14, 1, Leaf).Box(-2, 15, -2, -2, 15, -2, Leaf).Box(1, 15, 1, 1, 15, 1, Leaf)   // crown of leaves
            .Box(3, 0, 1, 3, 14, 1, Wood).Box(4, 14, 1, 4, 16, 1, Wood)                                       // crooked staff
            .Box(3, 16, 0, 5, 17, 2, Leaf).Box(4, 18, 1, 4, 18, 1, Lamp),
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

        // The Orcs' war drummer (War drums): a great hide drum slung on its belly, bone sticks raised, a crest of feathers.
        "war_drummer" => new Builder().Orc()
            .Box(-2, 15, -1, 1, 15, 2, TeamDark).Box(-1, 16, 0, 0, 18, 0, Team)           // band and feathers
            .Box(-3, 4, 3, 2, 10, 6, Wood).Box(-3, 5, 7, 2, 9, 7, Cloth)                // drum shell and its head
            .Box(-3, 4, 3, 2, 4, 6, Bone).Box(-3, 10, 3, 2, 10, 6, Bone)                // bone hoops
            .Box(-4, 6, 0, -4, 12, 0, Bone).Box(3, 6, 0, 3, 12, 0, Bone)                // drumsticks
            .Box(-4, 13, 0, -4, 13, 0, Hide).Box(3, 13, 0, 3, 13, 0, Hide),

        // The delver (Cave guides): a hooded guide with a lamp on its brow, a pack with a coil of rope and a crystal-tipped staff.
        "delver" => new Builder().Humanoid()
            .Box(-2, 13, -3, 1, 14, 1, DarkRock).Box(-2, 15, -2, 1, 15, 0, DarkRock)      // a dark hood
            .Box(-1, 14, 2, 0, 14, 2, Lamp)                                              // a lamp on its brow
            .Box(-2, 6, 1, 1, 9, 1, TeamDark)                                            // jerkin
            .Box(-2, 5, -3, 1, 10, -3, Leather).Box(-2, 8, -4, 1, 9, -4, Rock)           // pack with a coil of rope
            .Box(3, 1, 1, 3, 15, 1, Wood).Box(3, 16, 0, 3, 17, 2, Crystal),              // staff tipped with crystal

        // Settler forms.
        "foreman" => new Builder().Humanoid().Rank()
            .Box(-2, 13, -2, 1, 14, 1, Leather).Box(-2, 13, 2, 1, 13, 2, Leather)        // cap with a brim
            .Box(-2, 6, 1, 1, 9, 1, Leather)                                             // apron
            .Box(3, 3, 0, 3, 10, 0, Wood).Box(3, 10, -1, 3, 12, 2, Steel),               // hammer
        // A hero: a foreman in a gold-banded master's cap, a sash in its owner's colour, a builder's square and a
        // rolled plan on its back, and a gilded mallet, so its workers see whom to follow.
        "master_builder" => new Builder().Humanoid().Rank()
            .Box(-2, 13, -2, 1, 14, 1, Leather).Box(-3, 13, 2, 2, 13, 3, Leather)        // cap with a wide brim
            .Box(-2, 13, -2, 1, 13, 1, Gold).Box(-1, 15, -1, 0, 15, 0, Team)             // gold band, a tuft in its colour
            .Box(-2, 6, 1, 1, 9, 1, Leather).Box(-2, 9, -1, 1, 9, 1, Team)               // apron and sash
            .Box(-4, 9, -1, -3, 10, 1, Gold).Box(2, 9, -1, 3, 10, 1, Gold)               // gilded shoulder pieces
            .Box(-3, 5, -3, -3, 13, -3, Gold).Box(-3, 5, -3, 1, 5, -3, Gold)             // builder's square on its back
            .Box(-1, 8, -4, 2, 9, -3, Cloth).Box(2, 8, -4, 2, 9, -3, Team)               // rolled plan, ribbon in its colour
            .Box(3, 3, 0, 3, 10, 0, Wood).Box(3, 10, -1, 3, 12, 2, Gold),                // gilded mallet
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
        // A hero: a knight in gilded armour with a crown on the helm and a tall banner on its back, so the
        // army sees where it stands.
        "marshal" => new Builder().Humanoid().Rank().Cape(bottom: 1)
            .Box(-2, 10, -2, 1, 14, 1, Steel).Box(-2, 11, 1, 1, 11, 1, Dark)             // great helm and visor
            .Box(-2, 15, -2, 1, 15, 1, Gold)                                             // crown band
            .Box(-2, 16, -2, -2, 16, -2, Gold).Box(1, 16, -2, 1, 16, -2, Gold)
            .Box(-2, 16, 1, -2, 16, 1, Gold).Box(1, 16, 1, 1, 16, 1, Gold)               // crown points
            .Box(-2, 7, 1, 1, 9, 1, Steel).Box(-1, 8, 1, 0, 8, 1, Gold)                  // breastplate with a gold boss
            .Box(-4, 9, -2, -3, 10, 1, Gold).Box(2, 9, -2, 3, 10, 1, Gold)               // gilded pauldrons
            .Box(3, 4, 1, 3, 5, 1, Dark).Box(3, 6, 0, 3, 6, 2, Gold).Box(3, 7, 1, 3, 15, 1, Steel) // longsword
            .Box(-4, 4, -2, -4, 11, 2, TeamDark)                                         // big shield
            .Box(0, 4, -3, 0, 26, -3, Wood).Box(0, 27, -3, 0, 27, -3, Gold)              // banner pole on the back
            .Box(1, 19, -3, 6, 25, -3, Team).Box(1, 25, -3, 6, 25, -3, Gold)             // the banner
            .Box(3, 21, -3, 4, 23, -3, Gold),                                            // its device
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
        // A hero: a longbowman in a gilded hood with a gold circlet, a great bow, a full quiver and a short
        // pennant in its owner's colour on its back, so its archers see where to gather.
        "bowmaster" => new Builder().Humanoid().Rank()
            .Box(-2, 13, -3, 1, 14, 1, TeamDark).Box(-2, 13, 2, 1, 13, 2, Gold)          // hood with a gold rim
            .Box(-2, 15, -2, 1, 15, 1, Gold).Box(-1, 16, 1, 0, 16, 1, Gold)              // circlet and its peak
            .Box(-2, 7, 1, 1, 9, 1, Leather).Box(-1, 8, 1, 0, 8, 1, Gold)                // jerkin with a gold clasp
            .Bow(1, 18, wood: Gold)                                                      // great gilded bow
            .Box(0, 6, -2, 1, 12, -2, Wood).Box(0, 13, -2, 1, 13, -2, Steel)             // full quiver
            .Box(-2, 4, -3, -2, 20, -3, Wood).Box(-2, 21, -3, -2, 21, -3, Gold)          // pennant pole
            .Box(-1, 16, -3, 2, 20, -3, Team).Box(0, 18, -3, 1, 18, -3, Gold),           // the pennant
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
        // A hero: a lancer on a barded horse with gilded trim, a crested gold helm, a long cape and a
        // gilded lance flying a pennon in its owner's colour, so its riders see whom to follow.
        "horselord" => new Builder().Mount(Horse)
            .Box(-3, 5, 2, 2, 9, 5, Steel).Box(-3, 9, 2, 2, 9, 5, Gold)                  // barding with a gold rim
            .Box(-1, 11, 7, 0, 12, 9, Steel).Box(-1, 13, 8, 0, 13, 8, Gold)              // chanfron and its spike
            .Box(-3, 4, -6, 2, 9, 1, Team).Box(-3, 4, -6, 2, 4, 1, Gold)                 // caparison with a gold hem
            .Humanoid(lift: 6, legs: false).Rank(6).Cape(6, 5)
            .Box(-2, 16, -2, 1, 20, 1, Gold).Box(-2, 17, 1, 1, 17, 1, Dark)              // gold helm and visor
            .Box(-1, 21, -2, 0, 23, 1, Team)                                             // crest
            .Box(3, 12, 1, 3, 29, 1, Gold).Box(3, 30, 1, 3, 32, 1, Steel).Box(3, 12, 0, 3, 13, 2, Steel) // gilded lance
            .Box(3, 25, -3, 3, 28, 0, Team),                                             // pennon
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
        // A hero: a demolisher with a gold-banded helm and a lamp at its brow, a great powder keg bound in gold
        // on its back flying a pennant in its owner's colour, and a gilded war pick, so its sappers see whom to follow.
        "breachmaster" => new Builder().Humanoid().Rank()
            .Box(-2, 13, -2, 1, 14, 1, Steel).Box(-2, 13, -2, 1, 13, 1, Gold)            // helm with a gold band
            .Box(-1, 14, 2, 0, 14, 2, Lamp)                                              // lamp at the brow
            .Box(-2, 6, 1, 1, 9, 1, Leather).Box(-2, 9, -1, 1, 9, 1, Team)               // apron and sash
            .Box(-4, 9, -2, -3, 10, 1, Gold).Box(2, 9, -2, 3, 10, 1, Gold)               // gilded pauldrons
            .Box(-3, 4, -6, 2, 11, -2, Dark).Box(-3, 5, -6, 2, 5, -2, Gold).Box(-3, 10, -6, 2, 10, -2, Gold) // keg, gold hoops
            .Box(-1, 12, -5, -1, 13, -5, Fire)                                           // fuse
            .Box(1, 12, -5, 1, 20, -5, Wood).Box(1, 21, -5, 1, 21, -5, Gold)             // pennant pole
            .Box(2, 17, -5, 4, 20, -5, Team)                                             // the pennant
            .Box(3, 4, 1, 3, 13, 1, Wood).Box(3, 13, -1, 3, 13, 3, Gold).Box(3, 12, 3, 3, 12, 3, Gold), // gilded war pick
        "battle_mage" => new Builder().Robe().Rank()
            .Box(-3, 14, -3, 2, 14, 2, TeamDark).Box(-2, 15, -2, 1, 16, 1, TeamDark)     // wide hat
            .Box(-1, 17, -1, 0, 18, 0, Team).Box(0, 19, 0, 0, 20, 0, Team)
            .Box(-3, 9, -1, -3, 9, 0, Steel).Box(2, 9, -1, 2, 9, 0, Steel)               // pauldrons
            .Box(3, 0, 1, 3, 15, 1, Wood).Box(2, 16, 0, 4, 18, 2, Fire),                 // staff with a fire orb
        // A hero: a battle mage in a tall hat banded in gold, gilded mantle and a mantle-clasp in its owner's colour,
        // with a gold-shod staff crowned by a ring of gold round a shining crystal, so its casters see whom to follow.
        "archmage" => new Builder().Robe().Rank()
            .Box(-3, 14, -3, 2, 14, 2, TeamDark).Box(-2, 15, -2, 1, 17, 1, TeamDark)     // wide, tall hat
            .Box(-2, 15, -2, 1, 15, 1, Gold)                                             // gold hat band
            .Box(-1, 18, -1, 0, 19, 0, Team).Box(0, 20, 0, 0, 22, 0, Team).Box(0, 23, 0, 0, 23, 0, Gold)
            .Box(-3, 9, -2, 2, 10, -2, Team)                                             // mantle in its colour
            .Box(-4, 9, -1, -3, 10, 1, Gold).Box(2, 9, -1, 3, 10, 1, Gold)               // gilded pauldrons
            .Box(-1, 9, 1, 0, 9, 1, Glow)                                                // mantle clasp
            .Box(3, 0, 1, 3, 17, 1, Wood).Box(3, 0, 1, 3, 1, 1, Gold)                    // gold-shod staff
            .Box(2, 18, 0, 4, 18, 2, Gold).Box(2, 21, 0, 4, 21, 2, Gold)                 // gold ring
            .Box(3, 19, 1, 3, 20, 1, Glow).Box(2, 19, 1, 2, 20, 1, Fire).Box(4, 19, 1, 4, 20, 1, Fire), // crystal and flame
        "seer" => new Builder().Robe().Rank()
            .Box(-2, 13, -3, 1, 14, 1, TeamDark).Box(-2, 10, -3, 1, 12, -3, TeamDark)    // deep hood
            .Box(-1, 11, 1, 0, 11, 1, Glow)                                              // shining eyes
            .Box(3, 0, 1, 3, 17, 1, Wood).Box(2, 18, 0, 4, 20, 2, Glow),                 // tall staff with a crystal

        // Monsters, and what taming them gives.
        "wolf" => new Builder().Wolf(Fur, PaleFur),
        // A red deer: a long, slim russet body on tall thin legs, a darker maned neck reaching forward, a small head
        // with ears out to the sides, the pale rump patch and short tail, and a stag's wide branching antlers.
        "deer" => new Builder()
            .Box(-2, 7, -6, 1, 10, 4, DeerCoat)                                          // body
            .Box(-2, 7, -5, 1, 7, 3, DeerBelly)                                          // belly
            .Box(-1, 8, -7, 0, 10, -7, DeerBelly).Box(-1, 10, -8, 0, 10, -8, DeerCoat)   // rump patch and tail
            .Box(-2, 1, -5, -2, 6, -5, DeerCoat).Box(1, 1, -5, 1, 6, -5, DeerCoat)       // hind legs
            .Box(-2, 1, 3, -2, 6, 3, DeerCoat).Box(1, 1, 3, 1, 6, 3, DeerCoat)           // forelegs
            .Box(-2, 0, -5, -2, 0, -5, Dark).Box(1, 0, -5, 1, 0, -5, Dark)               // hooves
            .Box(-2, 0, 3, -2, 0, 3, Dark).Box(1, 0, 3, 1, 0, 3, Dark)
            .Box(-1, 10, 4, 0, 12, 5, DeerMane).Box(-1, 12, 5, 0, 13, 6, DeerMane)        // neck, reaching forward
            .Box(-1, 13, 6, 0, 14, 8, DeerCoat).Box(-1, 13, 9, 0, 13, 9, Dark)           // head, nose
            .Box(-2, 14, 7, -2, 14, 7, Dark).Box(1, 14, 7, 1, 14, 7, Dark)               // eyes
            .Box(-3, 15, 6, -2, 15, 6, DeerCoat).Box(1, 15, 6, 2, 15, 6, DeerCoat)       // ears, out to the sides
            .Box(-1, 15, 6, -1, 16, 6, Antler).Box(0, 15, 6, 0, 16, 6, Antler)           // antlers: beams
            .Box(-3, 17, 6, -2, 17, 6, Antler).Box(1, 17, 6, 2, 17, 6, Antler)
            .Box(-4, 18, 5, -4, 21, 5, Antler).Box(3, 18, 5, 3, 21, 5, Antler)
            .Box(-4, 19, 6, -4, 19, 7, Antler).Box(3, 19, 6, 3, 19, 7, Antler)           // brow tines
            .Box(-5, 22, 4, -3, 22, 5, Antler).Box(2, 22, 4, 4, 22, 5, Antler),          // crowns
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
        // A bog horror: a hunched heap of mud and violet slime taller than a man, reeds growing from its back,
        // long dragging arms with hooked claws, and a cold green lure of light hanging before its face.
        "bog_horror" => new Builder()
            .Box(-4, 0, -2, -2, 6, 1, BogMud).Box(1, 0, -2, 3, 6, 1, BogMud)                    // stumpy legs
            .Box(-5, 6, -4, 4, 15, 3, BogMud)                                                   // a heap of a body
            .Box(-4, 8, 3, 3, 13, 3, BogSlime).Box(-5, 14, -5, 4, 17, 1, BogSlime)              // slime on the front, a hunched back
            .Box(-6, 2, -1, -6, 14, 1, BogMud).Box(5, 2, -1, 5, 14, 1, BogMud)                  // long arms
            .Box(-7, 1, 0, -6, 2, 3, Dark).Box(5, 1, 0, 6, 2, 3, Dark)                          // hooked claws
            .Box(-3, 14, 1, 2, 18, 5, BogMud).Box(-3, 14, 5, 2, 14, 6, BogSlime)                // head, thrust forward, and a dripping jaw
            .Box(-2, 17, 5, -2, 17, 5, BogLight).Box(1, 17, 5, 1, 17, 5, BogLight)              // eyes
            .Box(-1, 19, 4, 0, 19, 7, Reed).Box(0, 15, 8, 0, 18, 8, Reed).Box(0, 14, 8, 0, 14, 8, BogLight) // the lure
            .Box(-4, 18, -4, -4, 23, -4, Reed).Box(-1, 18, -5, -1, 24, -5, Reed).Box(3, 18, -3, 3, 22, -3, Reed) // reeds
            .Box(-2, 7, -4, 1, 8, -4, TeamDark),                                                // a band, once it is someone's
        // A giant crab: a wide, low shell as big as a cart, red above and pale below, two great claws held up
        // in front and eyes on stalks.
        "giant_crab" => new Builder()
            .Box(-8, 4, -6, 7, 8, 5, CrabShell).Box(-6, 9, -5, 5, 10, 4, CrabShell)             // the shell
            .Box(-7, 3, -5, 6, 3, 4, CrabBelly)                                                 // its pale underside
            .Box(-5, 10, -3, -4, 10, 2, CrabDark).Box(3, 10, -3, 4, 10, 2, CrabDark)            // ridges
            .Box(-11, 0, -4, -9, 4, -3, CrabDark).Box(-11, 0, 0, -9, 4, 1, CrabDark)            // legs
            .Box(8, 0, -4, 10, 4, -3, CrabDark).Box(8, 0, 0, 10, 4, 1, CrabDark)
            .Box(-10, 0, 3, -9, 3, 3, CrabDark).Box(8, 0, 3, 9, 3, 3, CrabDark)
            .Box(-8, 5, 6, -6, 7, 9, CrabShell).Box(-9, 5, 10, -5, 9, 13, CrabShell)            // claws
            .Box(-9, 10, 12, -8, 11, 14, CrabDark)
            .Box(5, 5, 6, 7, 7, 9, CrabShell).Box(4, 5, 10, 8, 9, 13, CrabShell)
            .Box(7, 10, 12, 8, 11, 14, CrabDark)
            .Box(-3, 9, 5, -3, 12, 5, CrabDark).Box(2, 9, 5, 2, 12, 5, CrabDark)               // eye stalks
            .Box(-3, 13, 5, -3, 13, 5, Dark).Box(2, 13, 5, 2, 13, 5, Dark)
            .Box(-6, 8, -6, 5, 8, -6, TeamDark),                                                // a band, once it is someone's
        // A giant spider: a hairy brown body as big as a pony, a bulging abdomen with a pale cross on it, eight long
        // arched legs, pale fangs and a cluster of red eyes.
        "giant_spider" => new Builder()
            .Box(-5, 4, -12, 4, 11, -3, SpiderHair)                                             // abdomen
            .Box(-1, 12, -10, 0, 12, -5, SpiderMark).Box(-3, 12, -8, 2, 12, -7, SpiderMark)     // the pale cross on its back
            .Box(-1, 5, -3, 0, 6, -2, SpiderDark)                                               // waist
            .Box(-4, 4, -2, 3, 8, 4, SpiderHair)                                                // forebody
            .Box(-3, 5, 5, 2, 7, 5, SpiderDark)                                                 // face
            .Box(-2, 7, 6, -2, 7, 6, SpiderEye).Box(1, 7, 6, 1, 7, 6, SpiderEye)                // eyes
            .Box(-1, 8, 5, -1, 8, 5, SpiderEye).Box(0, 8, 5, 0, 8, 5, SpiderEye)
            .Box(-2, 2, 6, -1, 4, 7, SpiderMark).Box(0, 2, 6, 1, 4, 7, SpiderMark)              // fangs
            // eight legs, arched up and out from the forebody
            .Box(-10, 8, 3, -5, 9, 3, SpiderDark).Box(-12, 9, 3, -10, 10, 7, SpiderDark).Box(-13, 0, 7, -12, 9, 7, SpiderDark)
            .Box(4, 8, 3, 9, 9, 3, SpiderDark).Box(9, 9, 3, 11, 10, 7, SpiderDark).Box(11, 0, 7, 12, 9, 7, SpiderDark)
            .Box(-10, 8, 1, -5, 9, 1, SpiderDark).Box(-12, 9, 1, -10, 10, 3, SpiderDark).Box(-13, 0, 3, -12, 9, 3, SpiderDark)
            .Box(4, 8, 1, 9, 9, 1, SpiderDark).Box(9, 9, 1, 11, 10, 3, SpiderDark).Box(11, 0, 3, 12, 9, 3, SpiderDark)
            .Box(-10, 8, -1, -5, 9, -1, SpiderDark).Box(-12, 9, -1, -10, 10, -1, SpiderDark).Box(-13, 0, -1, -12, 9, -1, SpiderDark)
            .Box(4, 8, -1, 9, 9, -1, SpiderDark).Box(9, 9, -1, 11, 10, -1, SpiderDark).Box(11, 0, -1, 12, 9, -1, SpiderDark)
            .Box(-10, 8, -2, -5, 9, -2, SpiderDark).Box(-12, 9, -5, -10, 10, -2, SpiderDark).Box(-13, 0, -5, -12, 9, -5, SpiderDark)
            .Box(4, 8, -2, 9, 9, -2, SpiderDark).Box(9, 9, -5, 11, 10, -2, SpiderDark).Box(11, 0, -5, 12, 9, -5, SpiderDark)
            .Box(-4, 8, 0, 3, 8, 1, TeamDark),                                                  // a band, once it is someone's
        // A cave crawler: a pale, segmented thing the size of a dog, low to the ground on six legs, with
        // feelers, pincer jaws and small glowing eyes.
        "cave_crawler" => new Builder()
            .Box(-3, 2, -6, 2, 4, -2, Chitin).Box(-3, 2, -1, 2, 5, 3, Chitin)                   // two body segments
            .Box(-2, 5, -5, 1, 5, -3, ChitinDark).Box(-2, 6, 0, 1, 6, 2, ChitinDark)            // plates on the back
            .Box(-2, 2, 4, 1, 4, 6, Chitin)                                                     // head
            .Box(-2, 1, 7, -2, 2, 8, ChitinDark).Box(1, 1, 7, 1, 2, 8, ChitinDark)              // pincer jaws
            .Box(-2, 4, 7, -2, 4, 7, Glow).Box(1, 4, 7, 1, 4, 7, Glow)                          // eyes
            .Box(-3, 5, 6, -3, 5, 9, ChitinDark).Box(2, 5, 6, 2, 5, 9, ChitinDark)              // feelers
            .Box(-5, 0, -5, -4, 3, -5, ChitinDark).Box(3, 0, -5, 4, 3, -5, ChitinDark)          // six legs
            .Box(-5, 0, -1, -4, 3, -1, ChitinDark).Box(3, 0, -1, 4, 3, -1, ChitinDark)
            .Box(-5, 0, 3, -4, 3, 3, ChitinDark).Box(3, 0, 3, 4, 3, 3, ChitinDark)
            .Box(-1, 2, -7, 0, 3, -7, ChitinDark)                                               // tail stub
            .Box(-3, 3, -2, 2, 3, -2, TeamDark),                                                // a band, once it is someone's
        // The Deepfolk's hive insects: dark chitin with plates in their owner's colour, amber eyes.
        // A skitterling: a lean, low insect the size of a dog on six legs, with a pair of sickle claws held up in front.
        "skitterling" => new Builder()
            .Box(-2, 2, -6, 1, 4, -3, HiveChitin).Box(-2, 2, -2, 1, 4, 1, HiveChitin)           // abdomen and thorax
            .Box(-2, 5, -5, 1, 5, -4, Team).Box(-1, 5, -1, 0, 5, 0, TeamDark)                    // plates in its owner's colour
            .Box(-2, 2, 2, 1, 4, 4, HiveChitinDark)                                              // head
            .Box(-2, 4, 4, -2, 4, 4, HiveEye).Box(1, 4, 4, 1, 4, 4, HiveEye)                    // eyes
            .Box(-3, 4, 3, -3, 6, 5, HiveChitinDark).Box(2, 4, 3, 2, 6, 5, HiveChitinDark)       // sickle claws, raised
            .Box(-3, 6, 6, -3, 6, 7, Bone).Box(2, 6, 6, 2, 6, 7, Bone)
            .Box(-1, 5, 5, -1, 6, 7, HiveChitinDark).Box(0, 5, 5, 0, 6, 7, HiveChitinDark)       // feelers
            .Box(-4, 0, -4, -3, 2, -4, HiveChitinDark).Box(2, 0, -4, 3, 2, -4, HiveChitinDark)   // six legs
            .Box(-4, 0, -1, -3, 2, -1, HiveChitinDark).Box(2, 0, -1, 3, 2, -1, HiveChitinDark)
            .Box(-4, 0, 1, -3, 2, 1, HiveChitinDark).Box(2, 0, 1, 3, 2, 1, HiveChitinDark),
        // A spitter: a hive insect with a swollen, glowing acid sac for an abdomen, rearing up to spit.
        "spitter" => new Builder()
            .Box(-3, 2, -8, 2, 7, -3, HiveAcidSac).Box(-2, 3, -9, 1, 6, -9, HiveAcidSac)         // the swollen sac
            .Box(-3, 6, -7, 2, 7, -4, Team).Box(-3, 2, -3, 2, 2, -3, TeamDark)                   // plates in its owner's colour
            .Box(-2, 3, -2, 1, 6, 1, HiveChitin)                                                 // thorax, rearing
            .Box(-2, 6, 1, 1, 9, 3, HiveChitinDark)                                              // head, held high
            .Box(-2, 8, 3, -2, 8, 3, HiveEye).Box(1, 8, 3, 1, 8, 3, HiveEye)                    // eyes
            .Box(-1, 6, 4, 0, 7, 5, HiveAcid)                                                    // the spitting mouth
            .Box(-4, 0, -6, -3, 3, -6, HiveChitinDark).Box(2, 0, -6, 3, 3, -6, HiveChitinDark)   // six legs
            .Box(-4, 0, -2, -3, 3, -2, HiveChitinDark).Box(2, 0, -2, 3, 3, -2, HiveChitinDark)
            .Box(-4, 0, 1, -3, 4, 1, HiveChitinDark).Box(2, 0, 1, 3, 4, 1, HiveChitinDark),
        // A burrower: a broad, heavy digging insect, low and armoured, with two great shovel claws and a horned head.
        "burrower" => new Builder()
            .Box(-4, 2, -6, 3, 5, -1, HiveChitin).Box(-3, 2, 0, 2, 5, 2, HiveChitin)            // broad body
            .Box(-4, 6, -5, 3, 6, -2, Team).Box(-3, 6, 0, 2, 6, 1, TeamDark)                    // armour plates in its owner's colour
            .Box(-3, 1, 3, 2, 4, 5, HiveChitinDark)                                              // head, low
            .Box(-1, 5, 4, 0, 7, 5, Bone)                                                        // horn
            .Box(-3, 3, 6, -3, 3, 6, HiveEye).Box(2, 3, 6, 2, 3, 6, HiveEye)                    // eyes
            .Box(-6, 0, 3, -4, 2, 6, HiveChitinDark).Box(3, 0, 3, 5, 2, 6, HiveChitinDark)       // shovel claws
            .Box(-6, 0, 7, -4, 0, 7, Bone).Box(3, 0, 7, 5, 0, 7, Bone)
            .Box(-5, 0, -5, -4, 2, -5, HiveChitinDark).Box(3, 0, -5, 4, 2, -5, HiveChitinDark)   // hind legs
            .Box(-5, 0, -2, -4, 2, -2, HiveChitinDark).Box(3, 0, -2, 4, 2, -2, HiveChitinDark),
        // A cave wyrm: a long, blind serpent as big as a house, grey hide with crystals growing out of its back,
        // a wedge of a head with a jaw full of teeth and a crystal set in its skull.
        "cave_wyrm" => new Builder()
            .Box(-2, 0, -26, 1, 3, -21, WyrmHide)                                               // tail
            .Box(-4, 0, -21, 3, 6, -14, WyrmHide).Box(-5, 0, -14, 4, 9, -5, WyrmHide)           // coils, thickening
            .Box(-6, 0, -5, 5, 11, 5, WyrmHide)                                                 // the thickest part
            .Box(-5, 2, 5, 4, 12, 11, WyrmHide)                                                 // neck, rearing up
            .Box(-5, 0, -14, 4, 1, 11, WyrmBelly)                                               // pale belly
            .Box(-5, 9, 10, 4, 16, 18, WyrmHide)                                                // head
            .Box(-4, 8, 14, 3, 9, 19, WyrmBelly)                                                // lower jaw
            .Box(-4, 10, 18, -4, 11, 18, Bone).Box(-1, 10, 19, 0, 11, 19, Bone).Box(3, 10, 18, 3, 11, 18, Bone) // teeth
            .Box(-1, 17, 12, 0, 18, 14, Crystal)                                                // the crystal in its skull
            .Box(-1, 12, 0, 0, 15, 2, Crystal).Box(-3, 10, -6, -2, 13, -4, Crystal)             // crystals down its back
            .Box(1, 9, -10, 2, 12, -8, Crystal).Box(-1, 7, -17, 0, 9, -16, Crystal)
            .Box(-6, 6, -2, 5, 6, 2, TeamDark),                                                 // a band, once it is someone's
        // A shade stalker: a big, gaunt blue-black wolf of the wilds, with a ragged mane of grey-blue down its back,
        // long fangs and cold pale eyes.
        "shade_stalker" => new Builder().Wolf(ShadeFur, ShadePale, big: true)
            .Box(-1, 9, -4, 0, 9, 3, ShadePale).Box(-1, 10, -1, 0, 10, 2, ShadePale)            // ragged mane
            .Box(-2, 9, 7, -2, 9, 7, ShadeEye).Box(1, 9, 7, 1, 9, 7, ShadeEye)                  // pale eyes
            .Box(-1, 6, 10, -1, 7, 10, Bone).Box(0, 6, 10, 0, 7, 10, Bone),                      // long fangs
        // A blight hound: a big, lean black wolf with violet veins down its flanks, a ridge of spines and ember eyes.
        "blight_hound" => new Builder().Wolf(BlightHide, BlightStone, big: true)
            .Box(-2, 5, -4, -2, 5, 2, BlightVein).Box(1, 5, -4, 1, 5, 2, BlightVein)            // veins down the flanks
            .Box(-1, 9, -5, 0, 9, -5, BlightVein).Box(-1, 9, -2, 0, 10, -2, BlightVein)         // spines down the back
            .Box(-1, 9, 1, 0, 10, 1, BlightVein)
            .Box(-2, 9, 7, -2, 9, 7, BlightEye).Box(1, 9, 7, 1, 9, 7, BlightEye)                // ember eyes
            .Box(-1, 7, 10, 0, 7, 10, Bone),                                                     // bared fangs
        // A blight brute: a hunched hulk of black stone as tall as a door, split with glowing violet cracks, its
        // fists dragging, a shard of corrupt crystal through its back and ember eyes under a heavy brow.
        "blight_brute" => new Builder()
            .Box(-5, 0, -2, -2, 9, 2, BlightStone).Box(1, 0, -2, 4, 9, 2, BlightStone)           // legs
            .Box(-6, 9, -4, 5, 21, 4, BlightStone)                                               // body
            .Box(-7, 18, -5, 6, 22, 2, BlightHide)                                               // hunched back
            .Box(-5, 12, 4, -5, 19, 4, BlightVein).Box(1, 10, 4, 1, 16, 4, BlightVein)           // cracks
            .Box(-3, 15, 4, 3, 15, 4, BlightVein)
            .Box(-10, 6, -2, -7, 20, 2, BlightStone).Box(6, 6, -2, 9, 20, 2, BlightStone)        // long arms
            .Box(-11, 1, -3, -6, 6, 3, BlightHide).Box(5, 1, -3, 10, 6, 3, BlightHide)           // dragging fists
            .Box(-3, 20, 2, 2, 25, 7, BlightStone).Box(-3, 24, 7, 2, 25, 8, BlightHide)          // head and brow
            .Box(-2, 23, 8, -2, 23, 8, BlightEye).Box(1, 23, 8, 1, 23, 8, BlightEye)             // eyes
            .Box(-1, 22, -6, 0, 29, -5, BlightVein).Box(-3, 21, -6, -3, 25, -6, BlightVein),     // crystal shards
        // A blight horror: the dark power's own spawn, a towering thing of black stone and hide twice a man's
        // height on four thick legs, with a crown of violet crystal, a gaping maw of bone and a burning heart.
        "blight_horror" => new Builder()
            .Box(-9, 0, -9, -5, 12, -5, BlightStone).Box(4, 0, -9, 8, 12, -5, BlightStone)       // hind legs
            .Box(-9, 0, 4, -5, 14, 8, BlightStone).Box(4, 0, 4, 8, 14, 8, BlightStone)           // fore legs
            .Box(-10, 12, -11, 9, 28, 10, BlightHide)                                            // a vast body
            .Box(-8, 13, 10, 7, 26, 11, BlightStone)                                             // breast plates
            .Box(-2, 17, 11, 1, 22, 12, BlightEye)                                               // the burning heart
            .Box(-10, 20, -6, -10, 26, 6, BlightVein).Box(9, 20, -6, 9, 26, 6, BlightVein)       // veins down the flanks
            .Box(-6, 24, 10, 5, 34, 18, BlightStone)                                             // head
            .Box(-5, 22, 14, 4, 24, 19, Bone)                                                    // lower jaw
            .Box(-5, 25, 19, -4, 27, 19, Bone).Box(-1, 25, 19, 0, 27, 19, Bone).Box(3, 25, 19, 4, 27, 19, Bone) // fangs
            .Box(-4, 31, 18, -3, 32, 18, BlightEye).Box(2, 31, 18, 3, 32, 18, BlightEye)         // eyes
            .Box(-5, 35, 12, -4, 41, 13, BlightVein).Box(-1, 35, 13, 0, 43, 14, BlightVein)      // a crown of crystal
            .Box(3, 35, 12, 4, 41, 13, BlightVein)
            .Box(-6, 29, -8, -5, 36, -6, BlightVein).Box(4, 29, -4, 5, 37, -2, BlightVein)       // shards down its back
            .Box(-1, 29, 2, 0, 38, 4, BlightVein)
            .Box(-10, 13, -2, 9, 14, 2, TeamDark),                                               // a band, once it is someone's
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

        // Siege: a wooden catapult on four wheels, its throwing arm drawn back with a stone in the bucket, its sides
        // in its owner's colour.
        "catapult" => new Builder()
            .Box(-4, 2, -6, -3, 3, 6, Wood).Box(2, 2, -6, 3, 3, 6, Wood)                 // side beams
            .Box(-2, 2, -6, 1, 3, -5, Wood).Box(-2, 2, 5, 1, 3, 6, Wood)                 // cross beams
            .Box(-5, 0, -5, -5, 3, -3, Dark).Box(4, 0, -5, 4, 3, -3, Dark)               // hind wheels
            .Box(-5, 0, 3, -5, 3, 5, Dark).Box(4, 0, 3, 4, 3, 5, Dark)                   // front wheels
            .Box(-5, 1, -4, -5, 2, -4, Iron).Box(4, 1, -4, 4, 2, -4, Iron)               // hubs
            .Box(-5, 1, 4, -5, 2, 4, Iron).Box(4, 1, 4, 4, 2, 4, Iron)
            .Box(-3, 4, 1, -3, 10, 2, Wood).Box(2, 4, 1, 2, 10, 2, Wood)                 // uprights
            .Box(-3, 11, 1, 2, 11, 2, Wood)                                              // the bar the arm strikes
            .Box(-1, 4, -1, 0, 4, 1, Iron)                                               // the twisted rope it pivots on
            .Box(-1, 5, -8, 0, 5, -1, Wood)                                              // the arm, drawn back
            .Box(-2, 5, -10, 1, 6, -8, Leather).Box(-1, 7, -10, 0, 7, -9, Rock)          // the bucket and its stone
            .Box(-4, 4, -5, -4, 5, 4, Team).Box(3, 4, -5, 3, 5, 4, Team)                 // sides in its owner's colour
            .Box(-2, 4, 4, 1, 5, 5, TeamDark),                                           // and a front board

        // Siege: a battering ram, an iron-headed log slung on chains under a pitched roof of hides on four wheels, the
        // ridge and eaves in its owner's colour.
        "battering_ram" => new Builder()
            .Box(-4, 2, -7, -4, 3, 6, Wood).Box(3, 2, -7, 3, 3, 6, Wood)                 // side beams
            .Box(-5, 0, -5, -5, 3, -3, Dark).Box(4, 0, -5, 4, 3, -3, Dark)               // hind wheels
            .Box(-5, 0, 3, -5, 3, 5, Dark).Box(4, 0, 3, 4, 3, 5, Dark)                   // front wheels
            .Box(-5, 1, -4, -5, 2, -4, Iron).Box(4, 1, -4, 4, 2, -4, Iron)               // hubs
            .Box(-5, 1, 4, -5, 2, 4, Iron).Box(4, 1, 4, 4, 2, 4, Iron)
            .Box(-4, 4, -7, -4, 8, -7, Wood).Box(3, 4, -7, 3, 8, -7, Wood)               // corner posts
            .Box(-4, 4, 6, -4, 8, 6, Wood).Box(3, 4, 6, 3, 8, 6, Wood)
            .Box(-5, 9, -8, -4, 9, 7, Hide).Box(-3, 10, -8, -2, 10, 7, Hide)             // the roof of hides
            .Box(1, 10, -8, 2, 10, 7, Hide).Box(3, 9, -8, 4, 9, 7, Hide)
            .Box(-1, 11, -8, 0, 11, 7, Team)                                             // its ridge
            .Box(-5, 8, -8, -5, 8, 7, TeamDark).Box(4, 8, -8, 4, 8, 7, TeamDark)         // and eaves
            .Box(-1, 7, -4, 0, 10, -4, Iron).Box(-1, 7, 3, 0, 10, 3, Iron)               // the chains
            .Box(-1, 5, -9, 0, 6, 6, Wood)                                               // the log
            .Box(-2, 4, 7, 1, 7, 9, Iron),                                               // and its iron head

        // A ship (the harbor concept art's): a planked hull sitting low in the water with a pointed bow (+Z) and a square
        // stern, a deck those aboard stand on, and two masts with square sails banded in its owner's colour, high
        // enough to clear the heads of those on deck; a pennant at each masthead.
        "ship" => new Builder()
            .Box(-5, -5, -10, 4, -1, 10, Hull)                                           // the hull, under the deck
            .Box(-4, -6, -10, 3, -6, 9, HullDark)                                        // the keel side, mostly under water
            .Box(-4, -5, 11, 3, -1, 12, Hull).Box(-3, -4, 13, 2, -1, 14, Hull)           // the bow narrowing
            .Box(-2, -3, 15, 1, 0, 16, Hull).Box(-1, -1, 17, 0, 1, 18, HullDark)         // to a point and a short bowsprit
            .Box(-1, 1, 19, 0, 1, 21, Wood)
            .Box(-5, -5, -12, 4, 1, -11, HullDark)                                       // the stern, a little raised
            .Box(-4, -1, -10, 3, -1, 12, Deck).Box(-3, -1, 13, 2, -1, 14, Deck)          // the deck
            .Box(-5, 0, -10, -5, 1, 12, Team).Box(4, 0, -10, 4, 1, 12, Team)             // gunwales in its owner's colour
            .Box(-4, 0, 13, -4, 0, 14, TeamDark).Box(3, 0, 13, 3, 0, 14, TeamDark)
            .Box(-1, 0, -12, 0, 6, -12, Wood).Box(-1, 5, -14, 0, 5, -12, Wood)           // the rudder post and tiller
            .Box(-1, 0, -13, 0, 0, -13, Wood)
            .Box(0, 0, 6, 0, 34, 6, Wood).Box(0, 0, -6, 0, 30, -6, Wood)                 // fore and main masts (between the rows of seats)
            .Box(-7, 30, 6, 7, 30, 6, Wood).Box(-7, 18, 6, 7, 18, 6, Wood)               // yards
            .Box(-6, 19, 7, 6, 29, 7, Sailcloth).Box(-6, 23, 7, 6, 24, 7, Team)          // the foresail, banded
            .Box(-7, 26, -6, 7, 26, -6, Wood).Box(-7, 16, -6, 7, 16, -6, Wood)
            .Box(-6, 17, -5, 6, 25, -5, Sailcloth).Box(-6, 20, -5, 6, 21, -5, Team)      // the mainsail, banded
            .Box(0, 35, 6, 0, 35, 9, Team).Box(0, 31, -6, 0, 31, -3, Team)               // pennants
            .Box(-5, -3, 8, -5, -2, 9, Dark).Box(4, -3, 8, 4, -2, 9, Dark),              // hawse holes

        // A bombard ship: the ship's hull, broader and lower, with one mast and a square sail banded in its owner's
        // colour, and on the foredeck a squat iron bombard on a timber bed, its mouth over the bow.
        "bombard_ship" => new Builder()
            .Box(-6, -5, -10, 5, -1, 10, Hull)                                           // the hull, under the deck
            .Box(-5, -6, -10, 4, -6, 9, HullDark)                                        // the keel side, mostly under water
            .Box(-5, -5, 11, 4, -1, 12, Hull).Box(-4, -4, 13, 3, -1, 14, Hull)           // the bow narrowing
            .Box(-2, -3, 15, 1, 0, 16, Hull)
            .Box(-6, -5, -12, 5, 2, -11, HullDark)                                       // a raised stern castle
            .Box(-5, -1, -10, 4, -1, 12, Deck).Box(-4, -1, 13, 3, -1, 14, Deck)          // the deck
            .Box(-6, 0, -10, -6, 1, 12, Team).Box(5, 0, -10, 5, 1, 12, Team)             // gunwales in its owner's colour
            .Box(-1, 0, -12, 0, 6, -12, Wood).Box(-1, 5, -14, 0, 5, -12, Wood)           // the rudder post and tiller
            .Box(0, 0, -4, 0, 28, -4, Wood)                                              // the mast
            .Box(-8, 26, -4, 8, 26, -4, Wood).Box(-8, 14, -4, 8, 14, -4, Wood)           // yards
            .Box(-7, 15, -3, 7, 25, -3, Sailcloth).Box(-7, 19, -3, 7, 20, -3, Team)      // the sail, banded
            .Box(0, 29, -4, 0, 29, -1, Team)                                             // pennant
            .Box(-3, 0, 3, 2, 1, 10, Wood)                                               // the bombard's bed
            .Box(-2, 2, 4, 1, 4, 13, Iron).Box(-3, 2, 4, 2, 5, 6, Iron)                  // the barrel and its breech
            .Box(-1, 3, 13, 0, 3, 13, Dark),                                             // its mouth

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
        // A cart for hauling between storage: an ox pulling a plank cart on two big wheels, its load under a cloth in
        // its owner's colour.
        "cart" => new Builder()
            .Box(-2, 4, 3, 1, 7, 10, Mule)                                               // the ox's body
            .Box(-2, 0, 3, -1, 3, 4, Mule).Box(0, 0, 3, 1, 3, 4, Mule)                   // hind legs
            .Box(-2, 0, 9, -1, 3, 10, Mule).Box(0, 0, 9, 1, 3, 10, Mule)                 // forelegs
            .Box(-1, 5, 11, 0, 7, 13, Mule).Box(-1, 5, 14, 0, 5, 14, Dark)               // head and muzzle
            .Box(-3, 8, 11, -2, 8, 11, Bone).Box(1, 8, 11, 2, 8, 11, Bone)               // horns
            .Box(-2, 8, 8, 1, 8, 9, Wood)                                                // the yoke
            .Box(-1, 5, -1, -1, 5, 2, Wood).Box(0, 5, -1, 0, 5, 2, Wood)                 // the shafts
            .Box(-4, 4, -9, 3, 4, -1, Wood)                                              // the cart's bed
            .Box(-4, 5, -9, -4, 6, -1, Wood).Box(3, 5, -9, 3, 6, -1, Wood)               // its sides
            .Box(-4, 5, -9, 3, 6, -9, Wood)                                              // and tailboard
            .Box(-3, 5, -8, 2, 7, -2, Leather).Box(-3, 8, -7, 2, 8, -3, Team)            // the load under a cloth
            .Box(-5, 0, -7, -5, 4, -3, Dark).Box(4, 0, -7, 4, 4, -3, Dark)               // the wheels
            .Box(-5, 1, -8, -5, 3, -2, Dark).Box(4, 1, -8, 4, 3, -2, Dark),
        // A pack pony (Handcarts): small and quick, with a mane, panniers on both flanks and a cloth in its owner's colour.
        "pack_pony" => new Builder()
            .Box(-2, 4, -4, 1, 6, 3, Horse)                                              // body
            .Box(-2, 0, -4, -1, 3, -3, Horse).Box(0, 0, -4, 1, 3, -3, Horse)             // hind legs
            .Box(-2, 0, 2, -1, 3, 3, Horse).Box(0, 0, 2, 1, 3, 3, Horse)                 // forelegs
            .Box(-1, 6, 3, 0, 9, 4, Horse).Box(-1, 8, 5, 0, 9, 7, Horse)                 // neck and head
            .Box(-1, 8, 7, 0, 8, 7, Dark)                                                // nose
            .Box(-1, 10, 3, 0, 10, 4, Dark).Box(-1, 7, 2, 0, 9, 2, Dark)                 // mane
            .Box(-1, 3, -5, 0, 5, -5, Dark)                                              // tail
            .Box(-2, 7, -2, 1, 7, 1, Team)                                               // saddle cloth
            .Box(-4, 3, -3, -3, 6, 1, Leather).Box(2, 3, -3, 3, 6, 1, Leather)           // panniers
            .Box(-4, 6, -2, -3, 6, 0, Wood).Box(2, 6, -2, 3, 6, 0, Rock),                // planks and stone in them
        "goblin_cutthroat" => new Builder().Goblin()
            .Box(-2, 11, -2, 1, 11, 1, Dark)                                             // bandana
            .Box(3, 3, 1, 3, 4, 1, Dark).Box(3, 5, 1, 3, 8, 1, Steel)                    // long knife
            .Box(-4, 3, 0, -4, 6, 0, Steel),                                             // and a short one
        "goblin_slinger" => new Builder().Goblin()
            .Box(-2, 11, -2, 1, 12, 1, Leather)                                          // leather cap
            .Box(3, 4, 0, 3, 9, 0, Leather).Box(3, 10, -1, 3, 10, 1, Rock)               // sling and stone
            .Box(-2, 5, 1, -1, 6, 1, Leather),                                           // pouch of stones

        "kobold_digger" => new Builder().Kobold()
            .Box(-2, 10, -2, 1, 10, 1, Iron).Box(-1, 11, 0, 0, 11, 0, Lamp)              // helmet with a candle
            .Box(3, 3, 1, 3, 9, 1, Wood).Box(2, 9, 0, 4, 9, 2, Iron)                    // pick
            .Box(-3, 3, -3, 0, 6, -2, Leather).Box(-2, 6, -3, -1, 6, -3, Crystal),      // a sack with a crystal in it
        "kobold_spear" => new Builder().Kobold()
            .Box(3, 1, 1, 3, 12, 1, Wood).Box(3, 13, 1, 3, 14, 1, Steel)                // long spear
            .Box(-4, 3, -1, -4, 7, 2, Hide).Box(-4, 5, 0, -4, 5, 1, Team),              // hide shield

        "merfolk_trident" => new Builder().Merfolk()
            .Box(3, 1, 1, 3, 15, 1, Wood).Box(3, 16, 1, 3, 18, 1, Steel)                // the trident's shaft and middle tine
            .Box(2, 16, 1, 4, 16, 1, Steel).Box(2, 17, 1, 2, 18, 1, Steel).Box(4, 17, 1, 4, 18, 1, Steel) // its side tines
            .Box(-4, 4, -1, -4, 9, 2, Shell).Box(-4, 6, 0, -4, 7, 1, ShellPink)         // a shield of shell
            .Box(-2, 13, -2, 1, 13, 1, Shell),                                          // a shell cap
        "merfolk_harpooner" => new Builder().Merfolk()
            .Box(3, 8, -3, 3, 8, 8, Wood).Box(3, 8, 9, 3, 8, 10, Steel).Box(3, 7, 9, 3, 7, 9, Steel) // a barbed harpoon held ready
            .Box(-4, 5, -1, -3, 7, 1, Hair).Box(-4, 8, 0, -4, 8, 0, Hair)               // a coil of line at its side
            .Box(0, 9, -1, 1, 9, -1, Pearl),                                            // a string of pearls

        "centaur_archer" => new Builder().Centaur(Horse)
            .Box(-4, 11, 4, -4, 17, 4, Wood).Box(-4, 10, 3, -4, 10, 3, Wood).Box(-4, 18, 3, -4, 18, 3, Wood) // short horse bow
            .Box(-3, 12, 3, -3, 12, 3, Leather)                                         // its grip
            .Box(0, 10, -4, 1, 13, -3, Leather).Box(0, 14, -4, 1, 14, -3, Cloth)        // a quiver on its back, fletched
            .Box(-3, 7, -3, 2, 8, 0, TeamDark),                                         // a saddle cloth
        "centaur_lancer" => new Builder().Centaur(PaleHorse)
            .Box(3, 10, 4, 3, 24, 4, Wood).Box(3, 25, 4, 3, 27, 4, Steel)               // long lance
            .Box(-4, 10, 1, -4, 14, 4, Hide).Box(-4, 12, 1, -4, 12, 4, Team)            // hide shield with a stripe
            .Box(-2, 18, 0, 1, 19, 3, Iron)                                             // an iron cap
            .Box(-3, 7, -3, 2, 8, 0, TeamDark),                                         // a saddle cloth

        "troll" => new Builder().Troll()
            .Box(7, 4, 0, 7, 18, 0, Wood).Box(6, 15, -2, 9, 20, 2, Rock)                // a maul of stone on a pole
            .Box(6, 17, -2, 9, 17, 2, Hide)                                            // lashed on
            .Box(-8, 18, -2, -4, 20, 1, Hide).Box(-8, 20, -1, -6, 20, 0, Bone),         // a hide shoulder wrap and a bone

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

        // The giant spiders' totem at the bottom of their hill caves: a pole wound thick in grey-white silk, a wrapped bundle
        // (some catch) bound to it here and there, and on top a carved spider's head with eight red eyes, fangs and pale
        // egg sacs hung from its jaw.
        "spider_totem_band" => new Builder()
            .Box(-5, 0, -5, 4, 7, 4, Wood).Box(-6, 0, -6, 5, 2, 5, Silk).Box(-6, 5, -6, 5, 6, 5, Silk)
            .Box(-5, 3, -5, 4, 4, 4, Team).Box(4, 1, -2, 7, 5, 1, Silk).Box(-8, 3, 0, -5, 6, 3, Silk),
        "spider_totem_head" => new Builder()
            .Box(-6, 0, -6, 5, 7, 5, SpiderHair).Box(-6, 0, -6, 5, 1, 5, Silk)
            .Box(-4, 6, -3, 3, 8, 2, SpiderDark).Box(-1, 9, -2, 0, 9, 1, SpiderMark)                              // crown, mark
            .Box(-4, 5, 6, -3, 6, 6, SpiderEye).Box(2, 5, 6, 3, 6, 6, SpiderEye)                                   // eyes
            .Box(-2, 6, 6, -2, 6, 6, SpiderEye).Box(1, 6, 6, 1, 6, 6, SpiderEye)
            .Box(-5, 3, 6, -5, 3, 6, SpiderEye).Box(4, 3, 6, 4, 3, 6, SpiderEye).Box(-1, 4, 6, 0, 4, 6, SpiderEye)
            .Box(-3, -3, 6, -2, 2, 7, SpiderMark).Box(1, -3, 6, 2, 2, 7, SpiderMark)                              // fangs
            .Box(-9, -4, 2, -7, -1, 4, Silk).Box(6, -3, -3, 8, 0, -1, Silk)                                         // egg sacs
            .Box(-4, 5, -7, -3, 6, -7, SpiderEye).Box(2, 5, -7, 3, 6, -7, SpiderEye),                             // the back face

        // The merfolk's conch, over its pole of stone: bands crusted with shells and a strap of the holder's colour, and
        // on top a great spiralled conch shell, pale outside and pink in its mouth, hung with pearls.
        "merfolk_conch_band" => new Builder()
            .Box(-5, 0, -5, 4, 7, 4, Rock).Box(-5, 0, -5, 4, 0, 4, DarkRock).Box(-5, 7, -5, 4, 7, 4, DarkRock)
            .Box(-5, 3, -5, 4, 4, 4, Team).Box(-6, 5, 0, -6, 6, 1, Shell).Box(5, 1, -2, 5, 2, -1, ShellPink)
            .Box(1, 5, 5, 2, 6, 5, Shell),
        "merfolk_conch_head" => new Builder()
            .Box(-5, 0, -5, 4, 2, 4, Rock).Box(-5, 2, -5, 4, 2, 4, Team)                                          // the seat
            .Box(-4, 3, -4, 3, 7, 3, Shell).Box(-3, 8, -3, 2, 10, 2, Shell).Box(-2, 11, -2, 1, 12, 1, Shell)       // the spiralled shell
            .Box(-1, 13, -1, 0, 15, 0, Shell).Box(0, 16, 0, 0, 16, 0, ShellPink)                                  // its tip
            .Box(-4, 5, -4, 3, 5, 3, ShellPink).Box(-3, 9, -3, 2, 9, 2, ShellPink)                                // the spiral's ridges
            .Box(-2, 4, 4, 1, 9, 5, ShellPink).Box(-1, 5, 6, 0, 8, 6, Dark)                                       // its open mouth
            .Box(-6, 1, -1, -6, 3, -1, Pearl).Box(5, 1, 1, 5, 3, 1, Pearl).Box(-1, 1, 5, -1, 2, 5, Pearl),         // pearls hung from it

        // The kobolds' crystal, over its pole of stone: stone bands bound with a strap of the holder's colour, and on
        // top a great violet crystal in a stone cup, with smaller crystals round it.
        "kobold_crystal_band" => new Builder()
            .Box(-5, 0, -5, 4, 7, 4, Rock).Box(-5, 0, -5, 4, 0, 4, DarkRock).Box(-5, 7, -5, 4, 7, 4, DarkRock)
            .Box(-5, 3, -5, 4, 4, 4, Team).Box(-4, 5, 5, -3, 6, 5, Crystal).Box(2, 1, -6, 3, 2, -6, Crystal),
        "kobold_crystal_head" => new Builder()
            .Box(-5, 0, -5, 4, 2, 4, Rock).Box(-5, 2, -5, 4, 2, 4, Team)                                          // the cup
            .Box(-3, 3, -3, 2, 10, 2, Crystal).Box(-2, 11, -2, 1, 13, 1, Crystal).Box(-1, 14, -1, 0, 15, 0, Glow)  // the great crystal
            .Box(-6, 3, -1, -4, 7, 0, Crystal).Box(3, 3, -2, 5, 6, -1, Crystal)                                    // small ones round it
            .Box(-1, 3, 3, 0, 8, 5, Crystal).Box(0, 3, -6, 1, 5, -4, Crystal),

        // The trolls' standing stone, over its pole of stone: rough grey slabs with moss and a band in the holder's
        // colour, and on top a crude carved troll head, long-nosed and tusked, with a ring of bones hung under it.
        "troll_stone_band" => new Builder()
            .Box(-5, 0, -5, 4, 7, 4, Iron).Box(-5, 1, -5, 4, 1, 4, DarkRock).Box(-5, 6, -5, 4, 6, 4, DarkRock)
            .Box(-5, 3, -5, 4, 4, 4, Team).Box(-6, 2, 1, -6, 5, 2, Moss).Box(5, 0, -3, 5, 3, -2, Moss),
        "troll_stone_head" => new Builder()
            .Box(-5, 0, -5, 4, 10, 4, Iron).Box(-5, 0, -5, 4, 0, 4, DarkRock)
            .Box(-6, 1, -6, 5, 1, 5, Bone).Box(-6, 0, -6, -6, 0, -6, Bone).Box(5, 0, 5, 5, 0, 5, Bone)              // ring of bones
            .Box(-5, 9, -5, 4, 10, 4, Team)                                                                       // painted crown
            .Box(-4, 7, 5, 3, 8, 5, DarkRock)                                                                     // heavy brow
            .Box(-3, 6, 5, -2, 6, 5, Lamp).Box(1, 6, 5, 2, 6, 5, Lamp)                                            // pale eyes
            .Box(-1, 2, 5, 0, 6, 7, DarkRock).Box(-1, 1, 7, 0, 1, 7, DarkRock)                                    // the long nose
            .Box(-4, 1, 5, -3, 3, 6, Bone).Box(2, 1, 5, 3, 3, 6, Bone)                                            // tusks
            .Box(-3, 11, -2, 2, 11, 1, Moss).Box(-1, 12, -1, 0, 12, 0, Moss),                                     // moss on top

        // The centaurs' standard, over its pole of wood: bands bound with leather and a stripe of the holder's colour, and
        // on top a crossbar hung with long horse tails and a pennant in the holder's colour, under a gilt horseshoe.
        "centaur_standard_band" => new Builder()
            .Box(-5, 0, -5, 4, 7, 4, Wood).Box(-5, 1, -5, 4, 1, 4, Leather).Box(-5, 6, -5, 4, 6, 4, Leather)
            .Box(-5, 3, -5, 4, 4, 4, Team).Box(-6, 2, 0, -6, 5, 0, Hair).Box(5, 2, -1, 5, 5, -1, Hair),
        "centaur_standard_head" => new Builder()
            .Box(-2, 0, -2, 1, 12, 1, Wood).Box(-2, 0, -2, 1, 0, 1, Leather)                                      // the pole
            .Box(-9, 10, -1, 8, 11, 0, Wood)                                                                      // crossbar
            .Box(-9, 2, -1, -8, 9, 0, Hair).Box(7, 2, -1, 8, 9, 0, Hair).Box(-9, 1, -1, -9, 1, 0, Hair).Box(8, 1, -1, 8, 1, 0, Hair) // horse tails
            .Box(-6, 4, -1, -5, 9, 0, PaleFur).Box(4, 4, -1, 5, 9, 0, PaleFur)
            .Box(-3, 4, 2, 2, 9, 2, Team).Box(-3, 4, 2, 2, 4, 2, TeamDark).Box(-1, 3, 2, 0, 3, 2, TeamDark)        // pennant
            .Box(-3, 13, -1, 2, 13, 0, Gold).Box(-3, 14, -1, -3, 16, 0, Gold).Box(2, 14, -1, 2, 16, 0, Gold),     // gilt horseshoe

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
