using System;
using UnityEngine;

namespace BlockRealm.Game
{
    /// <summary>
    /// Builds the whole game at start-up from code, so the project needs no hand-authored scene: whichever scene
    /// loads first gets a camera, sky, world renderer, entity views, sound, interface and session.
    /// </summary>
    public static class GameBootstrap
    {
        private static bool _started;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Boot()
        {
            if (_started) return;
            _started = true;
            Application.runInBackground = true;
            Application.targetFrameRate = -1;
            QualitySettings.vSyncCount = 1;
            QualitySettings.antiAliasing = 4;
            QualitySettings.anisotropicFiltering = AnisotropicFiltering.Disable;

            string[] args = Environment.GetCommandLineArgs();
            GameObject root = new GameObject("BlockRealm");
            UnityEngine.Object.DontDestroyOnLoad(root);

            if (DedicatedServer.WantsServer(args))
            {
                DedicatedServer server = root.AddComponent<DedicatedServer>();
                server.Begin(args);
                return;
            }

            GameAssets.Load();

            GameObject camGo = new GameObject("Main Camera");
            camGo.transform.SetParent(root.transform, false);
            camGo.tag = "MainCamera";
            Camera cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.Skybox;
            cam.nearClipPlane = 0.06f;
            cam.allowHDR = false;
            cam.fieldOfView = GameSettings.Fov;
            camGo.AddComponent<AudioListener>();
            camGo.AddComponent<ScreenFx>();

            SkyController sky = root.AddComponent<SkyController>();
            sky.Init(cam);

            AudioBank audio = root.AddComponent<AudioBank>();

            GameObject worldGo = new GameObject("World");
            worldGo.transform.SetParent(root.transform, false);
            WorldRenderer renderer = worldGo.AddComponent<WorldRenderer>();
            renderer.Viewer = cam.transform;

            GameObject entitiesGo = new GameObject("Entities");
            entitiesGo.transform.SetParent(root.transform, false);
            EntityViews entities = entitiesGo.AddComponent<EntityViews>();

            GameObject fxGo = new GameObject("Effects");
            fxGo.transform.SetParent(root.transform, false);
            EffectsController effects = fxGo.AddComponent<EffectsController>();
            effects.Init(audio);

            GameSession session = root.AddComponent<GameSession>();
            session.Cam = cam;
            session.Renderer = renderer;
            session.Entities = entities;
            session.Effects = effects;
            session.Sky = sky;
            session.Audio = audio;

            PlayerView view = root.AddComponent<PlayerView>();
            view.Init(cam, null, session.Input, audio, effects, sky);
            session.View = view;

            GameUi ui = root.AddComponent<GameUi>();
            ui.Session = session;
            session.OnPlayerHurt = ui.NotifyHurt;
            if (SmokeTest.Wanted(args))
            {
                SmokeTest smoke = root.AddComponent<SmokeTest>();
                smoke.Session = session;
                smoke.Begin(args);
            }
        }
    }
}
