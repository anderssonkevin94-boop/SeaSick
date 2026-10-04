namespace SeaSick.Ship
{
    /// **Who stands at which gun when a fight is on one side (2026-10-03).**
    ///
    /// Kevin, approved spec: *gunners whose gun faces away WALK to unmanned
    /// guns on the engaged side; enemies both sides -> fill the locked
    /// target's side first, spare gunners man the other side; after the
    /// fight they walk back to their home guns.* This is the pure half of
    /// that: no Unity objects, no allocation, just numbers in and a gun per
    /// hand out, so the rules can be checked without a play session
    /// (`SelfTest`). `CannonBattery.RebalanceCrews` gathers the inputs from
    /// the live deck and walks the hands to whatever this answers.
    ///
    /// **Stability first.** A hand already standing at a gun on a side that
    /// wants him keeps that gun; a hand whose HOME gun is on that side goes
    /// home; only then are the free guns handed out, nearest along the deck
    /// (by z) to where the hand is now. So a second call with the same
    /// sides changes nothing, and a hand who comes back from the buckets or
    /// a rescue never bumps somebody who walked over to cover for him.
    ///
    /// Hands, not guns, are the input list: one entry per hand that HAS a
    /// home gun (the battery's named gunner for it). Spare hands with no
    /// gun are not gunners and never move -- the full shipboard priority
    /// list (rescue -> bail -> engaged guns -> sails) is later and is not
    /// this.
    public static class GunCrewShift
    {
        public const int None = -1, Port = 0, Starboard = 1;

        /// `gunSide[g]`: Port, Starboard, or None for a gun that cannot be
        /// stood at (destroyed mid-refit). `gunZ[g]`: ship-local z, for "the
        /// nearest free gun". `handHome[h]`: that hand's home gun index;
        /// `handEligible[h]`: false drops him from the map (overboard,
        /// ashore, hauling, jolly boat) and answers -1; `handCurrent[h]`:
        /// the gun he stands at now, or -1. `primary` / `secondary`: the
        /// engaged sides (None = no fight; `secondary` is the other side
        /// when there are enemies both sides). Writes `outTarget[h]` = the
        /// gun he should stand at, or -1. `gunTaken` / `handPlaced` are
        /// scratch, at least `gunCount` / `handCount` long.
        public static void Assign(int gunCount, int[] gunSide, float[] gunZ,
            int handCount, int[] handHome, bool[] handEligible, int[] handCurrent,
            int primary, int secondary,
            int[] outTarget, bool[] gunTaken, bool[] handPlaced)
        {
            for (int g = 0; g < gunCount; g++) gunTaken[g] = false;
            for (int h = 0; h < handCount; h++)
            {
                outTarget[h] = -1;
                handPlaced[h] = !handEligible[h];
            }

            if (secondary == primary) secondary = None;
            if (primary == None) secondary = None;

            // No fight (or the grace ran out): everybody to his own gun.
            // Home guns are one per hand by construction, so no clashes.
            if (primary == None)
            {
                for (int h = 0; h < handCount; h++)
                {
                    if (handPlaced[h]) continue;
                    Take(h, handHome[h], outTarget, gunTaken, handPlaced, gunCount);
                }
                return;
            }

            // The locked target's side first, then (enemies both sides) the
            // other one with whoever is left.
            FillSide(primary, primary, secondary, gunCount, gunSide, gunZ,
                handCount, handHome, handCurrent, outTarget, gunTaken, handPlaced);
            if (secondary != None)
                FillSide(secondary, primary, secondary, gunCount, gunSide, gunZ,
                    handCount, handHome, handCurrent, outTarget, gunTaken, handPlaced);

            // Everyone not needed on an engaged side: his own gun if it is
            // free (the away side, which nobody else wants), else the
            // nearest free one. There are never more gunners than guns, so
            // somewhere is always free.
            for (int h = 0; h < handCount; h++)
            {
                if (handPlaced[h]) continue;
                int home = handHome[h];
                if (home >= 0 && home < gunCount && !gunTaken[home])
                {
                    Take(h, home, outTarget, gunTaken, handPlaced, gunCount);
                    continue;
                }
                int g = NearestFree(None, FromZ(h, gunZ, handHome, handCurrent, gunCount),
                                    gunCount, gunSide, gunZ, gunTaken);
                if (g >= 0) Take(h, g, outTarget, gunTaken, handPlaced, gunCount);
                else handPlaced[h] = true;   // nowhere: stands down (-1)
            }
        }

        static void FillSide(int side, int primary, int secondary,
            int gunCount, int[] gunSide, float[] gunZ,
            int handCount, int[] handHome, int[] handCurrent,
            int[] outTarget, bool[] gunTaken, bool[] handPlaced)
        {
            // a) already standing at a gun on this side: keep it.
            for (int h = 0; h < handCount; h++)
            {
                if (handPlaced[h]) continue;
                int c = handCurrent[h];
                if (c >= 0 && c < gunCount && gunSide[c] == side && !gunTaken[c])
                    Take(h, c, outTarget, gunTaken, handPlaced, gunCount);
            }
            // b) his own gun is on this side and free: go home.
            for (int h = 0; h < handCount; h++)
            {
                if (handPlaced[h]) continue;
                int home = handHome[h];
                if (home >= 0 && home < gunCount && gunSide[home] == side && !gunTaken[home])
                    Take(h, home, outTarget, gunTaken, handPlaced, gunCount);
            }
            // c) the free guns, nearest first, to: a displaced hand whose
            // home is HERE, then the true spares (home on a side nobody is
            // fighting), then -- filling the locked side first -- hands
            // whose own gun is on the other engaged side.
            for (int pass = 0; pass < 3; pass++)
            {
                for (int h = 0; h < handCount; h++)
                {
                    if (handPlaced[h]) continue;
                    int home = handHome[h];
                    int homeSide = home >= 0 && home < gunCount ? gunSide[home] : None;
                    int cls = homeSide == side ? 0
                        : (homeSide != primary && homeSide != secondary) ? 1 : 2;
                    if (cls != pass) continue;
                    int g = NearestFree(side, FromZ(h, gunZ, handHome, handCurrent, gunCount),
                                        gunCount, gunSide, gunZ, gunTaken);
                    if (g < 0) return;   // this side is full
                    Take(h, g, outTarget, gunTaken, handPlaced, gunCount);
                }
            }
        }

        static float FromZ(int h, float[] gunZ, int[] handHome, int[] handCurrent, int gunCount)
        {
            int at = handCurrent[h] >= 0 && handCurrent[h] < gunCount ? handCurrent[h] : handHome[h];
            return at >= 0 && at < gunCount ? gunZ[at] : 0f;
        }

        /// The free gun on `side` (None = either side) nearest `z`; ties to
        /// the lower index, so the answer never depends on anything but the
        /// inputs.
        static int NearestFree(int side, float z, int gunCount, int[] gunSide, float[] gunZ, bool[] gunTaken)
        {
            int best = -1;
            float bestD = float.MaxValue;
            for (int g = 0; g < gunCount; g++)
            {
                if (gunTaken[g] || gunSide[g] == None) continue;
                if (side != None && gunSide[g] != side) continue;
                float d = gunZ[g] - z;
                if (d < 0f) d = -d;
                if (d < bestD) { bestD = d; best = g; }
            }
            return best;
        }

        static void Take(int h, int g, int[] outTarget, bool[] gunTaken, bool[] handPlaced, int gunCount)
        {
            handPlaced[h] = true;
            if (g < 0 || g >= gunCount) return;
            outTarget[h] = g;
            gunTaken[g] = true;
        }

        // ---- self-test (no editor state, no play mode) -------------------
        //
        //   unity cmd eval --json --code 'return SeaSick.Ship.GunCrewShift.SelfTest();'
        //
        // Returns "PASS n/n" or the first failures. Pure C#: the same call
        // works from any test runner.

        public static string SelfTest()
        {
            var sb = new System.Text.StringBuilder();
            int pass = 0, total = 0;
            // The authored four: two a side, fore (z 4.8) and aft (z -1.2).
            int[] four = { Port, Port, Starboard, Starboard };
            float[] fourZ = { 4.8f, -1.2f, 4.8f, -1.2f };

            void Case(string name, int[] side, float[] z, int[] home, bool[] ok, int[] cur,
                      int p, int s, int[] want)
            {
                total++;
                var got = new int[home.Length];
                Assign(side.Length, side, z, home.Length, home, ok, cur, p, s,
                       got, new bool[side.Length], new bool[home.Length]);
                bool same = true;
                for (int i = 0; i < want.Length; i++) same &= got[i] == want[i];
                if (same) { pass++; return; }
                sb.Append("FAIL ").Append(name).Append(": want [")
                  .Append(string.Join(",", want)).Append("] got [")
                  .Append(string.Join(",", got)).Append("]\n");
            }

            bool[] all4 = { true, true, true, true };
            int[] home4 = { 0, 1, 2, 3 };
            Case("full crew: nobody moves", four, fourZ, home4, all4, home4,
                 Starboard, None, new[] { 0, 1, 2, 3 });
            Case("two port gunners, fight to starboard: both cross, fore to fore",
                 four, fourZ, new[] { 0, 1 }, new[] { true, true }, new[] { 0, 1 },
                 Starboard, None, new[] { 2, 3 });
            Case("fight over: back to home guns",
                 four, fourZ, new[] { 0, 1 }, new[] { true, true }, new[] { 2, 3 },
                 None, None, new[] { 0, 1 });
            Case("enemies both sides, lock starboard: starboard filled first",
                 four, fourZ, new[] { 0, 2 }, new[] { true, true }, new[] { 0, 2 },
                 Starboard, Port, new[] { 3, 2 });
            Case("enemies both sides, a spare mans the other side",
                 four, fourZ, new[] { 0, 1, 2 }, new[] { true, true, true }, new[] { 0, 1, 2 },
                 Starboard, Port, new[] { 3, 1, 2 });
            Case("starboard gunner overboard: a port hand covers his gun",
                 four, fourZ, home4, new[] { true, true, false, true }, new[] { 0, 1, -1, 3 },
                 Starboard, None, new[] { 2, 1, -1, 3 });
            Case("rescued hand back: the cover keeps his gun, he takes the free one nearest",
                 four, fourZ, home4, all4, new[] { 2, 1, -1, 3 },
                 Starboard, None, new[] { 2, 1, 0, 3 });
            Case("rescued hand back while short: cover stays, he takes the other engaged gun",
                 four, fourZ, new[] { 0, 2 }, new[] { true, true }, new[] { 2, -1 },
                 Starboard, None, new[] { 2, 3 });
            Case("guns on one side only, fight on the other: stay home",
                 new[] { Starboard, Starboard }, new[] { 2f, -2f }, new[] { 0 }, new[] { true }, new[] { 0 },
                 Port, None, new[] { 0 });
            Case("stable: crossed hands keep their guns",
                 four, fourZ, new[] { 0, 1 }, new[] { true, true }, new[] { 3, 2 },
                 Starboard, None, new[] { 3, 2 });
            Case("a destroyed gun is never handed out",
                 new[] { Port, Port, Starboard, None }, fourZ, new[] { 0, 1 }, new[] { true, true }, new[] { 0, 1 },
                 Starboard, None, new[] { 2, 1 });
            Case("coaster 4 a side, 3 gunners, fight to port: two cross to the nearest",
                 new[] { Starboard, Port, Starboard, Port, Starboard, Port, Starboard, Port },
                 new[] { 6f, 6f, 2f, 2f, -2f, -2f, -6f, -6f },
                 new[] { 0, 1, 2 }, new[] { true, true, true }, new[] { 0, 1, 2 },
                 Port, None, new[] { 3, 1, 5 });

            // 2026-10-04: mid-walk. `handCurrent` is the gun a hand is
            // mapped to, which is his walk TARGET while he is still on the
            // way (`CannonBattery.GunOf`), so these are the walking cases.
            Case("mid-walk, lock flips to the side they left: both turn back home",
                 four, fourZ, new[] { 0, 1 }, new[] { true, true }, new[] { 2, 3 },
                 Port, Starboard, new[] { 0, 1 });
            Case("long walk: the 1 Hz re-plan while still walking changes nothing",
                 new[] { Starboard, Port, Starboard, Port, Starboard, Port, Starboard, Port },
                 new[] { 6f, 6f, 2f, 2f, -2f, -2f, -6f, -6f },
                 new[] { 0, 1, 2 }, new[] { true, true, true }, new[] { 3, 1, 5 },
                 Port, None, new[] { 3, 1, 5 });

            // A side sequence (each answer fed back as the next `current`):
            // a hand's gun changes only on the step the side changes, and
            // a flip back lands him on the same gun as before (no drift, no
            // ping-pong between covers).
            {
                total++;
                int[] home = { 0, 1 }, cur = { 0, 1 }, got = new int[2];
                int[] seq = { Starboard, Starboard, Port, Port, Starboard, Starboard, None, None };
                int[][] want = { new[] { 2, 3 }, new[] { 2, 3 }, new[] { 0, 1 }, new[] { 0, 1 },
                                 new[] { 2, 3 }, new[] { 2, 3 }, new[] { 0, 1 }, new[] { 0, 1 } };
                bool ok = true;
                for (int i = 0; i < seq.Length && ok; i++)
                {
                    Assign(4, four, fourZ, 2, home, new[] { true, true }, cur, seq[i], None,
                           got, new bool[4], new bool[2]);
                    ok = got[0] == want[i][0] && got[1] == want[i][1];
                    if (!ok) sb.Append($"FAIL side sequence step {i}: want [{want[i][0]},{want[i][1]}] got [{got[0]},{got[1]}]\n");
                    cur[0] = got[0]; cur[1] = got[1];
                }
                if (ok) pass++;
            }

            sb.Insert(0, $"{(pass == total ? "PASS" : "FAIL")} {pass}/{total}\n");
            return sb.ToString().TrimEnd();
        }
    }
}
