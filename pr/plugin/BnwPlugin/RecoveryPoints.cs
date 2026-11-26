using System;
using System.Collections.Generic;
using Last.Entity.Field;
using Last.Map;
using UnityEngine;
using Hooks = PrKit.Hooks;
using Object = UnityEngine.Object;

namespace BraveNewWorld.PR
{
    // BNW's full-recovery save points look different: their save-point sprite uses map palette 5 (the plain ones 6),
    // which turns the sparkle's blue-violet slots (palette 6: 6-9) white and pink-red (palette 5) (BNW ROM E6/8000).
    // The port puts a step-on trigger running sc_bnw_recovery (script 9964: the full recovery) on each of those save
    // points (tools/port_bnw_mechanics.py recovery_points); the PR's sparkle there is its own art (effect object, type
    // 10, asset 256) with no palette, so it's recolored here: on a field map, each recovery trigger's nearest type-10
    // effect gets its red and blue swapped (texture, material colors, particle colors), so the PR's blue-violet sparkle
    // turns pink-red and its white core stays white, as BNW's. (Until 0.26.9 it was multiplied by a pink tint, which
    // left it violet: the blue art has no red to raise; the user, 2026-11-25: "make them red".)
    //
    // The heal: the user's Phantom Train visit (2026-11-25, "i'm pretty sure it didn't heal me") — the port's trigger
    // shares its cell with the PR's own save-point object (type 9), and QA's heal check ran the script directly, never
    // the step. So the plugin starts sc_bnw_recovery itself when the party steps onto a recovery cell (not on arriving
    // in the map on it), unless a script already runs (the trigger's own, if the game ran it).
    static class RecoveryPoints
    {
        internal static bool Enabled = true;
        internal const int RecoveryScript = 9964;
        const string ScriptName = "sc_bnw_recovery";
        // world units between the trigger and its sparkle: the field's units are pixels (16 a cell; the sparkle object
        // sits 8 px above its save cell: QA's PC run 2026-11-21, "nearest 8")
        const float MaxDistance = 24f;
        static int lastMap = -1, lastCount = -1, frame;
        static readonly List<Vector2Int> cells = new List<Vector2Int>();
        static readonly HashSet<int> recolored = new HashSet<int>();
        static readonly Dictionary<IntPtr, Texture2D> swappedTextures = new Dictionary<IntPtr, Texture2D>();
        static bool wasOn, armed;
        internal static int Tinted, Heals;
        internal static string Last = "";

        static Color Swap(Color c) => new Color(c.b, c.g, c.r, c.a);

        static void SwapGradient(Gradient g)
        {
            if (g == null) return;
            var keys = g.colorKeys;
            if (keys == null) return;
            for (int i = 0; i < keys.Length; i++)
            {
                var k = keys[i];
                k.color = Swap(k.color);
                keys[i] = k;
            }
            g.colorKeys = keys;
        }

        // the game's interop has MinMaxGradient's fields only (color = m_ColorMax, gradient = m_GradientMax): all four
        // swapped, whatever the mode
        static ParticleSystem.MinMaxGradient Swap(ParticleSystem.MinMaxGradient m)
        {
            m.m_ColorMin = Swap(m.m_ColorMin);
            m.m_ColorMax = Swap(m.m_ColorMax);
            SwapGradient(m.m_GradientMin);
            SwapGradient(m.m_GradientMax);
            return m;
        }

        // a copy of the texture with red and blue swapped (the game's textures aren't readable: through a RenderTexture)
        static Texture2D Swapped(Texture src)
        {
            if (swappedTextures.TryGetValue(src.Pointer, out var done) && done != null) return done;
            int w = src.width, h = src.height;
            var rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32);
            var prev = RenderTexture.active;
            Texture2D t;
            try
            {
                Graphics.Blit(src, rt);
                RenderTexture.active = rt;
                t = new Texture2D(w, h, TextureFormat.RGBA32, false);
                t.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            }
            finally
            {
                RenderTexture.active = prev;
                RenderTexture.ReleaseTemporary(rt);
            }
            var px = t.GetPixels32();
            for (int i = 0; i < px.Length; i++)
            {
                var c = px[i];
                px[i] = new Color32(c.b, c.g, c.r, c.a);
            }
            t.SetPixels32(px);
            t.filterMode = src.filterMode;
            t.wrapMode = src.wrapMode;
            t.Apply(false, false);
            t.hideFlags = HideFlags.DontUnloadUnusedAsset;
            swappedTextures[src.Pointer] = t;
            return t;
        }

