using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Last.Battle;
using Last.Management;
using PrKit;
using UnityEngine;

namespace BraveNewWorld.PR
{
    // BNW's monster images (recolors, Kaiser, the final battle tower) and their bestiary thumbnails, made once from the
    // player's own game into BNW's Magicite folder (the kit's PrKit/MonsterImages.cs; recipe:
    // bnw_data.json "monster_images", from tools/port_sprites.py). Called from Plugin.Load, before
    // Magicite scans its folder on its first Update, so the images load on the same start. Each later
    // start only checks the marker.
    static class BnwMonsterImages
    {
        public static void Ensure(string dataPath)
        {
            RegisterHarness();
            var sw = Stopwatch.StartNew();
            try
            {
                var recipe = MonsterImageRecipe.FromJson(File.ReadAllText(dataPath), "monster_images");
                if (recipe == null) return;
                tower = recipe.Images.FirstOrDefault(i => i.Parts != null && i.Parts.Count > 0);
                var line = MonsterImages.Ensure(BepInEx.Paths.GameRootPath, MagiciteDir.FolderPath("BraveNewWorld"), recipe,
                    w => Plugin.L.LogWarning("[bnw] " + w));
                Plugin.L.LogInfo($"[bnw] {line} ({sw.ElapsedMilliseconds} ms)");
            }
            catch (Exception e)
            {
                Plugin.L.LogError("[bnw] monster images not made: " + e.Message);
            }
        }

        // The final battle's tower (MN_FF6_147_C00): BNW's statue parts are sprites that vanish when they die;
        // the PR paints the statue as one picture, so when a part monster dies its pixels go back to the
        // picture under it (the image without its parts: MonsterImages.BuildBase, from the player's game),
        // in the loaded texture itself; the picture as made comes back when the battle ends.
        static MonsterImage tower;
        static Dictionary<int, byte[]> partLayers;
        static byte[] towerBase;
        static readonly HashSet<int> seen = new HashSet<int>(), erased = new HashSet<int>();
        static Texture2D towerTex;
        static Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<Color32> towerFull;
        static bool inBattle;
        static string towerNote = "no tower in this data";

        public static void Track()
        {
            if (tower == null) return;
            try
            {
                var battle = BattlePlugManager.Instance();
                if (battle == null || !battle.IsBattle())
                {
                    if (inBattle) EndBattle();
                    return;
                }
                inBattle = true;
                partLayers ??= tower.Parts.ToDictionary(p => p.Monster, p => MonsterImages.PartLayer(tower, p));
                var present = new HashSet<int>();
                foreach (var u in battle.GetEnemyUnits())
                {
                    int id = u?.GetMonster()?.Id ?? 0;
                    if (!partLayers.ContainsKey(id)) continue;
                    if ((u.BattleUnitDataInfo?.Parameter?.CurrentHP ?? 0) > 0) { present.Add(id); seen.Add(id); }
                }
                foreach (var id in seen.Where(i => !present.Contains(i) && !erased.Contains(i)).ToList())
                {
                    erased.Add(id);
                    Erase(id);
                }
            }
            catch (Exception e)
            {
                tower = null;
                Plugin.L.LogWarning("[bnw] tower: off after an error: " + e.Message);
            }
        }

        static void Erase(int monster)
        {
            if (towerTex == null || towerTex.WasCollected)
            {
                towerTex = null;
                foreach (var t in Resources.FindObjectsOfTypeAll<Texture2D>())
                    if (t != null && t.name == tower.Key && t.width == tower.W && t.height == tower.H) { towerTex = t; break; }
                if (towerTex == null) { towerNote = "tower texture not loaded"; return; }
                if (!towerTex.isReadable) { towerNote = "tower texture not readable"; towerTex = null; return; }
                towerFull = towerTex.GetPixels32();
            }
            towerBase ??= MonsterImages.BuildBase(BepInEx.Paths.GameRootPath, tower,
                new Dictionary<string, Dictionary<string, BundleTexture>>(), w => Plugin.L.LogWarning("[bnw] tower: " + w));
            if (towerBase == null) { towerNote = "no base picture"; return; }
            int W = tower.W, H = tower.H, n = 0;
            var layer = partLayers[monster];
            var px = towerTex.GetPixels32();
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    int k = 4 * (y * W + x);
                    if (layer[k + 3] == 0) continue;
                    // the base, or a part still standing that covers this pixel too
                    var src = towerBase;
                    foreach (var (id, other) in partLayers)
                        if (id != monster && !erased.Contains(id) && other[k + 3] != 0) { src = other; break; }
                    px[(H - 1 - y) * W + x] = new Color32(src[k], src[k + 1], src[k + 2], 255);
                    n++;
                }
            towerTex.SetPixels32(px);
            towerTex.Apply(false, false);
            towerNote = $"erased monster {monster} ({n} pixels); erased so far: {string.Join(",", erased)}";
            Plugin.L.LogInfo("[bnw] tower: " + towerNote);
        }

