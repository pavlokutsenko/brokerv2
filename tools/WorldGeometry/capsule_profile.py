"""Native-confirmed pawn sizes fitting the Giran 9 x 23 map envelope."""
import math

CONFIRMED_CAPSULES = ((9,23),(9,18),(5,19))

def supported_capsule(radius,half_height):
    return (math.isfinite(radius) and math.isfinite(half_height)
            and any(abs(radius-r)<.1 and abs(half_height-h)<.1
                    for r,h in CONFIRMED_CAPSULES))
