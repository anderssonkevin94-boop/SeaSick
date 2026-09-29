using UnityEngine;

namespace SeaSick.Terrain
{
    /// All island-generation tuning. Lives in an asset, not on scene
    /// components, so changing a default here actually changes the world.
    /// Only the Noise section is consumed in step 1; the rest is declared now
    /// so the asset schema doesn't churn as the pipeline stages land.
    [CreateAssetMenu(menuName = "SeaSick/Terrain Settings", fileName = "TerrainSettings")]
    public class TerrainSettings : ScriptableObject
    {
        [Header("World")]
        public int seed = 1337;
        [Tooltip("Use the graphic art palette; changes vertex colours only, never terrain shape.")]
        public bool graphicArtPalette;
        public bool storybookLandforms;
        public bool individualTrees;
        [Tooltip("Water surface height. The whole ocean stack assumes 0.")]
        public float seaLevel = 0f;
        [Tooltip("0 = unbounded. Otherwise everything beyond this radius (metres from origin) is ocean.")]
        public float worldRadius = 0f;
        [Tooltip("Land fades out over this many metres inside worldRadius.")]
        public float worldEdgeFalloff = 500f;
        [Tooltip("Added to world XZ before sampling, so a chosen island can be slid under the home position without changing the seed.")]
        public Vector2 worldOffset = Vector2.zero;

        // Every number in these two blocks is also pushed into the asset by
        // TuneIslands.HomePush, and the asset is what actually runs. They are
        // kept in step by hand because there is no third place to keep them,
        // and Unity caches a field's default in the import artifact the first
        // time it sees it -- so editing an initialiser here after the asset
        // has the field changes nothing at all, silently. Push, then measure.
        [Header("Home isle (authored)")]
        [Tooltip("Replace the procedural land around the origin with an AUTHORED home island. The rest of the archipelago is untouched: this is a local override on the final height, blended out to open water, so worldOffset still chooses the world the player sails into and only stops choosing the island they live on.")]
        public bool homeIsle = true;
        [Tooltip("Where the island's centre sits in WORLD metres. It is absolute and does NOT move with worldOffset, because the ship spawns at the world origin and the whole point of authoring it is that the origin lands in the cove every time.")]
        public Vector2 homeIsleCentre = new Vector2(0f, 66f);
        [Tooltip("Mean radius of the land, metres. The island has to read WHOLE in the docked overview, which holds about 165 m of ground up the frame, so the extent is the constraint and not the area.")]
        public float homeIsleRadius = 74f;
        [Tooltip("Metres the coastline wanders either side of that radius. This is the level set of r - R(p), so the outline comes back organic and closed however much it wobbles.")]
        public float homeIsleShape = 11f;
        [Tooltip("Feature scale of the outline wobble, 1/metres. Near the radius gives two or three lobes; well above it only frays the sand.")]
        public float homeIsleShapeFrequency = 1f / 95f;
        [Tooltip("Height of the flat top, metres. Must clear sandHeight (so it reads grass, not beach) AND the village's own floor of sandHeight + 1.2, or the settlement has nowhere to stand.")]
        public float homeIsleTop = 5.2f;
        [Tooltip("Metres the flat top rolls either side of that, so it is a meadow rather than a table. Faded to nothing at the waterline so it cannot move the coastline.")]
        public float homeIsleRoll = 0.55f;
        public float homeIsleRollFrequency = 1f / 55f;
        [Tooltip("Metres of ground between the waterline and the flat top. Sets the width of the sand: sand runs from 0 to sandHeight, so at 34 m and a 5.2 m top the beach is about 21 m wide and the grassy bank above it about 13 m.")]
        public float homeIsleShoreRun = 28f;
        [Tooltip("Metres from the waterline out to seabedDepth. Steeper than a real sand island, and deliberately: the cove needs 2.5 m of water in its approach, and on a gentle shelf the only way to get it is to run a dead-straight gut a hundred metres out to sea -- a dredged channel on an island with nobody to dredge it. Nothing above the waterline moves with this, so the beach stays as wide as shoreRun says.")]
        public float homeIsleForeshore = 72f;
        [Tooltip("Metres from the centre out to where the authored field still wins outright. Everything inside this is home and nothing else.")]
        public float homeIsleFadeStart = 210f;
        [Tooltip("...and where the procedural world takes over again. Land the archipelago happens to put inside the band is drowned into a shoal, which is the intent: home stands alone in open water.")]
        public float homeIsleFadeEnd = 470f;