        static void RecolorMaterial(Material m)
        {
            if (m == null) return;
            try
            {
                var tex = m.mainTexture;
                if (tex != null) m.mainTexture = Swapped(tex);
            }
            catch (Exception e) { Hooks.WarnOnce("RecoveryPoints.texture", e); }
            try
            {
                var sh = m.shader;
                int n = sh.GetPropertyCount();
                for (int i = 0; i < n; i++)
                    if (sh.GetPropertyType(i) == UnityEngine.Rendering.ShaderPropertyType.Color)
                    {
                        var name = sh.GetPropertyName(i);
                        m.SetColor(name, Swap(m.GetColor(name)));
                    }
            }
            catch (Exception e)
            {
                Hooks.WarnOnce("RecoveryPoints.colors", e);
                if (m.HasProperty("_Color")) m.SetColor("_Color", Swap(m.GetColor("_Color")));
                if (m.HasProperty("_TintColor")) m.SetColor("_TintColor", Swap(m.GetColor("_TintColor")));
            }
        }

        // The PR's save-point sparkle is an animated map object (FieldMapObjectAnimDefault, object type 10): a
        // SpriteRenderer whose sprite the animation swaps between frames of the atlas MO_FF6_B062 (QA's bnwfx dump,
        // 2026-11-25), shared with every plain save point. So each recovery sparkle's renderer is listed, and after the
        // animation (LateUpdate) its current frame is replaced by a red copy: the frame's rect of a red/blue-swapped copy
        // of the atlas, made once per frame sprite (per map: freed on a map change).
        static readonly List<SpriteRenderer> redRenderers = new List<SpriteRenderer>();
        static readonly Dictionary<IntPtr, Sprite> redSprites = new Dictionary<IntPtr, Sprite>();
        static readonly HashSet<IntPtr> ours = new HashSet<IntPtr>();
        internal static int FramesSwapped;

        static Sprite Red(Sprite s)
        {
            if (redSprites.TryGetValue(s.Pointer, out var done)) return done;
            Sprite red = null;
            try
            {
                var tex = Swapped(s.texture);
                // the sparkle's frames (Default_0n(Clone)) are the game's runtime sprites: rect and textureRect read 0x0
                // (QA bnwfx, 2026-11-25), so the frame's area comes from its UVs and its size and pivot from its bounds
                var uv = s.uv;
                Rect tr;
                Vector2 pivot;
                float ppu;
                if (uv != null && uv.Length > 0)
                {
                    float u0 = 1, v0 = 1, u1 = 0, v1 = 0;
                    for (int i = 0; i < uv.Length; i++)
                    {
                        u0 = Mathf.Min(u0, uv[i].x); v0 = Mathf.Min(v0, uv[i].y);
                        u1 = Mathf.Max(u1, uv[i].x); v1 = Mathf.Max(v1, uv[i].y);
                    }
                    tr = new Rect(Mathf.Round(u0 * tex.width), Mathf.Round(v0 * tex.height),
                                  Mathf.Round((u1 - u0) * tex.width), Mathf.Round((v1 - v0) * tex.height));
                    var b = s.bounds;
                    ppu = b.size.x > 0 ? tr.width / b.size.x : s.pixelsPerUnit;
                    pivot = new Vector2(b.size.x > 0 ? -b.min.x / b.size.x : 0.5f, b.size.y > 0 ? -b.min.y / b.size.y : 0.5f);
                }
                else
                {
                    tr = s.textureRect;
                    pivot = (s.pivot - s.textureRectOffset) / tr.size;
                    ppu = s.pixelsPerUnit;
                }
                if (tr.width < 1 || tr.height < 1) throw new InvalidOperationException($"{s.name}: no area (uv {uv?.Length ?? 0}, bounds {s.bounds.size})");
                red = Sprite.Create(tex, tr, pivot, ppu, 0, SpriteMeshType.FullRect);
                red.name = s.name + " (BNW recovery)";
                ours.Add(red.Pointer);
            }
            catch (Exception e) { Hooks.WarnOnce("RecoveryPoints.sprite", e); }
            redSprites[s.Pointer] = red;   // a frame that can't be copied stays as it is (not retried each frame)
            return red;
        }

