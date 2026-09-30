using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using AllIn.Game.HoldemScene;
using AllIn.Holdem;
using HoldemLadder = AllIn.Holdem.Ladder;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AllIn.Game.Dev
{
    /// <summary>
    /// Dev-only: launched with <c>-allinHoldemShots &lt;dir&gt;</c>, opens Holdem.unity (v2 ladder), lets a scripted hero play
    /// (push-heavy so calls, steals, cover-survivals and the bubble show up quickly), screenshots each kind of moment once, and quits.
    /// </summary>
    public sealed class DevHoldemCapture : MonoBehaviour
    {
        string _dir;
        int _shot;
        readonly HashSet<string> _seen = new HashSet<string>();

        /// <summary>Pushes every hand except the very worst: enough calls and steals to see every scene.</summary>
        sealed class PushHappy : ILadderPlayer
        {
            public bool AllIn(LadderHand h) => HoldemLadder.HandPercentile(h) <= 60;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Arm()
        {
            var args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, "-allinHoldemShots");
            if (i < 0 || i + 1 >= args.Length) return;
            Directory.CreateDirectory(args[i + 1]);
            SaveIO.DirectoryOverride = args[i + 1]; // the Loop scene boots first; keep it off the real save
            Application.runInBackground = true;
            var go = new GameObject("DevHoldemCapture");
            DontDestroyOnLoad(go);
            go.AddComponent<DevHoldemCapture>()._dir = args[i + 1];
        }

        IEnumerator Start()
        {
            yield return null;
            SceneManager.LoadScene(LadderApp.SceneName);
            LadderApp app = null;
            while ((app = FindAnyObjectByType<LadderApp>()) == null) yield return null;
            yield return new WaitForSecondsRealtime(0.5f);
            yield return Shot("menu");

            Time.timeScale = 4f;
            app.AutoHero = new PushHappy();
            app.OnMoment = Moment;
            // Pub at study 4 (HUD advice), then a Main at study 2 (ladder stages), then other seeds until the key scenes were all seen
            long[] seeds = { 7, 21, 33, 44, 58, 61, 72, 85 };
            int n = 0;
            app.StartTournament(TournamentKind.Pub, 4, seeds[n++]);
            float until = Time.realtimeSinceStartup + 120f;
            while (Time.realtimeSinceStartup < until)
            {
                if (app.AtResult)
                {
                    yield return new WaitForSecondsRealtime(0.2f);
                    if (n >= seeds.Length || Enough()) break;
                    bool main = n % 2 == 1;
                    app.StartTournament(main ? TournamentKind.Main : TournamentKind.Pub, main ? 2 : 3, seeds[n++]);
                }
                yield return null;
            }
            Time.timeScale = 1f;
            Application.Quit();
        }

        bool Enough() => _seen.Contains("steal") && _seen.Contains("call_won") && _seen.Contains("call_survived") && _seen.Contains("call_out") && _seen.Contains("bubble") && _seen.Contains("result");

        IEnumerator Moment(string name)
        {
            if (!_seen.Add(name)) yield break;
            yield return Shot(name);
        }

        IEnumerator Shot(string name)
        {
            yield return new WaitForEndOfFrame();
            ScreenCapture.CaptureScreenshot(Path.Combine(_dir, (++_shot).ToString("00") + "_" + name + ".png"));
            yield return null;
        }
    }
}