        [Header("Home isle cove")]
        [Tooltip("Which way the cove opens, degrees, in the same convention Island uses: 0 = +Z, 90 = +X, 180 = -Z.")]
        [Range(0f, 360f)] public float homeIsleCoveBearing = 180f;
        [Tooltip("How far along that bearing, from the centre, the cove's mouth reaches. It sits OUTSIDE the shoreline, far enough out that the open foreshore has already reached the 2.5 m the approach needs -- stop it short and the cove is a pool behind a bar, which is precisely what HarbourSite refuses to berth her in.")]
        public float homeIsleCoveMouth = 102f;
        [Tooltip("...and how far in the head of it is. Below the plateau radius (radius - shoreRun) the cove cuts into the flat top, which is where a harbour village wants to stand.")]
        public float homeIsleCoveHead = 34f;
        [Tooltip("Half-width at the mouth, metres -- the THROAT. Narrower than the basin on purpose: it is the pinch between the two horns that makes the water behind it quiet, and shelter is a third of what HarbourSite scores a berth on.")]
        public float homeIsleCoveHalfMouth = 13f;
        [Tooltip("Half-width at the head, metres, where it closes into a landing beach.")]
        public float homeIsleCoveHalfHead = 20f;
        [Tooltip("Half-width at the widest, metres -- the basin she lies in. Bounded by the island, not by taste: the land left between the cove flank and the outer coast is radius - sqrt(basinAlong^2 + halfBasin^2), and past about 18 m here the horns stop being spits and start being a breach.")]
        public float homeIsleCoveHalfBasin = 24f;
        [Tooltip("How far along the axis the basin is widest, metres from the centre.")]
        public float homeIsleCoveBasin = 46f;
        [Tooltip("Metres from the cove's own waterline out to full depth. Short, because deep water close in is the whole reason to dock here rather than on the beach -- and it is UNDERWATER, so it costs nothing above the sand and it is what decides how much of the basin she can actually float in.")]
        public float homeIsleCoveBank = 10f;
        [Tooltip("Depth of the pool, metres (negative). HarbourSite wants 3 m under her whole length at the berth and 2.5 m all the way in, so this carries both with a margin for the swell that still gets in.")]
        public float homeIsleCoveFloor = -5.4f;

        [Header("Noise (fBm)")]
        [Range(1, 8)] public int octaves = 5;
        [Tooltip("Base frequency in 1/metres. 1/400 ≈ features ~400 m across.")]
        public float baseFrequency = 1f / 400f;
        public float lacunarity = 2f;
        [Range(0f, 1f)] public float gain = 0.5f;

        [Header("Erosion")]
        [Tooltip("How hard each octave is damped by the steepness of the coarser ones. This is what carves spurs and gullies instead of stacking blobs. 0 = plain fBm, bit for bit.")]
        public float erosion = 2f;
        [Tooltip("How much of it reaches the terrain, faded in by how far inland a spot is. At the shore the field is EXACTLY plain fBm, so every beach slope and coastline measurement is untouched.")]
        [Range(0f, 1f)] public float erosionAmount = 1f;

        [Header("Island mask")]
        [Tooltip("Continentalness frequency, much lower than the base noise. This is ISLAND SIZE, and because landRatio sets the threshold as a quantile, raising it shrinks the islands and makes proportionally more of them for free -- total land is unchanged.")]
        public float maskFrequency = 1f / 1000f;
        [Range(1, 4)] public int maskOctaves = 3;
        [Range(0.01f, 0.6f), Tooltip("Fraction of the world that is land. 0.12 = sparse islands.")]
        public float landRatio = 0.15f;
        [Tooltip("Width of the mask's soft edge, in mask-noise units, so islands fade into the sea. In NOISE units, not metres, so it scales with maskFrequency on its own: shrink the islands and the beach fringe shrinks with them.")]
        [Range(0.01f, 0.5f)] public float maskFalloff = 0.1f;

