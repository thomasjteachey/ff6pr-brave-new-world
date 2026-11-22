"""Brave New World's monster images (tools/port_sprites.py images mode; bnw_data.json "monster_images";
built on the player's machine by plugin/Rules/MonsterImages.cs).

  pr/.venv/Scripts/python -m unittest tests.test_bnw_sprites -v

Checks the recipe against the mod's monster/monster_asset tables, that it carries no PR art (recolors
are color tables only), and, when the game is on this PC, that each image comes out as the porter made
it (the Python twin of the plugin's builder, tools/sprite_sheet.build; Rules.Tests runs the C# one).
"""
import csv
import json
import os
import sys
import unittest

HERE = os.path.dirname(os.path.abspath(__file__))
PR = os.path.dirname(HERE)
sys.path.insert(0, os.path.join(PR, "tools"))
import kitpath  # noqa: E402,F401
import port_sprites  # noqa: E402

MOD = os.path.join(PR, "mods", "BraveNewWorld")
MASTER = os.path.join(MOD, "master", "Assets", "GameAssets", "Serial", "Data", "Master")


def table(name):
    with open(os.path.join(MASTER, name + ".csv"), encoding="utf-8") as f:
        return {int(r["id"]): r for r in csv.DictReader(f)}


class MonsterImages(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        with open(os.path.join(MOD, "bnw_data.json"), encoding="utf-8") as f:
            cls.recipe = json.load(f).get("monster_images")
        if not cls.recipe:
            raise unittest.SkipTest("no monster_images in bnw_data.json")
        cls.monster, cls.asset = table("monster"), table("monster_asset")

    def test_rows(self):
        for img in self.recipe["images"]:
            if "monster" not in img:        # the final battle's tower: the game's own key, repainted
                self.assertEqual(img["key"], "MN_FF6_147_C00")
                continue
            i = img["monster"]
            aid = port_sprites.IMAGE_ASSET_BASE + i
            self.assertEqual(img["key"], f"{img['group'].upper()}_B{i:03d}")
            row = self.asset.get(aid)
            self.assertIsNotNone(row, f"monster_asset {aid}")
            self.assertEqual((row["group_name"], row["asset_name"]), (img["group"], f"b{i:03d}"))
            self.assertEqual(int(self.monster[i + 1]["monster_asset_id"]), aid, f"monster {i} uses its image")

    def test_no_pr_art(self):
        for img in self.recipe["images"]:
            if "overlay" in img:            # BNW's changed pixels only: most of the picture stays the PR's
                import base64, zlib
                drawn = 0
                for ov64 in [img["overlay"]] + [p["overlay"] for p in img.get("parts", [])]:
                    ov = zlib.decompress(base64.b64decode(ov64))
                    drawn += sum(1 for k in range(3, len(ov), 4) if ov[k])
                self.assertLess(drawn, img["w"] * img["h"] // 10, f"{img['key']}: {drawn} pixels")
                self.assertEqual(sorted(p["monster"] for p in img.get("parts", [])), list(range(344, 353)), "the nine parts")
            elif "from" in img:
                self.assertNotIn("pixels", img)
                self.assertLessEqual(len(img["map"]), 32, img["key"])
            else:   # BNW's own art (profile sprite_new_art)
                self.assertIn(img["monster"], {311, 37}, f"{img['key']}: only BNW's own sprites ship as pixels (Kaiser, Shemp)")

    def test_ids(self):
        body = {k: v for k, v in self.recipe.items() if k != "id"}
        self.assertEqual(self.recipe["id"], port_sprites.sha1(json.dumps(body, sort_keys=True).encode())[:16])

    def test_build_from_this_pc(self):
        try:
            from prkit import games
            game = games.get("ff6").install
        except Exception:
            game = None
        if not game:
            self.skipTest("no FF6 PR on this PC")
        import sprite_sheet
        for img in self.recipe["images"]:
            w, h, rgba, _ = sprite_sheet.build(img)
            self.assertEqual((w, h), (img["w"], img["h"]), img["key"])
            self.assertEqual(port_sprites.sha1(rgba), img["sha1"], img["key"])


if __name__ == "__main__":
    unittest.main()
