namespace AllIn.Core
{
    /// <summary>
    /// Mental-recovery candidates (v0.4 reply, DESIGN.md D9). Sim-injectable per Loop. **V3 is the rule since 2026-09-30** (the default instance); V0 is the old
    /// behaviour kept for comparison sims. Not saved.
    /// </summary>
    public sealed class MentalTuning
    {
        public int SleepMental = Rules.SleepMental;   // defaults = V3
        /// <summary>0 = off. Otherwise: this many consecutive worked days with no mistake / firing / missed day pays StreakBonus once, then the count restarts.</summary>
        public int StreakDays = Rules.WorkStreakDays;
        public int StreakBonus = Rules.WorkStreakBonus;

        public static MentalTuning V0() => new MentalTuning { SleepMental = 3, StreakDays = 0, StreakBonus = 0 };
        public static MentalTuning V1() => new MentalTuning { SleepMental = 5, StreakDays = 0, StreakBonus = 0 };
        public static MentalTuning V2() => new MentalTuning { SleepMental = 3 };
        public static MentalTuning V3() => new MentalTuning();
        public static MentalTuning ByName(string n) => n switch { "V0" => V0(), "V1" => V1(), "V2" => V2(), _ => V3() };
    }

    public sealed partial class Loop
    {
        public MentalTuning Tuning { get; set; } = MentalTuning.V3();

        /// <summary>Called by the restaurant shift. Consecutive = worked yesterday too; a mistake resets the count.</summary>
        internal void NoteShift(bool mistake)
        {
            var d = Data;
            if (mistake) { d.workStreak = 0; d.lastWorkDay = d.day; return; }
            d.workStreak = (d.lastWorkDay == d.day - 1 && d.workStreak > 0) ? d.workStreak + 1 : 1;
            d.lastWorkDay = d.day;
            if (Tuning.StreakDays > 0 && d.workStreak >= Tuning.StreakDays)
            {
                AddMental(Tuning.StreakBonus);
                d.workStreak = 0;
            }
        }
    }
}