        static void EndBattle()
        {
            inBattle = false;
            if (erased.Count > 0 && towerTex != null && !towerTex.WasCollected && towerFull != null)
            {
                towerTex.SetPixels32(towerFull);
                towerTex.Apply(false, false);
                Plugin.L.LogInfo("[bnw] tower: picture restored after the battle");
            }
            seen.Clear();
            erased.Clear();
            towerNote = "no part erased";
        }

        // Harness (tools/game_harness.py): load an asset the way the game does, to check that what
        // Magicite serves (new keys, new groups) reaches the game.
        //   resload <group> <key>       ResourceManager.RequestLoadAssetBundle<Sprite>, then the sprite
        //   resload book <id>           a bestiary entry (picture_book id): the monster image and the
        //                               habitat thumbnail names the bestiary makes, both loaded
        static string group1, key1, group2, key2, label;

        static void RegisterHarness()
        {
            Harness.Register("resload", ResLoad,
                "resload <group> <key> | resload book <picture_book id>: load a sprite as the game does and report it");
            Harness.Register("tower", _ => tower == null ? "no tower in this data"
                    : $"tower {tower.Key}: parts seen [{string.Join(",", seen)}], erased [{string.Join(",", erased)}]; {towerNote}",
                "tower: the final battle tower's statue parts seen and erased (BNW's parts vanish when they die)");
        }

        static string ResLoad(string[] a, int tick)
        {
            var rm = ResourceManager.Instance;
            if (rm == null) return "error: no ResourceManager";
            if (tick == 0)
            {
                group2 = key2 = null;
                if (a.Length >= 2 && a[0] == "book" && int.TryParse(a[1], out var id))
                {
                    var pb = Master.Get<Last.Data.Master.PictureBook>(id);
                    if (pb == null) return $"error: no picture_book {id}";
                    var md = new Last.OutGame.Library.MonsterData(pb);
                    string g = "", n = "", tg = "";
                    Last.OutGame.Library.LibraryResourceLoader.CreateMonsterAssetName(md, ref g, ref n);
                    Last.OutGame.Library.LibraryResourceLoader.CreateThumbnailGroupName(md, ref tg);
                    group1 = g; key1 = n; group2 = tg; key2 = "Thumbnail";
                    label = $"book {id} (monster {pb.MonsterId})";
                }
                else if (a.Length >= 2)
                {
                    group1 = a[0]; key1 = a[1];
                    label = $"{group1} {key1}";
                }
                else return "error: resload <group> <key> | resload book <id>";
                rm.RequestLoadAssetBundle<Sprite>(group1, key1, false);
                if (group2 != null) rm.RequestLoadAssetBundle<Sprite>(group2, key2, false);
                return null;
            }
            bool done1 = rm.CheckLoadAssetCompleted(group1, key1);
            bool done2 = group2 == null || rm.CheckLoadAssetCompleted(group2, key2);
            if (!(done1 && done2) && tick < 600) return null;
            return $"{label}: {Describe(rm, group1, key1, done1)}" + (group2 != null ? $"; {Describe(rm, group2, key2, done2)}" : "");
        }

        static string Describe(ResourceManager rm, string group, string key, bool done)
        {
            if (!done) return $"{group}/{key} not loaded";
            var sp = rm.GetAssetData<Sprite>(group, key);
            if (sp == null) return $"{group}/{key} loaded, no sprite";
            var tex = sp.texture;
            return $"{group}/{key} = sprite {sp.name} {sp.rect.width}x{sp.rect.height} (texture {tex?.name} {tex?.width}x{tex?.height})";
        }
    }
}
