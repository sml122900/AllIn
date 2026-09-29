namespace AllIn.Core.Actions
{
    /// <summary>One purchase = 5,000 won = 5 auto-picked games, for the next Saturday draw. Instant.</summary>
    public sealed class LottoBuyAction : IAction
    {
        public static readonly LottoBuyAction Instance = new LottoBuyAction();

        public string Id => "lotto.buy";
        public bool EndsSlot => false;
        public SlotMask Slots => SlotMask.Day;
        public long CashCost(Loop loop) => Rules.LottoPrice;

        public Availability Check(Loop loop)
        {
            var d = loop.Data;
            if (d.slot == Slot.Night) return Availability.No(Texts.LottoNotAtNight);
            if (d.lottoBoughtToday >= Rules.LottoMaxPerDay) return Availability.No(Texts.LottoDailyLimit);
            if (d.bankCash < Rules.LottoPrice) return Availability.No(Texts.LottoNoCash);
            return Availability.Yes;
        }

        public ActionResult Execute(Loop loop)
        {
            var d = loop.Data;
            loop.SpendBank(Rules.LottoPrice);
            d.stats.lottoSpent += Rules.LottoPrice;
            d.lottoBoughtToday++;
            var t = new LottoTicket { serial = ++d.lottoTicketSerial, drawDay = Calendar.NextSaturday(d.day) };
            for (int g = 0; g < Rules.LottoGamesPerTicket; g++)
            {
                int[] game = LottoRules.PickGame(loop.Rng);
                System.Array.Copy(game, 0, t.numbers, g * Rules.LottoPicks, Rules.LottoPicks);
            }
            d.tickets.Add(t);
            return ActionResult.Done(Texts.ActLotto + " 5게임 (" + Fmt.Won(Rules.LottoPrice) + ")");
        }
    }
}
