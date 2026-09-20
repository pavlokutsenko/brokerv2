# Stale capsule after world entry

Live PID 14348 showed the failure directly. The collector retained the position
used when the center was marked, approximately `(83358, 149584)`, and continued
reporting the character inside the 500-unit zone. A fresh guarded chain read
resolved controller `0x1F337D19070`, pawn `0x1F3395AD580`, capsule
`0x1F3AFDA7A50`, and current position `(83703.645, 147850.719, -3409.0)`.
The current point is about 1,767 units from the saved center.

The old capsule remained readable, so finite-coordinate validation could not
detect that the pawn had been replaced. The production reader now refreshes
the pawn and capsule pointers from the cached controller for every snapshot.
