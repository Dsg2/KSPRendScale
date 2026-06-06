using System;
using System.IO;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using KSP.UI.Screens;

namespace RenderScaleMod
{
    // Runs once at game start, survives scene changes.
    [KSPAddon(KSPAddon.Startup.Instantly, true)]
    public class RenderScaleAddon : MonoBehaviour
    {
        private static RenderScaleAddon instance;

        // Render scale, 0.25 .. 1.00. We only ever downscale.
        private float scale = 1.0f;
        private const float MinScale = 0.25f;
        private const float MaxScale = 1.0f;

        private bool showWindow = false;
        private Rect windowRect = new Rect(220f, 160f, 300f, 10f);
        private float reapplyTimer = 0f;

        private ApplicationLauncherButton appButton;
        private Texture2D buttonTex;

        // ---- Route A state ----
        private RenderTexture rt;
        private bool rtHDR;
        private Camera blitCamera;
        private RenderScaleBlitter blitter;
        private readonly List<Camera> redirected = new List<Camera>();

        // ---- lifecycle ----------------------------------------------------

        private void Awake()
        {
            if (instance != null) { Destroy(gameObject); return; }
            instance = this;
            DontDestroyOnLoad(gameObject);
            Load();
        }

        private void Start()
        {
            GameEvents.onGUIApplicationLauncherReady.Add(OnAppLauncherReady);
            GameEvents.onLevelWasLoaded.Add(OnLevelLoaded);
            ApplyScale();
        }

        private void OnDestroy()
        {
            GameEvents.onGUIApplicationLauncherReady.Remove(OnAppLauncherReady);
            GameEvents.onLevelWasLoaded.Remove(OnLevelLoaded);
            RemoveButton();
            Teardown();
            if (blitCamera != null) { Destroy(blitCamera.gameObject); blitCamera = null; }
            if (instance == this) instance = null;
        }

        private void OnLevelLoaded(GameScenes scene)
        {
            // Cameras are rebuilt on scene loads; re-apply so the new ones get redirected.
            ApplyScale();
        }

        private void Update()
        {
            // Hotkey: Alt+R toggles the window.
            if ((Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt)) && Input.GetKeyDown(KeyCode.R))
                ToggleWindow();

            // While active, periodically re-assert: catches cameras spawned by other
            // mods (Scatterer/Deferred/EVE) and a resolution change.
            if (scale < 0.999f)
            {
                reapplyTimer += Time.unscaledDeltaTime;
                if (reapplyTimer > 1.5f)
                {
                    reapplyTimer = 0f;
                    ApplyScale();
                }
            }
        }

        // ---- core: Route A supersample/downsample ------------------------

        private void ApplyScale()
        {
            if (scale >= 0.999f) { Teardown(); return; }

            // 1) Gather the world cameras that render straight to the screen.
            //    Skip: UI cameras, our blit cam, and cameras with their own target
            //    (Kerbal portraits, reflection probes, map thumbnails).
            List<Camera> world = new List<Camera>();
            float maxWorldDepth = float.NegativeInfinity;
            float minUIDepth = float.PositiveInfinity;
            bool hdr = false;

            Camera[] cams = Camera.allCameras;
            for (int i = 0; i < cams.Length; i++)
            {
                Camera c = cams[i];
                if (c == null) continue;
                if (blitCamera != null && c == blitCamera) continue;

                bool isUI = c.name != null && c.name.IndexOf("UI", StringComparison.OrdinalIgnoreCase) >= 0;
                if (isUI)
                {
                    if (c.targetTexture == null && c.depth < minUIDepth) minUIDepth = c.depth;
                    continue;
                }

                if (c.targetTexture != null && c.targetTexture != rt) continue; // foreign target -> leave alone

                world.Add(c);
                if (c.depth > maxWorldDepth) maxWorldDepth = c.depth;
                if (c.allowHDR) hdr = true;
            }

            if (world.Count == 0) return;

            // 2) Make sure the shared render texture matches screen size & HDR.
            EnsureRenderTexture(hdr);

            // 3) Point every world camera at the shared RT.
            redirected.Clear();
            for (int i = 0; i < world.Count; i++)
            {
                world[i].targetTexture = rt;
                redirected.Add(world[i]);
            }

            // 4) Position the blit camera just above the world cams but below UI.
            float blitDepth = maxWorldDepth + 0.5f;
            if (minUIDepth < float.PositiveInfinity && blitDepth >= minUIDepth)
                blitDepth = minUIDepth - 0.1f;
            if (blitDepth <= maxWorldDepth) blitDepth = maxWorldDepth + 0.1f;

            EnsureBlitCamera();
            blitCamera.depth = blitDepth;
            blitter.source = rt;
            blitCamera.enabled = true;
        }