        [Header("Island shape")]
        [Tooltip("How many times longer than wide an island runs. 1 = round lobed blobs, which is what plain fBm gives you. Each field stretches along a CONSTANT axis (a per-point rotation shreds the field at range); regionalGrain blends three such axes so the grain still turns from region to region.")]
        [Range(1f, 4f)] public float maskStretch = 2.2f;
        [Tooltip("Which way the grain runs, degrees.")]
        [Range(0f, 180f)] public float maskGrainAngle = 34f;
        [Tooltip("Metres the coastline is displaced by before the mask is read. Applied AFTER the stretch, so it bends and re-aims the elongated shapes rather than being stretched with them -- this is what stops every island pointing the same way. Its finer octaves are the bays, spits and hooks. 0 = smooth blobs.")]
        public float maskWarp = 110f;
        [Tooltip("Warp feature scale. Near the mask frequency re-aims whole islands; well above it only frays the coast.")]
        public float maskWarpFrequency = 1f / 900f;

        [Header("Island variety")]
        [Tooltip("Give each REGION of the sea its own grain instead of one grain for the whole world. The stretch is read along three axes 60 degrees apart and a slow selector field picks between them, so neighbouring islands still share a grain (it reads as geology) while the next group over runs another way, and islands on a boundary come out bent or crossed. Blended between whole fields, never rotated per point, so it cannot shred or seam at range.")]
        public bool regionalGrain = true;
        [Tooltip("Size of those regions, 1/metres. Well below maskFrequency, so an island almost always sits inside one region.")]
        public float grainRegionFrequency = 1f / 2600f;
        [Range(5f, 55f), Tooltip("Degrees of the selector's circle over which two grains blend. Wider = more bent, crossed islands; narrower = crisper regions.")]
        public float grainBlendDegrees = 25f;
        [Range(1f, 2.5f), Tooltip("How much SMALLER the islands are near home: the mask frequency is multiplied by this inside nearIslandRadius. 1 = the same size everywhere. Land ratio is untouched, so near home there are more islands, each smaller -- lots of close, quick things to find first, the big ones further out.")]
        public float nearIslandScale = 1.5f;
        [Tooltip("Metres from the world origin (home) out to where the near-home island size still holds.")]
        public float nearIslandRadius = 900f;
        [Tooltip("...and where full-size islands take over. Between the two the fields are blended, not rescaled, so nothing in the band is squashed.")]
        public float farIslandRadius = 2200f;
        [Tooltip("Shelf floor height around land, metres (negative). This is the depth every island's shore profile was tuned against.")]
        public float seabedDepth = -12f;
        [Tooltip("Open-ocean floor height, metres (negative). Must be deeper than the deepest storm trough, or the sea clips through the seafloor.")]
        public float deepSeabedDepth = -180f;
        [Tooltip("How far OUTSIDE the land threshold the shelf reaches, in mask-noise units. Bigger = a wider shelf and a continental slope further offshore.")]
        [Range(0.01f, 0.3f)] public float shelfBand = 0.12f;

        [Header("Skerries")]
        [Tooltip("Extra small islands scattered through open water. 0 = off. This is a LIFT on the continentalness noise, so everything downstream -- shelf depth, shoreline position, beach profile -- stays consistent; raising the mask alone would have put spires straight up out of deep ocean.")]
        [Range(0f, 1f)] public float skerryAmount = 0.30f;
        [Tooltip("Feature scale. Smaller number = larger, rarer islets.")]
        public float skerryFrequency = 1f / 620f;
        [Tooltip("How far up its own distribution the field has to climb before it lifts anything. Higher = fewer islets.")]
        [Range(0f, 1f)] public float skerryThreshold = 0.72f;
        [Tooltip("Skerries fade out this far (in mask units) below the land threshold, so they never fuse onto an existing coast.")]
        [Range(0.01f, 0.4f)] public float skerryClearance = 0.12f;
        [Tooltip("Metres of relief an islet gets. An islet is a SANDBANK, not a sea stack: left on the world's normal relief a 50 m islet came out as a 100 m needle at 5% walkable, because nothing in the pipeline knows a landmass is small.")]
        public float skerryRelief = 14f;