        public static void LateUpdate()
        {
            if (!Enabled || redRenderers.Count == 0) return;
            try
            {
                for (int i = redRenderers.Count - 1; i >= 0; i--)
                {
                    var sr = redRenderers[i];
                    if (sr == null) { redRenderers.RemoveAt(i); continue; }
                    var s = sr.sprite;
                    if (s == null || ours.Contains(s.Pointer)) continue;
                    var red = Red(s);
                    if (red != null) { sr.sprite = red; FramesSwapped++; }
                }
            }
            catch (Exception e) { Hooks.WarnOnce("RecoveryPoints.late", e); }
        }

        static void FreeCopies()
        {
            redRenderers.Clear();
            foreach (var s in redSprites.Values) if (s != null) Object.Destroy(s);
            foreach (var t in swappedTextures.Values) if (t != null) Object.Destroy(t);
            redSprites.Clear();
            swappedTextures.Clear();
            ours.Clear();
        }

        // red and blue swapped in what colors the sparkle: a sprite renderer's frames (above), other renderers'
        // materials (main texture and color properties), and a particle system's start color. Once per object
        // (swapping twice undoes it).
        static void RecolorObject(GameObject go)
        {
            if (!recolored.Add(go.GetInstanceID())) return;
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                if (r == null) continue;
                var sr = r.TryCast<SpriteRenderer>();
                if (sr != null)
                {
                    // the sparkle's frames (the map's and the minimap's), not its shadow
                    if (sr.gameObject.GetComponent<FieldShadowEntity>() == null && !redRenderers.Contains(sr)) redRenderers.Add(sr);
                    continue;
                }
                RecolorMaterial(r.material);
            }
            foreach (var ps in go.GetComponentsInChildren<ParticleSystem>(true))
            {
                if (ps == null) continue;
                try
                {
                    var main = ps.main;
                    main.startColor = Swap(main.startColor);   // (color over lifetime: stripped from the game's build)
                    ps.Clear(true);   // particles already out keep their old color
                    ps.Play(true);
                }
                catch (Exception e) { Hooks.WarnOnce("RecoveryPoints.particles", e); }
            }
        }

        // harness: what draws near each recovery trigger (QA, 2026-11-25: the first recolor reached an object that
        // doesn't draw the sparkle)
        internal static void RegisterHarness() =>
            PrKit.Harness.Register("bnwfx", a => Dump(), "bnwfx: the renderers and particle systems within 40 px of each recovery trigger, with their objects' components (logged)");

        static string Path(Transform t)
        {
            var parts = new List<string>();
            for (int i = 0; t != null && i < 6; i++, t = t.parent) parts.Insert(0, t.name);
            return string.Join("/", parts);
        }

        static string Dump()
        {
            var triggers = new List<Vector3>();
            foreach (var e in Object.FindObjectsOfType<FieldEntity>())
            {
                var ev = e?.Property?.TryCast<PropertyEvent>();
                if (ev != null && ev.ScriptId == RecoveryScript) triggers.Add(e.transform.position);
            }
            if (triggers.Count == 0) return "bnwfx: no recovery trigger on this map";
            var lines = new List<string>();
            foreach (var r in Object.FindObjectsOfType<Renderer>())
            {
                if (r == null) continue;
                var p = r.transform.position;
                float d = float.MaxValue;
                foreach (var t in triggers) d = Mathf.Min(d, Vector2.Distance(new Vector2(p.x, p.y), new Vector2(t.x, t.y)));
                if (d > 40f) continue;
                var comps = new List<string>();
                foreach (var c in r.gameObject.GetComponents<Component>()) comps.Add(c.GetIl2CppType().Name);
                string extra = "";
                var sr = r.TryCast<SpriteRenderer>();
                if (sr != null) extra += $" sprite {(sr.sprite == null ? "-" : sr.sprite.name + " tex " + sr.sprite.texture?.name)} color {sr.color}";
                var m = r.sharedMaterial;
                if (m != null) extra += $" material {m.name} shader {m.shader?.name} tex {m.mainTexture?.name}" + (m.HasProperty("_Color") ? $" _Color {m.GetColor("_Color")}" : "") + (m.HasProperty("_TintColor") ? $" _TintColor {m.GetColor("_TintColor")}" : "");
                var ps = r.gameObject.GetComponent<ParticleSystem>();
                if (ps != null) extra += $" particles start {ps.main.startColor.m_ColorMax} mode {ps.main.startColor.m_Mode} alive {ps.particleCount}";
                lines.Add($"{Path(r.transform)} d {d:0.#} {r.GetIl2CppType().Name} enabled {r.enabled}/{r.gameObject.activeInHierarchy} layer {r.gameObject.layer} [{string.Join(",", comps)}]{extra}");
            }
            foreach (var sr in redRenderers)
            {
                if (sr == null) { lines.Add("red: (destroyed)"); continue; }
                var sp = sr.sprite;
                lines.Add($"red: {Path(sr.transform)} enabled {sr.enabled}/{sr.gameObject.activeInHierarchy} color {sr.color} bounds {sr.bounds.center}/{sr.bounds.size} " +
                          $"sprite {(sp == null ? "-" : sp.name + " rect " + sp.rect + " ppu " + sp.pixelsPerUnit + " pivot " + sp.pivot + " tex " + sp.texture?.name + " " + sp.texture?.width + "x" + sp.texture?.height)}");
            }
            foreach (var kv in redSprites)
            {
                var red = kv.Value;
                if (red == null) { lines.Add("red frame: (not made)"); continue; }
                var t = red.texture;
                int opaque = 0, n = 0;
                try
                {
                    var tr = red.textureRect;
                    var px = t.GetPixels32();
                    for (int y = (int)tr.y; y < (int)(tr.y + tr.height); y++)
                        for (int x = (int)tr.x; x < (int)(tr.x + tr.width); x++) { n++; if (px[y * t.width + x].a > 0) opaque++; }
                }
                catch (Exception e) { lines.Add("red frame pixels: " + e.Message); }
                lines.Add($"red frame: {red.name} rect {red.rect} textureRect {red.textureRect} ppu {red.pixelsPerUnit} pivot {red.pivot} tex {t?.width}x{t?.height}: {opaque} of {n} pixels with alpha");
            }
            var s = $"bnwfx: {redRenderers.Count} red sparkle renderer(s), {redSprites.Count} red frame(s), {FramesSwapped} frame swaps; {lines.Count} renderer(s) near {triggers.Count} trigger(s):\n  " + string.Join("\n  ", lines);
            Plugin.L.LogInfo(s);
            return s;
        }

        static Vector2Int Cell(FieldController fc, Vector3 world)
        {
            var c = fc.ConvertWorldPositionToCellPosition(world);
            return new Vector2Int(Mathf.RoundToInt(c.x), Mathf.RoundToInt(c.y));
        }

        public static void Update()
        {
            if (!Enabled) return;
            ++frame;
            try
            {
                if (frame % 6 == 0) CheckStep();
                if (frame % 30 == 0) Scan();
            }
            catch (Exception e) { Hooks.WarnOnce("RecoveryPoints", e); }
        }

        // the party stepped onto a recovery cell: start the full recovery (once per step onto it)
        static void CheckStep()
        {
            if (cells.Count == 0) { wasOn = false; return; }
            var udm = global::Last.Management.UserDataManager.Instance();
            if (udm == null || udm.CurrentMapId != lastMap) return;
            var fc = Object.FindObjectOfType<FieldMap>()?.fieldController;
            if (fc?.player == null) return;
            var at = Cell(fc, fc.player.transform.position);
            bool on = cells.Contains(at);
            if (on && !wasOn && armed)
            {
                if (PrKit.MapScripts.Busy)
                {
                    Hooks.NoteOnce("bnw-recovery-busy-" + lastMap, () => $"[events] recovery point at {at.x},{at.y} on map {lastMap}: a script runs (the trigger's own?), not started again");
                }
                else
                {
                    var why = PrKit.MapScripts.Start(ScriptName);
                    if (why == null) Heals++;
                    Plugin.L.LogInfo($"[events] recovery point at {at.x},{at.y} on map {lastMap}: {(why ?? "full recovery started")}");
                }
            }
            wasOn = on;
            armed = true;
        }

        static void Scan()
        {
            var udm = global::Last.Management.UserDataManager.Instance();
            if (udm == null) return;
            int map = udm.CurrentMapId;
            var ents = Object.FindObjectsOfType<FieldEntity>();
            if (ents == null) return;
            if (map == lastMap && ents.Length == lastCount) return;
            if (map != lastMap)
            {
                recolored.Clear();
                FreeCopies();
                wasOn = false;
                armed = false;   // arriving on the cell (a map load, a save loaded there) doesn't heal: stepping onto it does
            }
            lastMap = map;
            lastCount = ents.Length;
            cells.Clear();
            var fc = Object.FindObjectOfType<FieldMap>()?.fieldController;
            var triggers = new List<Vector3>();
            var effects = new List<FieldEntity>();
            int type10 = 0, effectClass = 0;
            foreach (var e in ents)
            {
                if (e == null) continue;
                var p = e.Property;
                var ev = p?.TryCast<PropertyEvent>();
                if (ev != null && ev.ScriptId == RecoveryScript) { triggers.Add(e.transform.position); continue; }
                // the sparkle: an effect entity (the PR's field effects are these two classes), or an object of type 10
                bool isEffect = e.TryCast<FieldEffectParticleEntity>() != null || e.TryCast<FieldEffectBoardEntity>() != null;
                bool is10 = p != null && p.ObjectType == 10;
                if (isEffect) effectClass++;
                if (is10) type10++;
                if (isEffect || is10) effects.Add(e);
            }
            if (triggers.Count == 0) return;
            if (fc != null)
                foreach (var t in triggers) cells.Add(Cell(fc, t));
            int n = 0;
            var dists = new List<string>();
            foreach (var t in triggers)
            {
                FieldEntity best = null;
                float bd = float.MaxValue;
                foreach (var fx in effects)
                {
                    var d = Vector2.Distance(new Vector2(fx.transform.position.x, fx.transform.position.y), new Vector2(t.x, t.y));
                    if (d < bd) { bd = d; best = fx; }
                }
                if (best == null || bd > MaxDistance) { dists.Add($"none within {MaxDistance} (nearest {(best == null ? "-" : bd.ToString("0.##"))})"); continue; }
                RecolorObject(best.gameObject);
                n++;
                dists.Add(bd.ToString("0.##"));
            }
            Tinted += n;
            Last = $"map {map}: {n} of {triggers.Count} recovery save point(s) red (trigger-sparkle distance {string.Join(", ", dists)}; " +
                   $"cells {string.Join(" ", cells)}; {ents.Length} field entities, {effectClass} effect entities, {type10} of object type 10)";
            Hooks.NoteOnce("bnw-recovery-" + map, () => "[events] recovery points: " + Last);
        }
    }
}
