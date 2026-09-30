namespace AllIn.Core
{
    /// <summary>
    /// Mental-recovery candidates (v0.4 reply, DESIGN.md D9). Sim-injectable per Loop; the default is V0 = the current rules
    /// (Rules.SleepMental, no streak bonus). Not saved: the game always runs V0 until the user picks a variant.
    /// </summary>
    public sealed class MentalTuning
    {
        public int SleepMental = Rules.SleepMental;
        /// <summary>0 = off. Otherwise: this many consecutive worked days with no mistake / firing / missed day pays StreakBonus once, then the count restarts.</summary>
        public int StreakDays;
        public int StreakBonus;

        public static MentalTuning V0() => new MentalTuning();
        public static MentalTuning V1() => new MentalTuning { SleepMental = 5 };
        public static MentalTuning V2() => new MentalTuning { StreakDays = 5, StreakBonus = 8 };
        public static MentalTuning V3() => new MentalTuning { SleepMental = 5, StreakDays = 5, StreakBonus = 8 };
        public static MentalTuning ByName(string n) => n switch { "V1" => V1(), "V2" => V2(), "V3" => V3(), _ => V0() };
    }

    public sealed partial class Loop
    {
        public MentalTuning Tuning { get; set; } = MentalTuning.V0();

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
