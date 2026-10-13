using System.Collections;
using System.IO;
using UnityEngine;

namespace Emberbond
{
    /// <summary>
    /// Takes high-resolution screenshots from a few camera poses and logs frame times, then writes a marker file so a
    /// command-line run can end. Only active when the process was started with -emberShots.
    /// </summary>
    public sealed class ShotRunner : MonoBehaviour
    {
        private struct Pose { public Vector3 Pos; public Vector3 Euler; public string Name; }

        private IEnumerator Start()
        {
            string[] args = System.Environment.GetCommandLineArgs();
            bool wanted = false;
            for (int i = 0; i < args.Length; i++) if (args[i] == "-emberShots") wanted = true;
            if (!wanted) yield break;

            string dir = Path.Combine(Directory.GetCurrentDirectory(), "shots");
            Directory.CreateDirectory(dir);
            int width = 1920, height = 1080;
            foreach (string a in args) if (a == "-4k") { width = 3840; height = 2160; }
            Camera cam = Camera.main;
            Vector3 city = new Vector3(-540f, 110f, 660f);
            Terrain tr = Terrain.activeTerrain;
            System.Func<Vector3, float, Vector3> above = (p, h) => new Vector3(p.x, (tr != null ? tr.SampleHeight(p) + tr.transform.position.y : 100f) + h, p.z);
            Vector3 seaDir = new Vector3(-0.866f, 0f, 0.5f);
            Vector3 p1 = above(city + seaDir * 560f, 90f);
            Vector3 p2 = above(city + new Vector3(330f, 0f, -240f), 70f);
            Vector3 p3 = above(city + new Vector3(-30f, 0f, -290f), 45f);
            Vector3 p4 = above(city + seaDir * 150f, 14f);
            Pose[] poses =
            {
                new Pose { Name = "sky_up",     Pos = p4 + Vector3.up * 40f, Euler = new Vector3(-28f, 40f, 0f) },
                new Pose { Name = "city_sea",   Pos = p1, Euler = Quaternion.LookRotation(city + Vector3.up * 40f - p1).eulerAngles },
                new Pose { Name = "city_hill",  Pos = p2, Euler = Quaternion.LookRotation(city - p2).eulerAngles },
                new Pose { Name = "city_back",  Pos = p3, Euler = Quaternion.LookRotation(city + Vector3.up * 25f - p3).eulerAngles },
                new Pose { Name = "city_gate",  Pos = p4, Euler = Quaternion.LookRotation(city + Vector3.up * 20f - p4).eulerAngles },
                new Pose { Name = "coast",   Pos = new Vector3(-420f, 220f, -980f), Euler = new Vector3(8f, 28f, 0f) },
                new Pose { Name = "volcano", Pos = new Vector3(-100f, 330f, -300f), Euler = new Vector3(6f, 62f, 0f) },
                new Pose { Name = "dragon",  Pos = new Vector3(-330f, 160f, -900f), Euler = new Vector3(4f, -18f, 0f) },
                new Pose { Name = "ground",  Pos = new Vector3(-300f, 110f, -850f), Euler = new Vector3(4f, 80f, 0f) },
            };

            // Let exposure, clouds and the water settle.
            for (int i = 0; i < 240; i++) yield return null;

            RenderTexture rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
            float worst = 0f, sum = 0f; int frames = 0;
            foreach (Pose p in poses)
            {
                cam.transform.position = p.Pos;
                cam.transform.rotation = Quaternion.Euler(p.Euler);
                for (int i = 0; i < 90; i++)
                {
                    yield return null;
                    float dt = Time.unscaledDeltaTime; sum += dt; frames++; worst = Mathf.Max(worst, dt);
                }
                cam.targetTexture = rt;
                yield return null;
                yield return new WaitForEndOfFrame();
                RenderTexture.active = rt;
                Texture2D tex = new Texture2D(width, height, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                tex.Apply();
                File.WriteAllBytes(Path.Combine(dir, p.Name + ".png"), tex.EncodeToPNG());
                Destroy(tex);
                RenderTexture.active = null;
                cam.targetTexture = null;
                Debug.Log("[Emberbond] shot " + p.Name);
            }
            Debug.Log("[Emberbond] frames=" + frames + " avg=" + (sum / Mathf.Max(1, frames) * 1000f).ToString("0.0") +
                      " ms worst=" + (worst * 1000f).ToString("0.0") + " ms");
            File.WriteAllText(Path.Combine(dir, "done.txt"), "ok");
            Application.Quit(0);
        }
    }
}