        private void EnsureRenderTexture(bool hdr)
        {
            int w = Mathf.Max(1, Mathf.RoundToInt(Screen.width * scale));
            int h = Mathf.Max(1, Mathf.RoundToInt(Screen.height * scale));

            if (rt != null && (rt.width != w || rt.height != h || rtHDR != hdr))
                ReleaseRT();

            if (rt == null)
            {
                RenderTextureFormat fmt = hdr ? RenderTextureFormat.DefaultHDR : RenderTextureFormat.Default;
                rt = new RenderTexture(w, h, 24, fmt);
                rt.name = "RenderScaleRT";
                rt.filterMode = FilterMode.Bilinear; // smooth upscale to screen
                rt.antiAliasing = 1;
                rt.Create();
                rtHDR = hdr;
                Debug.Log(string.Format("[RenderScale] RT {0}x{1} hdr={2} (scale {3:0.##})", w, h, hdr, scale));
            }
        }

        private void EnsureBlitCamera()
        {
            if (blitCamera != null) return;
            GameObject go = new GameObject("RenderScaleBlitCam");
            DontDestroyOnLoad(go);
            blitCamera = go.AddComponent<Camera>();
            blitCamera.clearFlags = CameraClearFlags.Nothing;
            blitCamera.cullingMask = 0;
            blitCamera.useOcclusionCulling = false;
            blitCamera.allowHDR = false;
            blitCamera.allowMSAA = false;
            blitCamera.targetTexture = null; // renders to the backbuffer
            blitter = go.AddComponent<RenderScaleBlitter>();
        }

        private void Teardown()
        {
            // Detach any camera still pointing at our RT (covers cams from prior scenes).
            Camera[] cams = Camera.allCameras;
            for (int i = 0; i < cams.Length; i++)
            {
                Camera c = cams[i];
                if (c != null && c.targetTexture == rt && rt != null)
                    c.targetTexture = null;
            }
            redirected.Clear();
            if (blitter != null) blitter.source = null;
            if (blitCamera != null) blitCamera.enabled = false;
            ReleaseRT();
        }

        private void ReleaseRT()
        {
            if (rt == null) return;
            rt.Release();
            Destroy(rt);
            rt = null;
        }

        // ---- UI ----------------------------------------------------------

        private void ToggleWindow()
        {
            showWindow = !showWindow;
            if (!showWindow) Save();
            if (appButton != null)
            {
                if (showWindow) appButton.SetTrue(false);
                else appButton.SetFalse(false);
            }
        }

        private void OnGUI()
        {
            if (!showWindow) return;
            GUI.skin = HighLogic.Skin;
            windowRect = GUILayout.Window(GetType().FullName.GetHashCode(), windowRect, DrawWindow, "Render Scale");
        }

        private void DrawWindow(int id)
        {
            GUILayout.BeginVertical();

            int pct = Mathf.RoundToInt(scale * 100f);
            int rw = Mathf.RoundToInt(Screen.width * scale);
            int rh = Mathf.RoundToInt(Screen.height * scale);
            GUILayout.Label(string.Format("Scale: {0}%   ({1} x {2})", pct, rw, rh));

            float newScale = GUILayout.HorizontalSlider(scale, MinScale, MaxScale);
            newScale = Mathf.Round(newScale * 20f) / 20f; // snap to 5% steps

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("50%")) newScale = 0.50f;
            if (GUILayout.Button("75%")) newScale = 0.75f;
            if (GUILayout.Button("100%")) newScale = 1.00f;
            GUILayout.EndHorizontal();

