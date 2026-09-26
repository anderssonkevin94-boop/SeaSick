using UnityEngine;

namespace SeaSick.World
{
    /// **The hunt's room and the hunt's clock (2026-09-26).**
    ///
    /// Kevin, phone playtest: *"in storage the animal will be x amount of
    /// meat and x amount of furs. right now the furs don't show up in my
    /// inventory."* An animal is a CARCASS: `Res.MeatPerAnimal` Food and
    /// `Techs.HuntDrops` (1 Hide) in one. Until today the kill was metered
    /// against the FOOD pile's room only, so a camp whose larder sat full
    /// (a forager on Food, a kitchen, a full Meals rack -- Kevin's Island_2
    /// camp at 29.8/30 Food) booked no kill at all, and therefore no hide,
    /// ever, while the hunter's body went on miming kills the books never
    /// made. Now a hunt stops only when there is room for NEITHER half of
    /// the carcass; whichever half does not fit is lost at the store, the
    /// same rule the hide already had ("a full hide pile does not stop the
    /// hunt, the hide is simply lost").
    public partial class OutpostLedger
    {
        /// Animals' worth of room the store has for a carcass: the larger
        /// of the Food room (in animals) and each drop's room (in animals).
        /// Net of loads walking to the store, like every other room.
        float HuntRoomAnimals()
        {
            float room = Mathf.Max(0f, StoreRoomF(Res.Food)) / Res.MeatPerAnimal;
            foreach (var drop in Economy.Techs.HuntDrops)
            {
                if (drop.n <= 0) continue;
                room = Mathf.Max(room, Mathf.Max(0f, StoreRoomF(drop.res)) / drop.n);
            }
            return room;
        }

        /// True when neither the meat nor any drop of a carcass has room --
        /// the one "store full" that stops a hunter.
        bool HuntStoreFull() => HuntRoomAnimals() <= 0f;

        /// **How far the herd's current animal is through being stalked**,
        /// 0..1, straight off the books: `Game.standing` falls continuously
        /// as hunters' days are paid, and `Outpost.SyncHunting` drops a beast
        /// each time it crosses a whole number (`CeilToInt`). So 11.3
        /// standing is the 12th animal 70 % taken. The hunter's body reads
        /// this to know when to close in and strike -- it never moves it.
        /// 0 with no herd or a whole number standing.
        public float HuntProgress01()
        {
            var stock = Stock(Res.Game);
            if (stock == null || stock.standing <= 0f) return 0f;
            float frac = stock.standing - Mathf.Floor(stock.standing);
            if (frac < 1e-4f) return 0f;
            return Mathf.Clamp01(1f - frac);
        }
    }
}
