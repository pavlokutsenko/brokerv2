"""Native short-segment collision checks following the captured ground height."""
import math
from ground_surface import GroundGrid
from capsule_probe import PawnCapsuleProbe
from capsule_profile import supported_capsule


class WalkGuard:
    def __init__(self,client,data):
        if not supported_capsule(client.capsule_radius,client.capsule_half_height):
            raise RuntimeError('pawn capsule does not fit a native-confirmed navigation profile')
        self.pawn_half_height=client.capsule_half_height
        self.probe=PawnCapsuleProbe(client)
        self.ground=GroundGrid(data.get('ground'))
        self.last_hit=None
        self.queries=0
        self.step_overrides=0
        self.rules=data.get('navigationRules',{})

    def clear(self,current,goal):
        self.last_hit=None
        start=tuple(current[:2]);length=math.dist(start,goal)
        # Split slopes so the query follows the ground, not a chord through stairs.
        count=max(1,math.ceil(length/40))
        xy=[tuple(start[k]+(goal[k]-start[k])*i/count for k in (0,1)) for i in range(count+1)]
        floor=[self.ground.height(*p) for p in xy]
        current_floor=current[2]-getattr(self,'pawn_half_height',23)
        floor=[z if z is not None else current_floor for z in floor]
        floor[0]=current_floor
        if max(floor)-min(floor)<3:
            xy=[xy[0],xy[-1]];floor=[floor[0],floor[-1]]
        for a,b,za,zb in zip(xy,xy[1:],floor,floor[1:]):
            if abs(zb-za)>28:
                self.last_hit={'blocked':True,'point':(*b,zb),'normal':(0,0,0),'outer':'ground_height_discontinuity'}
                return False
            # Small stair risers are walkable; keep the capsule above their tread.
            z=max(za,zb)+26
            hit=self.probe.trace((*a,z),(*b,z))
            self.queries+=1
            # The saved ground is below the live tread in parts of the temple.
            # An upward, low contact is a step; test one raised capsule sweep.
            # A facade with a horizontal normal still blocks movement.
            normal=hit.get('normal') or (0,0,0)
            rules=getattr(self,'rules',{})
            stair_prefix=rules.get('lowStairPrefix')
            low_stair=stair_prefix and (hit.get('outer') or '').startswith(stair_prefix) and \
                hit['point'][2]<=max(za,zb)+16
            point=hit.get('point') or (0,0,float('inf'))
            # This narrow BSP stair band produced a slanted tread normal
            # (~0.76 Z) while the pawn was already walking over it. Keep the
            # raised sweep requirement; unrelated sloping contacts stay blocked.
            rules=getattr(self,'rules',{})
            band=rules.get('riserX')
            temple_riser=(band and hit.get('outer')==rules.get('riserMesh') and
                          band[0]<=point[0]<=band[1] and normal[2]>.7 and
                          any(gate[2]<=point[1]<=gate[3] for gate in rules.get('gates',[])))
            low_tread=(normal[2]>.8 or temple_riser) and point[2]<=max(za,zb)+22
            if hit['blocked'] and (low_stair or low_tread):
                raised=self.probe.trace((*a,z+16),(*b,z+16))
                self.queries+=1
                if not raised['blocked']:
                    self.step_overrides=getattr(self,'step_overrides',0)+1
                hit=raised
            if hit['blocked']:
                self.last_hit=hit
                return False
        return True

    def clear_level_escape(self,current,goal):
        """Probe a short departure at the observed capsule height.

        The saved floor can be lower than the step the player currently stands
        on. A normal slope sweep then starts inside that step and falsely
        blocks every exit. Keep this fallback confined to bounded escape moves.
        """
        start=tuple(current[:2])
        if math.dist(start,goal)>250:
            return False
        destination_floor=self.ground.height(*goal)
        if destination_floor is not None and abs(destination_floor-(current[2]-getattr(self,'pawn_half_height',23)))>28:
            return False
        count=max(1,math.ceil(math.dist(start,goal)/40))
        points=[tuple(start[k]+(goal[k]-start[k])*i/count for k in (0,1)) for i in range(count+1)]
        for a,b in zip(points,points[1:]):
            probe_z=current[2]+23-getattr(self,'pawn_half_height',23)
            hit=self.probe.trace((*a,probe_z),(*b,probe_z))
            self.queries+=1
            if hit['blocked']:
                self.last_hit=hit
                return False
        self.last_hit=None
        return True

    def clear_outward_escape(self,current,goal):
        """Leave a shallow initial wall contact, with a clear sweep beyond it."""
        hit=self.last_hit or {};normal=hit.get('normal') or (0,0,0)
        distance=math.dist(current[:2],goal)
        if not (hit.get('blocked') and hit.get('time')==0 and 0<hit.get('penetration',0)<=2
                and abs(normal[2])<.2 and 30<=distance<=100): return False
        length=math.hypot(*normal[:2])
        if length<.8: return False
        nx,ny=normal[0]/length,normal[1]/length
        if ((goal[0]-current[0])*nx+(goal[1]-current[1])*ny)/distance<.8: return False
        # Recheck beyond the initial overlap. Only an ordinary game movement
        # follows; the pawn's position is never written to this shifted point.
        offset=12
        probe_z=current[2]+23-getattr(self,'pawn_half_height',23)
        shifted=(current[0]+nx*offset,current[1]+ny*offset,probe_z)
        followup=self.probe.trace(shifted,(*goal,probe_z));self.queries+=1
        if followup['blocked']:
            self.last_hit=followup;return False
        self.last_hit=None
        return True
