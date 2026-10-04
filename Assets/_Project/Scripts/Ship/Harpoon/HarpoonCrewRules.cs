namespace SeaSick.Ship.Harpoon
{
    /// **Who mans the bow harpoon (2026-10-04, PLAN-harpoon §5, §8.7).**
    ///
    /// Kevin: *"a crew member mans it, and the captain fires it, slower,
    /// when nobody is free"*. The shipboard priority list is rescue -> bail
    /// -> engaged guns / harpoon -> sails/oars; this is ONLY the harpoon's
    /// place in it:
    ///
    ///  1. **Stability.** The hand already posted stays while he can (a
    ///     walk to the bow is not undone by a better candidate appearing).
    ///     He is dropped when he can no longer serve (overboard, ashore,
    ///     hauling, in the jolly boat, bailing) or -- a borrowed gunner --
    ///     when the fight needs every gunner.
    ///  2. **A spare hand** (no gun of his own), free right now, nearest
    ///     the bow post.
    ///  3. **A gunner on the disengaged side**, free right now, nearest the
    ///     bow -- only while the gunners able to stand at a gun, minus him,
    ///     still cover the guns the fight needs (`CannonBattery.
    ///     HandsForFight`). Out of a fight every side is disengaged.
    ///  4. Else **the captain** (-1): slower, never crewless.
    ///
    /// Pure numbers in, an index out: no Unity objects, no allocation, so
    /// `SelfTest` runs outside the editor. `HarpoonCrewSource` reads the
    /// deck into these arrays and acts on the answer.
    public static class HarpoonCrewRules
    {
        public const int None = GunCrewShift.None;
        public const int Port = GunCrewShift.Port;
        public const int Starboard = GunCrewShift.Starboard;

        /// Accuracy falls with seasickness: 1 - 0.6 x sick, never below this.
        public const float SickAccuracyLoss = 0.6f;
        public const float MinAccuracy = 0.3f;
        /// s a released hand lingers at the bow before he walks back, so a
        /// flicker of Demand does not march him to and fro (the gun shift's
        /// ~3 s hold).
        public const float ReleaseGraceSeconds = 3f;

        /// `isSpare[h]`: no gun of his own. `free[h]`: able to start the
        /// walk now (at his post, not walking, not bailing, not at the
        /// rail, aboard). `gunSide[h]`: a gunner's gun side (Port /
        /// Starboard / None). `dist[h]`: metres to the bow post.
        /// `current`: the hand posted now (-1 none) and `currentOk`: he can
        /// still serve (aboard, not hauling/jolly boat/bailing); for him
        /// `isSpare` / `gunSide` describe what he WAS (a borrowed gunner
        /// stays a gunner). `primary` / `secondary`: the engaged sides.
        /// `handsForFight`: gunners the fight needs; `ableGunners`: gunners
        /// able to stand at a gun at all, the posted one included.
        /// Returns the hand to post, or -1 for the captain.
        public static int Pick(int count, bool[] isSpare, bool[] free, int[] gunSide, float[] dist,
            int current, bool currentOk,
            int primary, int secondary, int handsForFight, int ableGunners)
        {
            bool canBorrow = ableGunners - 1 >= handsForFight;

            if (current >= 0 && current < count && currentOk)
            {
                if (isSpare[current] || canBorrow) return current;
                // a borrowed gunner the fight now needs: back to the guns.
            }

            int best = -1;
            float bestD = float.MaxValue;
            for (int h = 0; h < count; h++)
            {
                if (h == current || !isSpare[h] || !free[h]) continue;
                if (dist[h] < bestD) { bestD = dist[h]; best = h; }
            }
            if (best >= 0 || !canBorrow) return best;

            for (int h = 0; h < count; h++)
            {
                if (h == current || isSpare[h] || !free[h]) continue;
                int s = gunSide[h];
                if (s != None && (s == primary || s == secondary)) continue;
                if (dist[h] < bestD) { bestD = dist[h]; best = h; }
            }
            return best;
        }

        /// A seasick harpooner shoots less steadily.
        public static float Accuracy(float sick01)
        {
            if (sick01 < 0f) sick01 = 0f;
            if (sick01 > 1f) sick01 = 1f;
            float a = 1f - SickAccuracyLoss * sick01;
            return a < MinAccuracy ? MinAccuracy : a;
        }

        /// **The release grace.** `Released` starts it (once), `Manned`
        /// cancels it; `Due` says the hand may walk back now.
        public struct ReleaseClock
        {
            public bool pending;
            public float since;

            public void Manned() { pending = false; }
            public void Released(float now)
            {
                if (pending) return;
                pending = true;
                since = now;
            }
            public bool Due(float now, float grace) => pending && now - since >= grace;
        }

        // ---- self-test (no editor state, no play mode) -------------------
        //
        //   tools/selftest-outside-editor/run.sh SeaSick.Ship.Harpoon.HarpoonCrewRules.SelfTest
        //   unity cmd eval --json --code 'return SeaSick.Ship.Harpoon.HarpoonCrewRules.SelfTest();'

        public static string SelfTest()
        {
            var sb = new System.Text.StringBuilder();
            int pass = 0, total = 0;

            void Check(string name, bool ok)
            {
                total++;
                if (ok) { pass++; return; }
                sb.Append("FAIL ").Append(name).Append('\n');
            }

            void Case(string name, bool[] spare, bool[] free, int[] side, float[] dist,
                      int cur, bool curOk, int p, int s, int need, int able, int want)
            {
                int got = Pick(spare.Length, spare, free, side, dist, cur, curOk, p, s, need, able);
                total++;
                if (got == want) { pass++; return; }
                sb.Append("FAIL ").Append(name).Append(": want ").Append(want)
                  .Append(" got ").Append(got).Append('\n');
            }

            // A coaster crew: hands 0..3 gunners (0,2 starboard; 1,3 port),
            // hands 4,5 spares. Distances to the bow post.
            bool[] sp = { false, false, false, false, true, true };
            bool[] allFree = { true, true, true, true, true, true };
            int[] side = { Starboard, Port, Starboard, Port, None, None };
            float[] d = { 6f, 6f, 10f, 10f, 9f, 4f };

            Case("a spare goes, the nearest one", sp, allFree, side, d,
                 -1, false, None, None, 0, 4, 5);
            Case("a spare goes before a nearer gunner", sp, allFree, side,
                 new[] { 1f, 1f, 1f, 1f, 9f, 8f }, -1, false, None, None, 0, 4, 5);
            Case("busy spares (bailing / at the rail): a gunner, out of a fight",
                 sp, new[] { true, true, true, true, false, false }, side, d,
                 -1, false, None, None, 0, 4, 0);
            Case("fight to starboard, two guns a side: a PORT gunner, not the nearer starboard one",
                 sp, new[] { true, true, true, true, false, false }, side,
                 new[] { 2f, 6f, 2f, 9f, 9f, 9f }, -1, false, Starboard, None, 2, 4, 1);
            Case("fight to starboard, four guns a side, three gunners: the captain",
                 new[] { false, false, false }, new[] { true, true, true },
                 new[] { Starboard, Port, Starboard }, new[] { 3f, 3f, 3f },
                 -1, false, Starboard, None, 4, 3, -1);
            Case("enemies both sides, every gun wanted: the captain",
                 sp, new[] { true, true, true, true, false, false }, side, d,
                 -1, false, Starboard, Port, 4, 4, -1);
            Case("enemies both sides but a spare is free: the spare",
                 sp, allFree, side, d, -1, false, Starboard, Port, 4, 4, 5);
            Case("the posted spare stays though a nearer one frees up", sp, allFree, side, d,
                 4, true, None, None, 0, 4, 4);
            Case("posted spare overboard: the other spare", sp, allFree, side, d,
                 5, false, None, None, 0, 4, 4);
            Case("borrowed gunner stays while the fight can spare him",
                 sp, new[] { true, false, true, true, false, false }, side, d,
                 1, true, Starboard, None, 2, 4, 1);
            Case("borrowed gunner dropped when the fight needs him (no spare): the captain",
                 sp, new[] { true, false, true, true, false, false }, side, d,
                 1, true, Starboard, Port, 4, 4, -1);
            Case("borrowed gunner dropped when the fight needs him, a spare is free: the spare",
                 sp, new[] { true, false, true, true, true, false }, side, d,
                 1, true, Starboard, Port, 4, 4, 4);
            Case("two gunners overboard: the two left are both wanted, captain",
                 new[] { false, false, false, false }, new[] { true, true, false, false },
                 new[] { Starboard, Port, Starboard, Port }, new[] { 3f, 3f, 3f, 3f },
                 -1, false, Starboard, None, 2, 2, -1);
            Case("one gunner overboard: three able, two wanted, a port one goes",
                 new[] { false, false, false, false }, new[] { true, true, true, false },
                 new[] { Starboard, Port, Starboard, Port }, new[] { 3f, 3f, 3f, 3f },
                 -1, false, Starboard, None, 2, 3, 1);
            Case("nobody free at all: the captain",
                 sp, new[] { false, false, false, false, false, false }, side, d,
                 -1, false, None, None, 0, 4, -1);
            Case("no crew: the captain", new bool[0], new bool[0], new int[0], new float[0],
                 -1, false, None, None, 0, 0, -1);

            Check("accuracy: well = 1", Accuracy(0f) == 1f);
            Check("accuracy: half sick = 0.7", System.Math.Abs(Accuracy(0.5f) - 0.7f) < 1e-5f);
            Check("accuracy: green = 0.4, >= floor", System.Math.Abs(Accuracy(1f) - 0.4f) < 1e-5f
                                                   && Accuracy(1f) >= MinAccuracy);
            Check("accuracy: out of range clamps", Accuracy(5f) >= MinAccuracy && Accuracy(-1f) == 1f);

            var c = new ReleaseClock();
            c.Released(10f);
            Check("grace: not due inside 3 s", !c.Due(12.9f, ReleaseGraceSeconds));
            c.Released(12f);   // a second release does not restart it
            Check("grace: due at 3 s from the FIRST release", c.Due(13f, ReleaseGraceSeconds));
            c.Manned();
            Check("grace: demand back cancels it", !c.Due(99f, ReleaseGraceSeconds));
            c.Released(20f);
            Check("grace: a new release starts a new grace",
                  !c.Due(22f, ReleaseGraceSeconds) && c.Due(23.01f, ReleaseGraceSeconds));

            // Bail order (CrewRoster.BailOrder): bail outranks the harpoon
            // when the buckets NEED him, never for merely having no gun.
            // Hands: 0 gunner z 2, 1 gunner z -2, 2 idle spare, 3 the
            // harpooner (a borrowed gunner: no gun while posted).
            var order = new int[4];
            bool[] here = { true, true, true, true };
            float[] gz = { 2f, -2f, 0f, 0f };
            int bn = Crew.CrewRoster.BailOrder(4, here, new[] { true, true, false, false },
                new[] { false, false, false, true }, gz, order);
            Check("bail: idle spare, then the harpooner, then gunners aft-first",
                  bn == 4 && order[0] == 2 && order[1] == 3 && order[2] == 1 && order[3] == 0);
            Check("bail: one bucket wanted takes the idle spare, not the harpooner",
                  System.Array.IndexOf(order, 3) >= 1);
            bn = Crew.CrewRoster.BailOrder(4, here, new[] { true, true, false, true },
                new[] { false, false, false, true }, new[] { 2f, -2f, 0f, 9f }, order);
            Check("bail: a harpooner still holding a gun is not sorted in with the gunners",
                  bn == 4 && order[0] == 2 && order[1] == 3 && order[2] == 1 && order[3] == 0);
            bn = Crew.CrewRoster.BailOrder(3, new[] { true, true, true }, new[] { true, true, false },
                new[] { false, false, true }, gz, order);
            Check("bail: no idle spare -- the harpooner goes before any gun falls silent",
                  bn == 3 && order[0] == 2 && order[1] == 1 && order[2] == 0);
            var o5 = new int[5];
            bn = Crew.CrewRoster.BailOrder(5, new[] { true, true, true, true, true },
                new[] { true, true, true, true, false }, new[] { false, false, false, false, false },
                new[] { 4.8f, -1.2f, 4.8f, -1.2f, 0f }, o5);
            Check("bail: authored four + spare keeps the old table {4,3,1,2,0}",
                  bn == 5 && o5[0] == 4 && o5[1] == 3 && o5[2] == 1 && o5[3] == 2 && o5[4] == 0);

            sb.Insert(0, $"{(pass == total ? "PASS" : "FAIL")} {pass}/{total}\n");
            return sb.ToString().TrimEnd();
        }
    }
}