        [Header("Relief")]
        [Tooltip("Normalised noise (0..1) → normalised relief (0..1). This is the island's PROFILE, not its height: metres come from baseHeight + reliefHeight * massif. Steps in it read as benches and cliffs; the overall climb is what gives an island a peak instead of a tabletop.")]
        public AnimationCurve profileCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);

        [Tooltip("Height of the lowest land, metres above sea level.")]
        public float baseHeight = 3f;
        [Tooltip("Relief at the COAST, metres. Kept at the old curve ceiling so every shoreline profile the beach work was measured against is unchanged; the massif multiplier grows the interior only.")]
        public float reliefHeight = 60f;

        [Header("Massif")]
        [Tooltip("Per-island height character. Low frequency so neighbouring islands differ: 1/5200 m.")]
        public float massifFrequency = 1f / 5200f;
        [Tooltip("Interior height multiplier at the flattest islands.")]
        public float massifMin = 0.55f;
        [Tooltip("Interior height multiplier at the tallest. 4 x 60 m = a 240 m massif, ten ship-lengths.")]
        public float massifMax = 4f;
        [Tooltip("Bias on the massif noise. Above 1 makes big islands RARE, which is what makes one a landmark rather than the norm.")]
        public float massifBias = 1.6f;
        [Tooltip("Island mask value at which the interior starts growing. Below this the coast keeps reliefHeight exactly, so the shore profile is untouched.")]
        [Range(0.1f, 0.9f)] public float massifMaskStart = 0.35f;

        [Header("Upland / lowland")]
        [Tooltip("Where the mountains ARE, within an island. Low = plains and valleys, high = full relief. 1/700 m so a peak is a few hundred metres across and an island has room for both.")]
        public float uplandFrequency = 1f / 700f;
        [Tooltip("Noise value where ground starts becoming upland. Below it, pure lowland.")]
        public float uplandStart = 0.46f;
        [Tooltip("Noise value where relief is at FULL height. A power bias was tried first and was wrong: it shaved a bit off everywhere, so mountains lost half their height (median island peak 107 m -> 50 m) while the plains were still not properly flat. A band makes the choice binary — mountains are rare AND full size, plains are properly plains.")]
        public float uplandFull = 0.72f;
        [Tooltip("Relief on the lowlands, in METRES — the vertical range a plain spreads over. Absolute, not a fraction of the mountain: as a fraction it scaled with the massif, so a small island's interior collapsed below the sand line and the whole island rendered as beach. Slopes scale with this, so it is the main lever on how much of an island can be walked and built on.")]
        public float plainRelief = 45f;
        [Tooltip("Detail-noise multiplier on the lowlands. The 25 m bump layer alone puts a slope of about 0.24 on flat ground — steeper than the 10 degrees a building needs — so it has to be damped where people are meant to live.")]
        [Range(0f, 1f)] public float lowlandDetail = 0.25f;

        [Header("Ridges")]
        [Tooltip("How much of the highland relief comes from ridged noise instead of fBm. 0 = the old rounded blobs, 1 = fully ridged.")]
        [Range(0f, 1f)] public float ridgeAmount = 0.85f;
        [Tooltip("Normalised height where ridges start appearing. Below it the field is pure fBm, which keeps every coastline exactly as it was.")]
        public float ridgeLow = 0.42f;
        [Tooltip("Normalised height where ridges are at full strength.")]
        public float ridgeHigh = 0.62f;

