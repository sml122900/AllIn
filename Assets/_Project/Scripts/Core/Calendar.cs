namespace AllIn.Core
{
    public enum Slot { Dawn = 0, Morning = 1, Afternoon = 2, Evening = 3, Night = 4 }

    public enum Room { Goshiwon = 0, OneRoom = 1, Officetel = 2 }

    public static class Calendar
    {
        public static int AbsoluteSlot(int day, Slot slot) => (day - 1) * Rules.SlotsPerDay + (int)slot;

        /// <summary>0 = Monday … 6 = Sunday. Day 1 (discharge day) is a Monday.</summary>
        public static int Weekday(int day) => (day - 1) % 7;

        public static bool IsSaturday(int day) => Weekday(day) == 5;

        /// <summary>The Saturday on or after this day.</summary>
        public static int NextSaturday(int day) => day + (5 - Weekday(day) + 7) % 7;

        public static bool IsNightSide(Slot s) => s == Slot.Night || s == Slot.Dawn;

        public static Room RoomFor(long cash) =>
            cash >= Rules.RoomOfficetelAt ? Room.Officetel :
            cash >= Rules.RoomOneRoomAt ? Room.OneRoom : Room.Goshiwon;
    }
}
