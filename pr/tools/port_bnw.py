"""Brave New World port: the kit's porter (tools/port.py) with BNW's profile (profile_bnw.py)."""
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import kitpath  # noqa: E402,F401  (the modding kit's tools, when it's this repository's kit/ submodule)
import port  # noqa: E402
import profiles  # noqa: E402

if __name__ == "__main__":
    port.main(profiles.PROFILES["bnw"])
