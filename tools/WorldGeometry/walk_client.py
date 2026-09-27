"""PID-scoped movement-only access through the established ProcessEvent bridge."""
import contextlib
import ctypes
import io
import json
import math
import os
from pathlib import Path
import struct
import time

from inspect_world import BUILD, GWORLD, GOBJECTS, Lu4MemoryClient, Memory, Survey, pe_info, read_object, validate_world_slot
from inspect_collision import Reflection
import process_event_shop_capture as capture
from invoke_process_event import invoke
from capsule_profile import supported_capsule
from lu4_target_controller import read_exact

FUNCTIONS = {
    "move": (248406, "Move to Location by Keyboard", "ControllerPC_C", 32),
    "stop": (248419, "StopIfMove", "ControllerPC_C", 8),
    "camera": (248408, "Get Move Camera Direction", "ControllerPC_C", 24),
}
SHOP_FUNCTIONS = [(19048, "PlayerShopBuyItemsList", 24), (19050, "PlayerShopNewBuyItemsList", 40),
                  (19052, "PlayerShopNewSellItemsList", 48), (19054, "PlayerShopSellItemsList", 32)]


class WalkClient:
    def __init__(self, pid):
        self.pid = pid
        self.directory = Path(os.environ["LOCALAPPDATA"]) / "PriceCheckCollector/research/market-walk" / str(pid)
        self.directory.mkdir(parents=True, exist_ok=True)
        self.stop_file = self.directory / "STOP"
        if os.environ.get('PRICECHECK_STOP_FILE'):
            self.stop_file=Path(os.environ['PRICECHECK_STOP_FILE'])
        self.client = Lu4MemoryClient()
        self.base = self.client.process_base(pid)
        self.m = Memory(self.client, pid)
        pe = pe_info(self.m, self.base)
        if (pe["timestamp"], pe["image_size"]) != BUILD:
            raise RuntimeError("unsupported client build")
        self.s = Survey(self.m, self.base)
        self.world = validate_world_slot(self.m, self.base+GWORLD, self.base, BUILD[1])
        if not self.world:
            raise RuntimeError("world/controller guard failed")
        capsule=self.world['player_capsule']
        if self.s.describe(capsule)['class']!='CapsuleComponent':
            raise RuntimeError('player capsule class changed')
        radius=self.m.unpack('<f',capsule+0x544,0)
        half_height=self.m.unpack('<f',capsule+0x540,0)
        scale=struct.unpack('<3d',self.m.read(capsule+0x210,24))
        if not 0<radius<100 or not all(math.isfinite(v) and 0<abs(v)<10 for v in scale):
            raise RuntimeError('invalid capsule radius/scale')
        self.capsule_radius=radius*max(abs(v) for v in scale)
        self.capsule_half_height=half_height*max(abs(v) for v in scale)
        if not self.capsule_radius<=self.capsule_half_height<100:
            raise RuntimeError('invalid capsule half-height')
        self.execution_clearance=max(24,self.capsule_radius+15)
        self.cleanup_callbacks=[]
        self.shops=None
        self.stopped_at=0
        self.stopped_position=None
        self.functions = {key: self.function(*value) for key, value in FUNCTIONS.items()}
        self.owned = False
        self.mutex = None
        self.original_paths = capture.STATE_PATH, capture.ROUTE_PATH, capture.FUNCTIONS_PATH
        self.initial_selected = self.m.u64(self.world["controller"]+0x898)

    def wait_navigation_capsule(self, seconds=20):
        """Wait for the player's spawn capsule to settle before navigation.

        A newly launched client briefly reports another character capsule.
        Read only this already-owned game's current component. Confirmed human
        and male/female dwarf shapes fit the conservative9x23 map envelope.
        """
        deadline=time.monotonic()+seconds;stable=0;previous=None;effective=None
        capsule=self.world['player_capsule']
        while time.monotonic()<deadline:
            if self.cancelled():
                raise RuntimeError('navigation readiness cancelled')
            self.m.pages.clear()
            if self.m.u64(self.base+GWORLD)!=self.world['world'] or \
                    self.m.u64(self.world['controller']+0x2D0)!=self.world['player_actor']:
                raise RuntimeError('world/pawn changed during navigation readiness')
            radius=self.m.unpack('<f',capsule+0x544,0)
            half=self.m.unpack('<f',capsule+0x540,0)
            scale=struct.unpack('<3d',self.m.read(capsule+0x210,24))
            factor=max(abs(v) for v in scale) if all(math.isfinite(v) for v in scale) else float('nan')
            effective=(radius*factor,half*factor)
            valid=supported_capsule(*effective)
            stable=stable+1 if valid and previous==effective else (1 if valid else 0)
            previous=effective
            if stable>=2:
                self.capsule_radius,self.capsule_half_height=effective
                self.execution_clearance=max(24,self.capsule_radius+15)
                return
            time.sleep(.25)
        raise RuntimeError(f'navigation capsule did not settle to a native-confirmed profile; observed {effective}')

    def function(self, index, name, owner, size):
        obj = read_object(self.m, self.base+GOBJECTS, index)
        d = self.s.describe(obj)
        if d["class"] != "Function" or d["name"] != name or self.s.name(self.m.u64(obj+0x20)) != owner or self.m.unpack("<H",obj+0xB6,0) != size:
            raise RuntimeError(f"function guard failed: {name}")
        return obj

    def position(self):
        # The imported reader caches pages for one-shot diagnostics. A live
        # controller must refresh world/pawn/target pages on every observation.
        self.m.pages.clear()
        w = self.world
        if self.m.u64(self.base+GWORLD) != w["world"] or self.m.u64(w["controller"]+0x2D0) != w["player_actor"]:
            raise RuntimeError("world/pawn changed")
        p = struct.unpack("<3d",self.m.read(w["player_capsule"]+0x1F0,24))
        if not all(math.isfinite(v) and abs(v)<1e7 for v in p):
            raise RuntimeError("invalid player position")
        return p

    def install(self):
        k = ctypes.WinDLL("kernel32",use_last_error=True)
        k.CreateMutexW.argtypes=[ctypes.c_void_p,ctypes.c_bool,ctypes.c_wchar_p]
        k.CreateMutexW.restype=ctypes.c_void_p
        k.CloseHandle.argtypes=[ctypes.c_void_p]
        self.kernel = k
        self.mutex = k.CreateMutexW(None,False,f"Local\\PriceCheckMarketWalk-{self.pid}")
        if not self.mutex or ctypes.get_last_error()==183:
            raise RuntimeError("another market walk owns this PID")
        capture.STATE_PATH = self.directory / "capture-state.json"
        capture.ROUTE_PATH = self.directory / "route-input.json"
        capture.FUNCTIONS_PATH = self.directory / "function-input.json"
        current=self.m.read(self.world["process_event"],len(capture.PROLOGUE))
        if current != capture.PROLOGUE:
            # The previous route can leave its verified ProcessEvent bridge in
            # place. Reuse it instead of rewriting a live game-thread entry
            # point between every claimed batch.
            saved=capture.prepare_install_state(self.pid)
            if saved is None or current != bytes.fromhex(saved['patch_hex']):
                raise RuntimeError("ProcessEvent already modified by another owner")
        matches=[]
        for index,name,size in SHOP_FUNCTIONS:
            obj=read_object(self.m,self.base+GOBJECTS,index)
            d=self.s.describe(obj)
            if d["name"]!=name or d["class"]!="Function" or self.m.unpack("<H",obj+0xB6,0)!=size:
                raise RuntimeError("capture event guard failed")
            matches.append({"name":name,"object":obj,"name_id":self.m.i32(obj+0x18)})
        capture.ROUTE_PATH.write_text(json.dumps({"pid":self.pid,"process_event":hex(self.world["process_event"])}),encoding="utf-8")
        capture.FUNCTIONS_PATH.write_text(json.dumps({"pid":self.pid,"matches":matches}),encoding="utf-8")
        with contextlib.redirect_stdout(io.StringIO()):
            capture.install(self.pid)
        self.owned=True

    def move(self, point):
        self.stopped_position=None
        if self.shops:
            self.shops.before_move()
            if self.cancelled():
                return
        source=self.position()
        if math.dist(source[:2],point)>260:
            raise RuntimeError("movement endpoint exceeds the 260-unit local limit")
        params=struct.pack("<4d",point[0],point[1],source[2],0)
        invoke(self.world["controller"],self.functions["move"],params)

    def stop(self, force=False):
        if self.shops:
            self.shops.active.clear()
        if self.owned:
            # A prior movement reply can arrive after the first stop request.
            # Keep issuing the ordinary stop while waiting for observed rest.
            start=time.monotonic();last_sent=0;stable_since=start
            anchor=self.position()
            previous=getattr(self,'stopped_position',None)
            if (not force and previous is not None and start-getattr(self,'stopped_at',0)<=3.5
                    and math.dist(anchor[:2],previous[:2])<=1):
                return  # No intervening move; already observed 1.5 seconds of rest.
            self.stopped_position=None
            while time.monotonic()-start<3.5:
                now=time.monotonic()
                if now-last_sent>=.20:
                    invoke(self.world["controller"],self.functions["stop"],struct.pack("<Q",self.world["player_actor"]))
                    last_sent=time.monotonic()
                current=self.position()
                if math.dist(current[:2],anchor[:2])>1:
                    anchor=current;stable_since=time.monotonic()
                if time.monotonic()-stable_since>=(1.5 if self.shops else .75):
                    self.stopped_at=time.monotonic();self.stopped_position=current
                    return
                time.sleep(.045)
            raise TimeoutError("stop did not settle within 3.5 seconds")

    def pause_for_plan(self):
        """Ordinary stop command before a short replan, without final-stop waiting."""
        if self.shops: self.shops.active.clear()
        self.stopped_position=None
        if self.owned:
            invoke(self.world['controller'],self.functions['stop'],
                   struct.pack('<Q',self.world['player_actor']))

    def camera_direction(self):
        raw=invoke(self.world["controller"],self.functions["camera"],bytes(24))
        result=struct.unpack("<3d",raw)
        if not all(math.isfinite(v) for v in result):
            raise RuntimeError("invalid camera direction")
        return result

    def cancelled(self):
        return self.stop_file.exists() or bool(ctypes.windll.user32.GetAsyncKeyState(0x77)&0x8000)

    def release_command_buffer(self,address):
        """Free external parameters only after the owned native command finished."""
        try:
            state=capture.load_state()
            if int(state['pid'])!=self.pid: return False
            trigger,status=struct.unpack('<II',read_exact(self.client,self.pid,int(state['command_address']),8))
            if trigger!=0 or status not in (0,2): return False
        except Exception:
            # Uncertain command lifetime: retain this allocation until PID exit.
            return False
        self.client.free_process_memory(self.pid,address)
        return True

    def close(self):
        try:
            if self.owned:
                try:
                    self.stop(force=True)
                finally:
                    try:
                        for cleanup in reversed(self.cleanup_callbacks):
                            cleanup()
                        self.cleanup_callbacks.clear()
                    finally:
                        # The bridge remains installed across price routes and
                        # the center return. The broker transition owns its one
                        # explicit uninstall before installing the broker hook.
                        capture.set_suppress_ui(False)
                        capture.set_suppress_target_ui(False)
                        self.owned=False
        finally:
            capture.STATE_PATH,capture.ROUTE_PATH,capture.FUNCTIONS_PATH=self.original_paths
            if self.mutex:
                self.kernel.CloseHandle(self.mutex)
                self.mutex=None
            self.client.close()

    def __enter__(self):
        return self

    def __exit__(self,*_):
        self.close()
