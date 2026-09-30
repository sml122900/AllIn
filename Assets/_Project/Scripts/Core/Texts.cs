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

        // bank / wallet (B5 v0.3 — placeholder names, no real brands)
        public const string WalletBank = "은행";
        public const string WalletDomestic = "국내 거래소";
        public const string WalletOverseas = "해외 지갑";
        public const string WalletInTransit = "전송 중";
        public const string WalletBadAmount = "그 금액은 안 된다.";
        public const string WalletLocked = "아직 잠겨 있다.";
        public const string WalletTransferSend = "전송";
        public const string WalletDeposit = "입금";
        public const string WalletWithdraw = "출금";
        public static string WalletLockRemaining(int hours) => "약 " + hours + "시간 남음";
        public const string WalletUnlocked = "전송 가능";
        public const string WalletNoSleepFull = "잠 못 듦";

        // B5a 관전 (watch)
        public const string WatchTitle = "관전";
        public const string WatchPray = "기도";
        public const string WatchAddMargin = "증거금 추가";
        public const string WatchStopLoss = "손절";
        public const string WatchWorkLine = "손님이 부른다.";
        public static string WatchPercentToLiq(double pct) => "청산까지 " + Fmt.Pct(pct, 0);
        public const string WatchNoOverseasCash = "잔고 없음 — 입금 후 24시간 뒤 전송 가능";

        // B5b 분노 (tilt)
        public const string TiltBanner = "분노";
        public static string TiltRecovery(int leverage, double pct) => leverage + "배 · " + Fmt.Pct(pct);
        public const string TiltRecoveryTitle = "잃은 돈을 되찾으려면";
        public const string TiltGoDeposit = "입금하러 가기";

        // B5d E8 ghost (본절)
        public const string E8Title = "본절";
        public const string E8Body = "그때 팔지 않았다면.";
        public const string E8Enhanced = "하필 지금.";
        public static string E8IfAlive(long pnl) => "살아 있었다면 " + Fmt.SignedWon(pnl);

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
        public const string E7Prayers = "기도";
        public const string E7RageBets = "분노 베팅";
        public const string E7GhostMissed = "놓친 돈";
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

        // hold'em table UI
        public static readonly string[] HoldemKindNames = { "홀덤펍 데일리", "새틀라이트", "메인 이벤트" };
        public const string HoldemMenuTitle = "홀덤 토너먼트";
        public const string HoldemStudy = "공부 레벨 (HUD)";
        public static readonly string[] HoldemStudyNames = { "0 · 기본", "1 · 스택 bb", "2 · 포지션", "3 · 팟오즈", "4 · 푸시/폴드" };
        public const string HoldemSeed = "시드";
        public const string HoldemRandomSeed = "랜덤";
        public const string HoldemStart = "시작";
        public const string HoldemFold = "폴드";
        public const string HoldemCheck = "체크";
        public const string HoldemCall = "콜";
        public const string HoldemBet = "벳";
        public const string HoldemRaise = "레이즈";
        public const string HoldemAllIn = "올인";
        public static readonly string[] HoldemPresets = { "1/2팟", "2/3팟", "팟", "올인" };
        public const string HoldemPot = "팟";
        public const string HoldemPotOdds = "팟오즈";
        public const string HoldemPushFold = "푸시/폴드";
        public const string HoldemTooDeep = "15bb 초과 — 푸시/폴드 구간 아님";
        public static string HoldemPushAdvice(double topPercent, double bb, string pos) =>
            pos + " · " + bb.ToString("F1") + "bb\n푸시 권장: 상위 " + topPercent.ToString("F0") + "%";
        public static string HoldemCallAdvice(double topPercent, double bb, string shoverPos) =>
            shoverPos + " 올인에 콜 · " + bb.ToString("F1") + "bb\n콜 권장: 상위 " + topPercent.ToString("F0") + "%";
        public static string HoldemMyHand(string cls, double percentile, bool inRange) =>
            "내 핸드 " + cls + " (상위 " + percentile.ToString("F0") + "%) → " + (inRange ? "O" : "X");
        public static string HoldemLevel(int level, long sb, long bb) => "레벨 " + level + " · " + sb.ToString("N0") + "/" + bb.ToString("N0");
        public static string HoldemNextLevel(int hands) => "다음 레벨까지 " + hands + "핸드";
        public static string HoldemHandNo(int n) => "핸드 #" + n;
        public static string HoldemRemaining(int n) => "남은 인원 " + n + "명";
        public const string HoldemFinalTable = "파이널 테이블";
        public const string HoldemBubble = "버블";
        public const string HoldemBubbleSub = "한 명만 더 떨어지면 입상";
        public const string HoldemTablesMerged = "테이블이 합쳐졌다";
        public static readonly string[] HoldemBadBeatLines = { "…말이 되냐.", "이게 빠진다고?", "리버. 또 리버." };
        public static string HoldemReentryPrompt(long cost) => "리엔트리 " + cost.ToString("N0") + "원?";
        public static string HoldemBusted(int finish) => "탈락 · " + finish + "위";
        public const string HoldemReentered = "리엔트리";
        public const string HoldemResultTitle = "결과";
        public static string HoldemFinish(int finish) => finish + "위";
        public const string HoldemPrizeNone = "상금 없음";
        public const string HoldemPrizeSatSeat = "새틀라이트 참가권 1장";
        public const string HoldemPrizeMainSeat = "메인 이벤트 시트";
        public static string HoldemPrizePoints(long p) => "펍 포인트 " + p + "점";
        public static string HoldemPrizeCash(long gross, long net) => Fmt.Won(gross) + "  (제세공과금 22% 공제 후 " + Fmt.Won(net) + ")";
        public const string HoldemHandsPlayed = "플레이한 핸드";
        public const string HoldemReentries = "리엔트리";
        public const string HoldemBadBeats = "배드빗";
        public const string HoldemBubbleReached = "버블 도달";
        public const string HoldemBackToMenu = "메뉴로";
        public static readonly string[] HoldemChipPiles = { "칩 ▪ 적음", "칩 ▪▪ 보통", "칩 ▪▪▪ 많음" };
        public static readonly string[] HoldemHandNames = { "하이카드", "원페어", "투페어", "트리플", "스트레이트", "플러시", "풀하우스", "포카드", "스트레이트 플러시" };
        // ---- hold'em v2 (all-in or fold + win ladder), placeholders ----
        public const string LadderAllIn = "올인";
        public const string LadderFold = "폴드";
        public static string LadderHand(int n) => "핸드 #" + n;
        public static string LadderWins(int wins, string ladder) => "승수 " + wins + "  (" + ladder + ")";
        public static string LadderRung(string name, int wins) => name + " " + wins + "승";
        public static readonly string[] LadderStageNames = { "", "참가권", "머니인", "파이널 테이블", "입상", "우승" };
        public static string LadderStack(double bb) => "스택 " + bb.ToString("F1") + "bb";
        public static string LadderBlindIn(int hands, long bb) => "블라인드 " + bb.ToString("N0") + " · 다음 레벨까지 " + hands + "핸드";
        public static string LadderPosition(string pos, int behind) => "포지션 " + pos + " · 내 뒤 " + behind + "명";
        public static string LadderRemaining(int n) => "남은 인원 " + n + "명";
        public const string LadderCall = "콜(올인)";
        public static string LadderFacing(string name, string pos, double bb) => name + " (" + pos + ") 올인 " + bb.ToString("F1") + "bb — BB + 앤티를 내고 콜할까?";
        public static string LadderCallAdvice(double top, double bb, string pos) => pos + " 올인에 콜 · " + bb.ToString("F1") + "bb → 콜 권장 상위 " + top.ToString("F0") + "%";
        public const string LadderFreeBlind = "BB — 앞사람이 전원 폴드. 블라인드 반환 +0.5bb";
        public const string LadderForced = "스택 1bb 미만 — 자동 올인";
        public static string LadderStole(double bb) => "아무도 콜하지 않았다  +" + bb.ToString("F1") + "bb";
        public static string LadderCalledBy(string name) => name + " 콜";
        public const string LadderWon = "승리 · 승수 +1";
        public const string LadderSurvived = "졌다 — 칩이 남았다";
        public const string LadderOut = "탈락";
        public const string LadderSplit = "무승부";
        public static string LadderFoldedLine(string pos, string hand) => pos + " " + hand + " 폴드";
        public static string LadderAdvice(double top, double bb, string pos) => pos + " · " + bb.ToString("F1") + "bb → 푸시 권장 상위 " + top.ToString("F0") + "%";
        public const string LadderNoAdviceBb = "BB 자리는 선택이 없다";
        public static string LadderStageReached(string stage) => stage + " 달성";
        public static string LadderResultStage(string stage) => string.IsNullOrEmpty(stage) ? "입상 실패" : stage;
        public const string LadderWinsResult = "최고 승수";
        public const string LadderSteals = "스틸";
        public const string LadderCalledAllIns = "콜 받은 올인";
        public const string HoldemWin = "승리";
        public const string HoldemQuick = "빠르게";
        public static string HoldemEquity(double eq) => "에퀴티 " + (eq * 100).ToString("F0") + "%";

        // D (v0.4) life: age, bankruptcy (E9), voyage, health — all placeholder
        public static string AgeLabel(int age) => age + "세";
        public const string AtSea = "배 위다. 아무것도 할 수 없다.";
        public const string VoyageForced = "출항이 정해졌다.";
        public const string VoyageNotBankrupt = "파산 상태가 아니다.";
        public const string E9Title = "파산";
        public const string E9Body = "월세를 내지 못했다.";
        public static string E9Held(int days) => days > 0 ? days + "일째 버티는 중이다." : "";
        public const string E9Forced = "더는 버틸 수 없다. 배를 탈 수밖에 없다.";
        public const string E9Board = "원양어선을 탄다";
        public const string E9Hold = "버틴다";
        public const string E9Bankrupt = "파산 — 통장 잔고가 바닥났다.";
        public const string VoyageConfirmTitle = "출항 전";
        public const string VoyageConfirmBody = "6개월간 폰 없음 — 포지션을 정리하시겠습니까?";
        public const string VoyageClose = "정리";
        public const string VoyageLeave = "그대로 둔다";
        public const string VoyageBoardTitle = "승선";
        public static readonly string[] VoyageBoardLines =
        {
            "부두에 서 있다. 짐은 가방 하나.",
            "휴대폰은 선사에 맡겼다.",
            "배가 뜬다. 육지가 작아진다.",
        };
        public static readonly string[] VoyageSkipLines =
        {
            "1개월. 파도. 그물. 파도.",
            "3개월. 날짜 세는 걸 그만뒀다.",
            "5개월. 허리가 나간 것 같다.",
            "6개월. 항구가 보인다.",
        };
        public const string VoyageReturnTitle = "귀항";
        public static string VoyageReturnPay(long pay) => "통장에 " + Fmt.Won(pay) + " 입금.";
        public const string VoyageMirror = "거울 속 얼굴이 낯설다. 몸이 예전 같지 않다.";
        public const string VoyageBankExpired = "원룸 계약은 해지됐다. 고시원에서 다시 시작한다.";
        public static string VoyageAge(int age) => "나이 " + age + "세.";
        public const string VoyagePositionNone = "포지션은 없다.";
        public static string VoyagePositionOpen(long value) => "방치한 포지션이 아직 살아 있다. 평가액 " + Fmt.Won(value);
        public static string VoyagePositionLiquidated(long lost) => "방치한 포지션은 청산됐다. " + Fmt.Won(-lost);
        public const string SickTitle = "병원";
        public static string SickBody(long cost, int days) => "몸이 무너졌다. 병원비 " + Fmt.Won(cost) + ", " + days + "일 동안 누워 있는다.";
        public const string ActCheckup = "검진·운동";
        public const string CheckupDone = "검진을 받고 운동했다.";
        public const string CheckupNoCash = "검진받을 돈이 없다.";
        public static string CheckupEffect(int progress, int of) => "건강 관리 " + progress + "/" + of;
        public const string BankruptStatus = "월세 미납";
        public const string Starved = "식비가 없어 굶었다.";
        public static string StarvedExchange(long n) => "거래소엔 " + Fmt.Won(n) + " 있음.";
        public const string HungerTitle = "굶주림";
        public static string HungerBody(int days, long onExchange) => days + "일째 굶고 있다." + (onExchange >= Rules.FoodPerDay ? "\n" + StarvedExchange(onExchange) : "");
        public static string E9Owed(long owed) => "밀린 월세 " + Fmt.Won(owed);
        public static string E9Exchange(long n) => "거래소에 " + Fmt.Won(n) + " 있음 (자동으로 처분되지 않는다)";
        public static string BankruptCountdown(int held, int limit) => "월세 미납 D+" + held + " / " + limit;
        public const string BankruptCleared = "밀린 월세를 냈다. 파산에서 벗어났다.";
        public const string HealthDebt = "건강 부채";
        public const string BodyMax = "체력 최대치";
        public const string Age = "나이";
        public const string VoyageCount = "원양어선 승선";
        public const string E7Continue = "계속 살기";
        public static string E7TitleMonth(int month) => month <= 1 ? E7Title : month + "개월차 정산";

        // HUD
        public const string Assets = "총자산";
        public const string Body = "체력";
        public const string Mental = "멘탈";
        public const string NewLife = "새 인생";
        public const string NewLifeConfirm = "세이브를 지우고 전역일로 돌아간다. 되돌릴 수 없다.";
        public const string Yes = "예";
        public const string No = "아니오";
    }
}
