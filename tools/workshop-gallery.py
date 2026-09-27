"""Add screenshots / GIFs to the Steam Workshop item's image GALLERY (additional previews).

steamcmd's workshop_build_item can only set the single main preview image, and the web form
is manual. This drives Steam's own ISteamUGC flat API (StartItemUpdate -> AddItemPreviewFile ->
SubmitItemUpdate) through the GAME's steam_api64.dll, signed in via the running Steam desktop
client as a Stationeers (app 544550) client, exactly like the game's Facepunch library does.
While it runs (a few seconds) Steam shows you as playing Stationeers.

usage:
    python tools/workshop-gallery.py FOLDER [--dry-run] [--item 3776545141]
            [--owner <SteamID64>] [--note "..."]

Every .jpg/.jpeg/.png/.gif in FOLDER is ADDED in file-name order (prefix them 01-, 02-...).
Nothing is removed or replaced; remove pictures on the Workshop page's "Add/edit images &
videos". --dry-run signs in, checks the signed-in account owns the item and lists the files,
but changes nothing. Keep files under ~1 MB (Steam's preview guidance); animated GIFs play.
Requirements: Steam running and signed in as the item's owner; Stationeers installed. The owner
is read from the item's public details (its creator); pass --owner for an item that isn't public.
"""
import argparse
import ctypes
import json
import os
import sys
import tempfile
import time
import urllib.parse
import urllib.request

GAME = r"C:\Program Files (x86)\Steam\steamapps\common\Stationeers"
DLL = os.path.join(GAME, r"rocketstation_Data\Plugins\x86_64\steam_api64.dll")
APPID = 544550
ITEM = 3776545141
SUBMIT_ITEM_UPDATE_RESULT = 3404   # k_iSteamUGCCallbacks (3400) + 4
RESULT_SIZE = 16                   # EResult(4) + bool(1) + pad(3) + PublishedFileId(8), pack 8
INVALID_HANDLE = 0xFFFFFFFFFFFFFFFF
RESULTS = {1: "OK", 2: "Fail", 8: "InvalidParam", 9: "FileNotFound", 15: "AccessDenied",
           16: "Timeout", 25: "LimitExceeded", 42: "InsufficientPrivilege"}


def bind(api):
    c = ctypes
    api.SteamAPI_Init.restype = c.c_bool
    for n in ("SteamAPI_SteamUGC_v014", "SteamAPI_SteamUtils_v009", "SteamAPI_SteamUser_v020"):
        getattr(api, n).restype = c.c_void_p
    api.SteamAPI_ISteamUser_GetSteamID.restype = c.c_uint64
    api.SteamAPI_ISteamUser_GetSteamID.argtypes = [c.c_void_p]
    api.SteamAPI_ISteamUGC_StartItemUpdate.restype = c.c_uint64
    api.SteamAPI_ISteamUGC_StartItemUpdate.argtypes = [c.c_void_p, c.c_uint32, c.c_uint64]
    api.SteamAPI_ISteamUGC_AddItemPreviewFile.restype = c.c_bool
    api.SteamAPI_ISteamUGC_AddItemPreviewFile.argtypes = [c.c_void_p, c.c_uint64, c.c_char_p, c.c_int]
    api.SteamAPI_ISteamUGC_SubmitItemUpdate.restype = c.c_uint64
    api.SteamAPI_ISteamUGC_SubmitItemUpdate.argtypes = [c.c_void_p, c.c_uint64, c.c_char_p]
    api.SteamAPI_ISteamUtils_IsAPICallCompleted.restype = c.c_bool
    api.SteamAPI_ISteamUtils_IsAPICallCompleted.argtypes = [c.c_void_p, c.c_uint64, c.POINTER(c.c_bool)]
    api.SteamAPI_ISteamUtils_GetAPICallResult.restype = c.c_bool
    api.SteamAPI_ISteamUtils_GetAPICallResult.argtypes = [c.c_void_p, c.c_uint64, c.c_void_p, c.c_int,
                                                          c.c_int, c.POINTER(c.c_bool)]
    api.SteamAPI_RunCallbacks.restype = None
    api.SteamAPI_Shutdown.restype = None