        [Header("Rock")]
        [Tooltip("Island-scale: how ROCKY an island is. Low frequency so it is near-constant across one landmass and only changes out in the water between them.")]
        public float rockCharacterFrequency = 1f / 6000f;
        [Tooltip("Bias on rockiness. Above 1 makes most islands soft ground and a rocky one an event -- the same rule the massif uses about peaks.")]
        public float rockBias = 2.2f;
        [Tooltip("Crag scale, metres between outcrops. Ridged noise, so this is the size of the shapes that break out.")]
        public float rockFrequency = 1f / 140f;
        [Range(1, 5)] public int rockOctaves = 3;
        [Tooltip("Metres of rock standing PROUD of the soil at full protrusion.")]
        public float rockRelief = 34f;
        [Tooltip("On the softest islands, how high the ridged field must climb before rock breaks the surface at all. RidgedRaw has mean 0.26, sd 0.21 -- so 0.80 is rare.")]
        [Range(0f, 1f)] public float rockThresholdSoft = 0.80f;
        [Tooltip("And on the rockiest. Lower means rock breaks out more often.")]
        [Range(0f, 1f)] public float rockThresholdHard = 0.44f;

        [Header("Vegetation character")]
        [Tooltip("Island-scale: how GREEN an island is. Same reasoning as rockiness -- slow enough that one landmass sits inside one value.")]
        public float verdancyFrequency = 1f / 5800f;
        [Tooltip("Bias on verdancy. 1 is an even spread of lush and bare islands; below 1 pushes the spread toward lush. The measured spread at 0.7 had a median of 0.37 and a maximum of 0.75, so NO island in the world was as green as the reference board's 0.90.")]
        public float verdancyBias = 0.45f;
        [Tooltip("Even the barest island keeps this much scrub, so nothing comes out as a dead grey disc.")]
        [Range(0f, 1f)] public float verdancyFloor = 0.25f;
        [Tooltip("How strongly rockiness suppresses green ISLAND-WIDE. Kept small on purpose: the reference crag is rock 0.90 AND verdancy 0.90 -- ridges breaking out of a closed wood, which is the picture. At 0.65 the rockiest islands came back the barest (measured verdancy 0.37 on the r386 crag), so the one thing worth photographing was the one thing with no trees on it. Rock still clears trees where it is actually PROUD, through vegRockSuppress, which is the right place for it.")]
        [Range(0f, 1f)] public float verdancyRockSuppress = 0.15f;
        [Tooltip("Slope where the wood starts thinning. NOT an altitude: the references have green nearly to the summit on gentle ground and bare stone at the waterline on a steep face.")]
        public float vegSlopeSoft = 0.34f;
        [Tooltip("Slope where nothing grows at all.")]
        public float vegSlopeHard = 0.70f;
        [Tooltip("How much PROUD ROCK kills trees. Rock that has broken through is stone, not soil.")]
        [Range(0f, 1f)] public float vegRockSuppress = 0.88f;

        [Header("Flora (the Blender kit)")]
        [Tooltip("Metres between scenery trees. The kit's 13 m spruce carries a 4.5 m crown, so 5 m closes the canopy into a wood -- which is what the reference boards are made of.")]
        public float treeSpacing = 5.0f;
        [Tooltip("World Z south of which islands turn tropical (palms, sandbanks). North is cold, south is hot; home sits at 0 and is temperate.")]
        public float palmLatitude = -1400f;
        [Tooltip("Half-width of the temperate-to-tropical blend, metres.")]
        public float palmBand = 600f;
        [Tooltip("Scenery cells nearer the camera than this draw their full mesh (~200 tris a tree); beyond it the cheap one (~80).")]
        public float sceneryLod0Distance = 220f;

        [Header("Stands, glades and fields")]
        [Tooltip("Overall thickness of the wood, as a multiplier on every tree's chance. The one knob for \"too many trees\" / \"too few\" -- it scales the whole scatter without touching the patchiness, the spacing or the species.")]
        [Range(0.1f, 2f)] public float treeDensity = 0.8f;
        [Tooltip("Metres of grass a tree needs under it above where SAND STOPS (TerrainChunkMesher.SandBlend, 2.5 m above sandHeight). Trees must never stand on sand: the old margin was 1.2 m, which is INSIDE the sand-to-grass blend, so half the wood on a low island had its feet in the beach.")]
        public float sandTreeMargin = 0.6f;
        [Tooltip("On a tropical island, the chance a palm is allowed to stand down on the sand itself. Palms are the one tree that belongs on a beach -- but rarely, or the beach stops being a beach.")]
        [Range(0f, 0.3f)] public float palmOnSand = 0.06f;
        [Tooltip("Share of temperate islands that are BROADLEAF woods rather than conifer. One species per island: mixing them reads as one confused species rather than as two (Kevin, 2026-09-11).")]
        [Range(0f, 1f)] public float broadleafIslands = 0.4f;

