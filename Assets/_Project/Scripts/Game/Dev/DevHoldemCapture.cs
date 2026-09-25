using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using AllIn.Game.HoldemScene;
using AllIn.Holdem;
using AllIn.Holdem.Brain;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AllIn.Game.Dev
{
    /// <summary>
    /// Dev-only: launched with <c>-allinHoldemShots &lt;dir&gt;</c>, opens Holdem.unity, lets a Pro bot play the hero seat through
    /// a Pub tournament at study level 4 (then a few hands at level 0), screenshots each kind of moment once, and quits.
    /// </summary>
    public sealed class DevHoldemCapture : MonoBehaviour
    {
        string _dir;
        int _shot;
        readonly HashSet<string> _seen = new HashSet<string>();

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
            SceneManager.LoadScene(HoldemApp.SceneName);
            HoldemApp app = null;
            while ((app = FindAnyObjectByType<HoldemApp>()) == null) yield return null;
            yield return new WaitForSecondsRealtime(0.5f);
            yield return Shot("menu");

            Time.timeScale = 4f;
            app.AutoHero = BotFactory.Instance.Create(PersonalityId.Pro);
            app.OnMoment = Moment;
            app.StartTournament(TournamentKind.Pub, 4, 7);
            while (!app.AtResult) yield return null;
            yield return new WaitForSecondsRealtime(0.3f);
            yield return Shot("result_study4");

            _seen.Clear();
            app.StartTournament(TournamentKind.Main, 0, 11);
            float until = Time.realtimeSinceStartup + 25f;
            while (!app.AtResult && Time.realtimeSinceStartup < until && _seen.Count < 3) yield return null;
            Time.timeScale = 1f;
            Application.Quit();
        }

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
