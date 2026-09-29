using System.Collections.Generic;

namespace SeaSick.World.Economy
{
    /// One crop a farm plot can grow. Times are REAL seconds (Kevin,
    /// 2026-09-27: "keep 3-min game day, crop/cook timers in real minutes").
    public class CropDef
    {
        public string res;
        /// Farm level the plot's farm needs before this crop is offered.
        public int farmLevel = 1;
        public float growSeconds;
        /// Units one harvest of one plot gives.
        public int yield;
        public string icon;

        public float PerHour => growSeconds > 0f ? yield * 3600f / growSeconds : 0f;
    }

    /// Something a villager can eat. `fill` is a share of one hand's daily
    /// need (`OutpostLedger.EatPerHandPerDay`, 1.0). Raw ones are only eaten
    /// when no cooked dish is left, at `EconomyTuning.RawFill`.
    public class EdibleDef
    {
        public string res;
        public float fill;
        /// Mood a game day while this was the hand's last meal.
        public float moodPerDay;
        /// Extra work pace while this was the hand's last meal (0.1 = +10%).
        public float workBonus;
        public bool raw;
    }

    /// **The food rework's tables (approved 2026-09-27, docs/GDD.md "Food").**
    /// Crops per plot, and what every edible thing is worth. Recipes that
    /// cook them live in `Recipes` like every other station's; the tuning
    /// asset (`EconomyTuning.crops/dishes`) overrides rows by id.
    public static class FoodBook
    {
        public static readonly CropDef[] Crops =
        {
            new CropDef { res = Res.Potato, farmLevel = 1, growSeconds = 300f,  yield = 4,  icon = "🥔" },
            new CropDef { res = Res.Carrot, farmLevel = 1, growSeconds = 480f,  yield = 5,  icon = "🥕" },
            new CropDef { res = Res.Onion,  farmLevel = 2, growSeconds = 720f,  yield = 5,  icon = "🧅" },
            new CropDef { res = Res.Wheat,  farmLevel = 2, growSeconds = 1200f, yield = 10, icon = "🌾" },
            new CropDef { res = Res.Apple,  farmLevel = 3, growSeconds = 3600f, yield = 12, icon = "🍎" },
        };

        /// Cooked first, best first; raw at the end. `fill` of a raw row is
        /// read through `Fill` (the tuning asset's `rawFill`).
        public static readonly EdibleDef[] Edibles =
        {
            new EdibleDef { res = Res.HuntersStew,  fill = 1.25f, moodPerDay = 0.1f,  workBonus = 0.1f },
            new EdibleDef { res = Res.VegStew,      fill = 1.0f,  moodPerDay = 0.1f },
            new EdibleDef { res = Res.FishPie,      fill = 1.0f,  workBonus = 0.1f },
            new EdibleDef { res = Res.RoastCarrots, fill = 1.0f },
            new EdibleDef { res = Res.Bread,        fill = 0.75f, moodPerDay = 0.05f },
            new EdibleDef { res = Res.GrilledMeat,  fill = 0.75f },   // Kevin 2026-09-28: 30 meat and a starving village
            new EdibleDef { res = Res.GrilledFish,  fill = 0.6f },
            new EdibleDef { res = Res.BakedPotato,  fill = 0.5f },
            new EdibleDef { res = Res.Meals,        fill = 0.5f },   // ship's biscuit
            // raw: the "not starving" floor
            new EdibleDef { res = Res.Potato, fill = 0.25f, raw = true },
            new EdibleDef { res = Res.Carrot, fill = 0.25f, raw = true },
            new EdibleDef { res = Res.Fish,   fill = 0.25f, raw = true },
            new EdibleDef { res = Res.Apple,  fill = 0.25f, raw = true },
            new EdibleDef { res = Res.Food,   fill = 0.25f, raw = true },   // wild forage
        };

        static Dictionary<string, CropDef> crops;
        static Dictionary<string, EdibleDef> edibles;

        public static CropDef Crop(string res)
        {
            if (crops == null) { crops = new Dictionary<string, CropDef>(); foreach (var c in Crops) crops[c.res] = c; }
            return res != null && crops.TryGetValue(res, out var d) ? d : null;
        }

        public static EdibleDef Edible(string res)
        {
            if (edibles == null) { edibles = new Dictionary<string, EdibleDef>(); foreach (var e in Edibles) edibles[e.res] = e; }
            return res != null && edibles.TryGetValue(res, out var d) ? d : null;
        }

        public static bool IsCrop(string res) => Crop(res) != null;
        public static bool IsEdible(string res) => Edible(res) != null;
        public static bool IsDish(string res) { var e = Edible(res); return e != null && !e.raw; }

        /// **Anything that feeds the camp, eaten or cooked into something
        /// eaten** -- what the old `Res.Food` meant to the priority switch,
        /// the "food gatherers are never docked" rule and the Food tab.
        public static bool IsFoodish(string res) =>
            res != null && (res == Res.Food || res == Res.Game || res == Res.Meat || res == Res.Flour
                            || IsCrop(res) || IsEdible(res));

        /// Fill of one unit as eaten.
        public static float Fill(string res)
        {
            var e = Edible(res);
            if (e == null) return 0f;
            return e.raw ? EconomyTuning.RawFill : e.fill;
        }

        public static float MoodPerDay(string res)
        {
            var e = Edible(res);
            if (e == null) return 0f;
            return e.raw ? EconomyTuning.RawMoodPerDay : e.moodPerDay;
        }

        public static float WorkBonus(string res) { var e = Edible(res); return e != null && !e.raw ? e.workBonus : 0f; }

        /// Game days one plot takes to ripen, at the running day length.
        public static float GrowDays(string res)
        {
            var c = Crop(res);
            if (c == null) return float.PositiveInfinity;
            return c.growSeconds / SeaSick.World.TimeOfDay.WorkDaySeconds;
        }

        public static string Icon(string res) => res switch
        {
            Res.Potato => "🥔", Res.Carrot => "🥕", Res.Onion => "🧅", Res.Wheat => "🌾", Res.Apple => "🍎",
            Res.Fish => "🐟", Res.Meat => "🍖", Res.Flour => "🌾", Res.Food => "🫐",
            Res.BakedPotato => "🥔", Res.GrilledFish => "🐟", Res.GrilledMeat => "🍖", Res.RoastCarrots => "🥕", Res.Bread => "🍞",
            Res.VegStew => "🍲", Res.FishPie => "🥧", Res.HuntersStew => "🍲", Res.Meals => "🍪",
            _ => "·",
        };

        /// Short buff line for a sheet: "mood +0.1/day · work +10%".
        public static string BonusLine(string res)
        {
            float m = MoodPerDay(res), w = WorkBonus(res);
            var parts = new List<string>();
            if (m > 0f) parts.Add($"mood +{m:0.##}/day");
            else if (m < 0f) parts.Add($"mood {m:0.##}/day");
            if (w > 0f) parts.Add($"work +{w * 100f:0}%");
            return string.Join(" · ", parts);
        }
    }
}