        [Tooltip("How big a stand is, as a fraction of the island's mean radius (clamped to 34-140 m). A FRACTION and not a frequency on purpose: at a fixed wavelength an island narrower than one lobe of the field sits at a single value and comes back uniformly open or uniformly closed -- the same trap uplandFrequency was in at 1/700. At 0.55 every island gets about two stands across it whatever its size.")]
        [Range(0.2f, 1.2f)] public float standSpan = 0.55f;
        [Tooltip("How hard the cover field is pushed away from its mean. 1 is the raw noise, which is a gentle wobble and reads as nothing; above 2 the island separates into closed stands and real glades. The MEAN is renormalised afterwards, so this changes the patchiness and not the amount of wood.")]
        public float standContrast = 2.7f;
        [Tooltip("What a glade keeps. Zero would be bald ground, which reads as a bug rather than as a clearing -- the scrub goes in the gap instead, so this can stay low.")]
        [Range(0f, 1f)] public float standFloor = 0.13f;
        [Tooltip("How far the scatter lattice is warped, as a fraction of the spacing. The jitter inside a cell cannot hide the grid because every tree still belongs to its own cell; a slow warp of the whole lattice moves neighbours TOGETHER, which is what breaks the rows.")]
        [Range(0f, 2f)] public float treeWarp = 1.5f;
        [Tooltip("Chance a failed tree roll puts a bush there instead, at its strongest in the middle of a glade. This is what keeps open ground from being a lawn.")]
        [Range(0f, 1f)] public float scrubChance = 0.45f;

        [Tooltip("Steepest ground anybody would sow. Well under the wood's own limit -- a crop needs ground you can walk a scythe across.")]
        public float fieldSlopeMax = 0.17f;
        [Tooltip("Metres above the sand where fields stop. Crops are lowland; a wheat field on a shoulder at 40 m reads as decoration.")]
        public float fieldMaxRise = 26f;
        [Tooltip("Metres between wheat mats. The kit's mat is 2.5 m across, so under that they overlap and the field closes -- which is most of what separates a crop from a meadow.")]
        public float cropSpacing = 1.95f;
        [Tooltip("How much of the world is farmed at all. This is a per-island roll biased by this number: at 0.35 most islands have no wheat worth the name and a few are properly worked, which is the variation that makes finding a farmed one mean something.")]
        [Range(0f, 1f)] public float farmedShare = 0.35f;

        [Header("Rock formations")]
        [Tooltip("Multiplier on every outcrop. The formations were built for the ground BETWEEN trees; above 1 they start reading as landform -- crags you sail past rather than stones you walk round.")]
        [Range(0.5f, 4f)] public float cragScale = 1.0f;
        [Tooltip("Chance an inland outcrop is a full HEADLAND: a run of shards two or three times the big ones, tall enough to read as a cliff from the sea. Rare on purpose -- one an island is a landmark, five is a quarry.")]
        [Range(0f, 0.5f)] public float headlandShare = 0.14f;
        [Tooltip("How much rock stands on the shore and in the shallows, where the wood is not allowed to go. Boulders at the tideline and, rarely, a sea stack off a headland. Zero leaves every beach bare, which is what it was.")]
        [Range(0f, 3f)] public float shoreRock = 1.0f;

        [Header("Detail")]
        public float detailFrequency = 1f / 25f;
        [Range(1, 4)] public int detailOctaves = 2;
        public float detailAmplitude = 0.5f;