def public_creator(item):
    """The item's creator SteamID64 from the public GetPublishedFileDetails, or None."""
    data = urllib.parse.urlencode({"itemcount": 1, "publishedfileids[0]": item}).encode()
    url = "https://api.steampowered.com/ISteamRemoteStorage/GetPublishedFileDetails/v1/"
    try:
        with urllib.request.urlopen(url, data, timeout=20) as r:
            d = json.load(r)["response"]["publishedfiledetails"][0]
        return int(d["creator"]) if d.get("result") == 1 and d.get("creator") else None
    except Exception:
        return None


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("folder")
    ap.add_argument("--item", type=int, default=ITEM)
    ap.add_argument("--owner", type=int, default=0, help="SteamID64; default = the item's public creator")
    ap.add_argument("--note", default="Screenshots and GIFs")
    ap.add_argument("--dry-run", action="store_true")
    a = ap.parse_args()

    folder = os.path.abspath(a.folder)
    files = sorted(os.path.join(folder, f) for f in os.listdir(folder)
                   if os.path.splitext(f)[1].lower() in (".jpg", ".jpeg", ".png", ".gif"))
    if not files:
        sys.exit("no images in " + folder)
    for f in files:
        kb = os.path.getsize(f) / 1024
        print("  %-40s %7.0f KB%s" % (os.path.basename(f), kb, "  <-- over 1 MB" if kb > 1024 else ""))
    owner = a.owner or public_creator(a.item)
    if not owner:
        sys.exit("could not read item %d's owner from its public details - pass --owner" % a.item)

    # SteamAPI_Init outside a Steam launch reads steam_appid.txt from the working directory.
    work = tempfile.mkdtemp(prefix="uia-gallery-")
    with open(os.path.join(work, "steam_appid.txt"), "w") as fh:
        fh.write(str(APPID))
    os.chdir(work)
    api = ctypes.CDLL(DLL)
    bind(api)
    if not api.SteamAPI_Init():
        sys.exit("SteamAPI_Init failed - is the Steam client running and signed in?")
    try:
        ugc = api.SteamAPI_SteamUGC_v014()
        utils = api.SteamAPI_SteamUtils_v009()
        user = api.SteamAPI_SteamUser_v020()
        me = api.SteamAPI_ISteamUser_GetSteamID(user)
        if me != owner:
            sys.exit("refusing: the signed-in account is not the item's owner")
        if a.dry_run:
            print("dry run: would add %d file(s) to item %d" % (len(files), a.item))
            return

        h = api.SteamAPI_ISteamUGC_StartItemUpdate(ugc, APPID, a.item)
        if h == INVALID_HANDLE or h == 0:
            sys.exit("StartItemUpdate refused item %d" % a.item)
        for f in files:
            ok = api.SteamAPI_ISteamUGC_AddItemPreviewFile(ugc, h, f.encode("utf-8"), 0)   # 0 = image
            print(("queued  " if ok else "REFUSED ") + os.path.basename(f))
        call = api.SteamAPI_ISteamUGC_SubmitItemUpdate(ugc, h, a.note.encode("utf-8"))
        if call == 0:
            sys.exit("SubmitItemUpdate returned no call handle")
        failed = ctypes.c_bool(False)
        deadline = time.time() + 300
        while time.time() < deadline:
            api.SteamAPI_RunCallbacks()
            if api.SteamAPI_ISteamUtils_IsAPICallCompleted(utils, call, ctypes.byref(failed)):
                break
            time.sleep(0.25)
        else:
            sys.exit("timed out waiting for Steam (the update may still complete)")
        buf = (ctypes.c_ubyte * RESULT_SIZE)()
        got = api.SteamAPI_ISteamUtils_GetAPICallResult(utils, call, buf, RESULT_SIZE,
                                                        SUBMIT_ITEM_UPDATE_RESULT, ctypes.byref(failed))
        if not got or failed.value:
            sys.exit("could not read the submit result (io failure=%s)" % failed.value)
        res = int.from_bytes(bytes(buf[0:4]), "little", signed=True)
        print("submit result: %d (%s)%s" % (res, RESULTS.get(res, "?"),
              "  - the account must accept the Workshop legal agreement" if buf[4] else ""))
        if res != 1:
            sys.exit(1)
    finally:
        api.SteamAPI_Shutdown()


if __name__ == "__main__":
    main()
