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
    // 10, asset 256) with no palette, so it's tinted here: on a field map, each recovery trigger's nearest type-10
    // effect has its renderers tinted pink-red. Checked about twice a second, again when the map's entity count changes
    // (an event layer showing its save point).
    static class RecoveryPoints
    {
        internal static bool Enabled = true;
        internal const int RecoveryScript = 9964;
        internal static readonly Color Tint = new Color(1f, 0.55f, 0.7f, 1f);
        // world units between the trigger and its sparkle: the field's units are pixels (16 a cell; the sparkle object
        // sits 8 px above its save cell: QA's PC run 2026-11-21, "nearest 8")
        const float MaxDistance = 24f;
        static int lastMap = -1, lastCount = -1, frame;
        internal static int Tinted;
        internal static string Last = "";

        // a sprite's color, a material's _Color / _TintColor, and a particle system's start color (the PR's field
        // sparkle is a particle effect: FieldEffectParticleEntity)
        static void TintObject(GameObject go)
        {
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                if (r == null) continue;
                var sr = r.TryCast<SpriteRenderer>();
                if (sr != null) { sr.color = Tint; continue; }
                var m = r.material;
                if (m == null) continue;
                if (m.HasProperty("_Color")) m.SetColor("_Color", Tint);
                if (m.HasProperty("_TintColor")) m.SetColor("_TintColor", Tint);
            }
            foreach (var ps in go.GetComponentsInChildren<ParticleSystem>(true))
            {
                if (ps == null) continue;
                try
                {
                    var main = ps.main;
                    main.startColor = new ParticleSystem.MinMaxGradient(Tint);
                }
                catch (Exception e) { Hooks.WarnOnce("RecoveryPoints.particles", e); }
            }
        }

        public static void Update()
        {
            if (!Enabled || ++frame % 30 != 0) return;
            try
            {
                var udm = global::Last.Management.UserDataManager.Instance();
                if (udm == null) return;
                int map = udm.CurrentMapId;
                var ents = Object.FindObjectsOfType<FieldEntity>();
                if (ents == null) return;
                if (map == lastMap && ents.Length == lastCount) return;
                lastMap = map;
                lastCount = ents.Length;
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
                    TintObject(best.gameObject);
                    n++;
                    dists.Add(bd.ToString("0.##"));
                }
                Tinted += n;
                Last = $"map {map}: {n} of {triggers.Count} recovery save point(s) tinted (trigger-sparkle distance {string.Join(", ", dists)}; " +
                       $"{ents.Length} field entities, {effectClass} effect entities, {type10} of object type 10)";
                Hooks.NoteOnce("bnw-recovery-" + map, () => "[events] recovery points: " + Last);
            }
            catch (Exception e) { Hooks.WarnOnce("RecoveryPoints", e); }
        }
    }
}