            if (Mathf.Abs(newScale - scale) > 0.001f)
            {
                scale = Mathf.Clamp(newScale, MinScale, MaxScale);
                ApplyScale();
            }

            GUILayout.Space(4f);
            if (GUILayout.Button("Re-apply"))
                ApplyScale();
            if (GUILayout.Button("Save & Close"))
            {
                Save();
                ToggleWindow();
            }

            GUILayout.EndVertical();
            GUI.DragWindow();
        }

        // ---- toolbar button ----------------------------------------------

        private void OnAppLauncherReady()
        {
            if (appButton != null) return;
            if (buttonTex == null) buttonTex = MakeIcon();
            appButton = ApplicationLauncher.Instance.AddModApplication(
                OnButtonTrue, OnButtonFalse,
                null, null, null, null,
                ApplicationLauncher.AppScenes.ALWAYS,
                buttonTex);
        }

        private void OnButtonTrue() { showWindow = true; }
        private void OnButtonFalse() { showWindow = false; Save(); }

        private void RemoveButton()
        {
            if (appButton != null && ApplicationLauncher.Instance != null)
                ApplicationLauncher.Instance.RemoveModApplication(appButton);
            appButton = null;
        }

        private Texture2D MakeIcon()
        {
            const int size = 38;
            Texture2D t = new Texture2D(size, size, TextureFormat.ARGB32, false);
            Color bg = new Color(0.10f, 0.12f, 0.16f, 1f);
            Color fg = new Color(0.30f, 0.80f, 1.00f, 1f);
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    bool border = x < 2 || y < 2 || x >= size - 2 || y >= size - 2;
                    bool block = (((x / 6) + (y / 6)) % 2) == 0; // coarse pixels = "low res"
                    Color c = border ? fg : (block ? fg * 0.55f : bg);
                    c.a = 1f;
                    t.SetPixel(x, y, c);
                }
            }
            t.Apply();
            return t;
        }

        // ---- persistence -------------------------------------------------

        private string SettingsDir()
        {
            return Path.Combine(KSPUtil.ApplicationRootPath, "GameData/RenderScale/PluginData");
        }

        private void Load()
        {
            try
            {
                string path = Path.Combine(SettingsDir(), "settings.cfg");
                if (!File.Exists(path)) return;
                string[] lines = File.ReadAllLines(path);
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i].Trim();
                    int eq = line.IndexOf('=');
                    if (eq < 0) continue;
                    string key = line.Substring(0, eq).Trim();
                    string val = line.Substring(eq + 1).Trim();
                    if (key == "scale")
                    {
                        float f;
                        if (float.TryParse(val, NumberStyles.Float, CultureInfo.InvariantCulture, out f))
                            scale = Mathf.Clamp(f, MinScale, MaxScale);
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogError("[RenderScale] Load failed: " + e);
            }
        }

        private void Save()
        {
            try
            {
                string dir = SettingsDir();
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir, "settings.cfg"),
                    "scale = " + scale.ToString(CultureInfo.InvariantCulture) + Environment.NewLine);
            }
            catch (Exception e)
            {
                Debug.LogError("[RenderScale] Save failed: " + e);
            }
        }
    }

    // Sits on the blit camera. Its OnRenderImage runs after the world cameras
    // (lower depth) have filled the shared RT, and writes that RT to the
    // backbuffer (dst) scaled up to native resolution.
    public class RenderScaleBlitter : MonoBehaviour
    {
        public RenderTexture source;

        private void OnRenderImage(RenderTexture src, RenderTexture dst)
        {
            if (source != null)
                Graphics.Blit(source, dst);
            else
                Graphics.Blit(src, dst);
        }
    }
}