        [Header("Shore")]
        [Tooltip("Foreshore slope as a fraction of what the raw terrain gives, at the FLATTEST coasts. 0.08 turns a measured 1:3 into about 1:37.")]
        [Range(0.03f, 1f)] public float shoreSlopeMin = 0.05f;
        [Tooltip("Same, at the STEEPEST coasts. Keeps rock and shingle in the world so a sand beach means something.")]
        [Range(0.03f, 1f)] public float shoreSlopeMax = 0.6f;
        [Tooltip("Bias toward the flat end. Above 1 makes gentle sand the NORM and rock the exception, instead of averaging every coast into the same middling slope — which is what a straight mix gave: a median of 1:6 when the target was 1:20.")]
        public float shoreSlopeBias = 2.5f;
        [Tooltip("How the two are mixed along a coastline, 1/metres. Low frequency so a bay is all one kind of shore rather than alternating every few metres.")]
        public float shoreFrequency = 1f / 900f;
        [Tooltip("Raw height, metres, up to which the foreshore holds its gentle slope before the land starts recovering. Without this the recovery begins at the waterline and the factor has doubled by 7 m of height, which is why the first attempt only moved the median beach from 1:3 to 1:7.")]
        public float shoreFlat = 10f;
        [Tooltip("Height, metres, by which the land is back to its own slope. Everything above this is untouched, so the massifs keep their shape.")]
        public float shoreTop = 30f;
        [Tooltip("Depth, metres, by which the seabed is back to its own slope. Sits at the shelf depth on purpose: the ocean's depth limit is tuned against that shelf and must not move.")]
        public float shoreBottom = 12f;

        [Header("Beach blend")]
        [Tooltip("Height above sea level up to which terrain is fully smooth (sailable, landable).")]
        public float beachHeight = 5f;
        [Tooltip("Metres above beachHeight over which smooth blends into terraced. Smaller = sharper.")]
        public float beachBlendWidth = 4f;

        [Header("Look")]
        [Tooltip("Sand tops out here, metres above sea level — the berm. Not the same as beachHeight, which is where TERRACING starts: sand painted all the way up a blend band is what made the shore read as a yellow hillside.")]
        public float sandHeight = 3.2f;
        [Tooltip("Vertex colour turns to snow above this height. Sits above most islands on purpose: a snow cap should mark the one massif worth steering by, not every hill.")]
        public float snowHeight = 165f;
        [Tooltip("Metres of PROUD rock at which ground is fully stone-coloured. Rock is now a fact about the geometry -- RockBreak knows exactly where rock won -- rather than a guess from the gradient.")]
        public float rockShowsAt = 0.6f;
        [Tooltip("Normal.y where slope alone starts reading as rock. This used to be 0.80 (37 degrees), which is ordinary hillside, and painted broad brown smears across every green flank.")]
        [Range(0.1f, 1f)] public float cliffRockStart = 0.62f;
        [Tooltip("...and where slope alone is fully rock. A genuine cliff.")]
        [Range(0.1f, 1f)] public float cliffRockFull = 0.40f;

        [Header("Chunks")]
        public float chunkSize = 128f;
        [Tooltip("Vertices per chunk edge at LOD 0. (2^k)+1 so LOD strides divide evenly; max 129 for 16-bit indices.")]
        public int chunkResolution = 65;
        [Tooltip("Edge skirt drop, metres. Must clear the worst LOD crack and not one metre more: past that it is a curtain of edge-coloured geometry hanging in view at the boundary of the loaded region. Measured by TuneIslands.Skirt — worst crack 11.9 m at stride 4.")]
        public float skirtDepth = 14f;

        [Header("Streaming / LOD")]
        [Tooltip("Chunks loaded in every direction from the target's chunk.")]
        public int viewRadius = 8;
        [Tooltip("Chunks (Chebyshev distance) at full density.")]
        public int lod0Radius = 2;
        [Tooltip("Chunks at half density; beyond this, quarter density.")]
        public int lod1Radius = 4;
        [Tooltip("Mesh colliders only within this many chunks of the target.")]
        public int colliderRadius = 1;
        [Tooltip("Chunk builds in flight on worker threads at once.")]
        public int jobsInFlight = 2;

        void OnValidate()
        {
            chunkResolution = Mathf.Clamp(chunkResolution, 5, 129);
            // Force (2^k)+1 so lodStep 2 and 4 divide the cell count.
            int cells = Mathf.ClosestPowerOfTwo(chunkResolution - 1);
            chunkResolution = cells + 1;
        }
    }
}
