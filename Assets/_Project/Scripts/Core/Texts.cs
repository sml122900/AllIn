namespace AllIn.Core
{
    /// <summary>
    /// Every player-facing string, in one place. All placeholder — the user will replace them.
    /// Don't polish or add copy here beyond what a mechanic needs.
    /// </summary>
    public static class Texts
    {
        // time
        public static readonly string[] SlotNames = { "새벽", "오전", "오후", "저녁", "밤" };
        public static readonly string[] SlotHours = { "04~08", "08~12", "12~18", "18~23", "23~04" };
        public static readonly string[] Weekdays = { "월", "화", "수", "목", "금", "토", "일" };
        public static readonly string[] RoomNames = { "고시원", "원룸", "오피스텔" };
        public static string DayLabel(int day) => "Day " + day + " (" + Weekdays[Calendar.Weekday(day)] + ")";

        // opening
        public const string OpeningTitle = "전역";
        public static readonly string[] OpeningLines =
        {
            "21개월. 오늘 전역했다.",
            "군 적금이 만기됐다. 통장에 2,000만원.",
            "월세 45만원은 이미 빠져나갔다.",
        };
        public const string FirstPopup = "이 돈으로 뭘 할까";
        public const string Close = "닫기";
        public const string Confirm = "확인";

        // actions
        public const string ActWork = "식당알바";
        public const string ActCoin = "코인";
        public const string ActLotto = "로또";
        public const string ActRest = "쉬기";
        public const string ActSleep = "잠";
        public const string Rested = "쉬었다.";
        public const string Slept = "잤다.";
        public const string AllNighter = "밤을 샜다.";
        public const string AllNighterWarning = "(밤이다 — 밤샘)";
        public const string BodyZero = "몸이 한계다. 쉴 수밖에 없다.";
        public const string CantGetUp = "못 일어났다. 눈을 뜨니 오후다.";
        public const string RentPaid = "월세 450,000원이 빠져나갔다.";
        public const string FoodPaid = "밥값 12,000원.";

        // job
        public const string WorkOnlyEvening = "알바는 저녁에만.";
        public const string WorkTooTired = "몸이 안 움직인다";
        public const string WorkFiredUntil = "잘렸다. 당분간 못 나간다.";
        public const string WorkDone = "식당알바 5시간. 51,600원.";
        public const string WorkMistake = "접시를 깼다. 사장이 내일부터 나오지 말란다.";

        // coin
        public const string CoinTitle = "BTC/USDT 무기한";
        public const string CoinLong = "롱";
        public const string CoinShort = "숏";
        public const string CoinOpen = "진입";
        public const string CoinTakesSlot = "진입·종료하면 이 슬롯이 끝난다.";
        public const string CoinClose = "종료";
        public const string CoinNoPosition = "포지션 없음";
        public const string CoinAlreadyOpen = "이미 포지션이 있다.";
        public const string CoinNoCash = "돈이 없다.";
        public const string CoinBadLeverage = "그런 레버리지는 없다.";
        public const string CoinBelowMin = "본전 생각이 난다. 이 정도로는 성에 안 찬다.";
        public const string CoinPrice = "현재가";
        public const string CoinUnrealized = "미실현손익";
        public const string CoinLiqPrice = "청산가";
        public const string CoinMargin = "증거금";
        public const string CoinLeverage = "레버리지";

        // E1 / E2 / E3 liquidation
        public const string E1Title = "청산";
        public const string E1Body = "포지션이 강제 청산되었습니다.";
        public const string E1Lost = "사라진 돈";
        public const string E2Time = "03:12";
        public const string E2Vibrate = "지잉— 지잉—";
        public const string E2Banner = "[거래소] 포지션이 강제 청산되었습니다.";
        public static readonly string[][] E3Monologues =
        {
            new[] { "천장을 본다.", "그 돈이면 몇 달을 일해야 했지.", "…계산하지 말자." },
            new[] { "엄마한테 전화가 왔었다.", "안 받았다." },
            new[] { "딱 한 번만 버텼으면.", "딱 한 틱만.", "아니, 그건 핑계다." },
            new[] { "통장 앱을 켰다가 껐다.", "다시 켰다.", "숫자는 그대로다." },
        };

        // E4 hope circuit
        public const string E4Title = "희망회로";
        public const string E4Line1 = "이 속도면…";
        public static string E4Rate(double dailyReturn) => "하루 " + Fmt.Pct(dailyReturn) + "씩";
        public static string E4Days(int days) => days + "일 뒤면";
        public const string E4Yes = "그렇지";
        public const string E4Dots = "…";

        // E5 lotto
        public const string LottoTitle = "로또 6/45";
        public const string LottoBuy = "구매 (5,000원 = 5게임)";
        public const string LottoNotAtNight = "밤에는 판매점이 닫았다.";
        public const string LottoDailyLimit = "오늘은 더 못 산다. (하루 20회)";
        public const string LottoNoCash = "5,000원도 없다.";
        public const string LottoDrawTitle = "이번 주 추첨";
        public const string LottoBonus = "보너스";
        public static readonly string[] LottoRankNames = { "낙첨", "1등", "2등", "3등", "4등", "5등" };
        public static string LottoCrowd(int winners) => "이번 주 1등 " + winners + "명 / 1천만 명이 샀다";
        public static string LottoTicketsFor(int day) => "추첨일 " + DayLabel(day);

        // E6 move
        public static string E6Move(Room from, Room to) => RoomNames[(int)from] + "에서 " + RoomNames[(int)to] + "(으)로 옮겼다.";

        // E7 settlement
        public const string E7Title = "30일 정산";
        public const string E7Start = "시작";
        public const string E7Final = "최종 총자산";
        public const string E7Labor = "노동 시간";
        public const string E7CoinPnl = "코인 실현손익";
        public const string E7Liquidations = "청산 횟수";
        public const string E7LottoSpent = "로또 지출";
        public const string E7LottoWon = "로또 당첨(세후)";
        public const string E7Max = "최고 총자산";
        public const string E7Min = "최저 총자산";
        public const string E7Hope = "희망회로 발동";
        public const string E7Scenes = "겪은 장면";
        public const string E7Again = "다시 전역일로";
        public static readonly string[] SceneNames = { "청산(낮)", "새벽 3시 청산 알림", "신세한탄", "희망회로", "로또 추첨", "이사" };

        // hold'em (placeholder names; personalities are never shown)
        public const string HoldemHeroName = "나";
        public static readonly string[] HoldemNames =
        {
            "김대리", "박부장", "취한 아저씨", "민수", "지훈", "서연", "하은", "도윤", "준호", "민지",
            "태식", "영철", "수빈", "재원", "현우", "유진", "상철", "동건", "미래", "건우",
        };

        // HUD
        public const string Cash = "현금";
        public const string Assets = "총자산";
        public const string Body = "체력";
        public const string Mental = "멘탈";
        public const string NewLife = "새 인생";
        public const string NewLifeConfirm = "세이브를 지우고 전역일로 돌아간다. 되돌릴 수 없다.";
        public const string Yes = "예";
        public const string No = "아니오";
    }
}
