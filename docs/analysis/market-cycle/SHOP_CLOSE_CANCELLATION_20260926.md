# Positive closure and reopening of a shop — 2026-09-26

## Confirmed cause

CharacterInfo kiosk=0 already changed RadarEntityStore.IsTrading to false,
but Snapshot exported only trading characters. CycleRadarPool and the worker
retained the previous trading observation and approached a standing character.
Absence in the native trading list alone is not evidence of closing a shop.

## V48 / V49

V48 exports ClosedTraders only for visible, previously trading characters with
an explicit kiosk=0 packet. Delete/visibility loss cannot create closure.
The existing worker also revalidates a previously seen actor missing from its
trading list with the established class/root/name/OID/type guards. Matching
kiosk=0 cancels the visit immediately; no new memory offsets or extra reader.
Logs: shop_closed(source character packet kiosk=0 / native kiosk=0),
approach_shop_closed. Pending target removed, no fake capture or server deletion;
other market targets continue. The next complete broker owns absence cleanup.

V49 adds the user's explicit reopening requirement: a newer positive1/3/8
observation re-enters the pool even for an already broker-known/read nickname
and the same ObjectID. Old positive frames cannot undo the closing signal.
Worker clears the old capture/request/retry/detour suppression on that actual
transition. Reopened captures use radar admission instead of an already
completed old server lease. Each capture sequence gets a distinct snapshot ID,
so reopening and re-reading within one route cannot be dropped as a duplicate.
The outbox still replays each individual snapshot idempotently after a crash.

## Verification / current state

V48 deployed804verified files15:02 local, owner6532/game19420/uploader19108
(rediscover). V48 first price route03224bf7c7364c6c9bd406c0143d5b28 ongoing;
no positive closure event observed by15:11. Do not call closure live-verified.
V49 deployed15:13 local:804verified files, owner18748/game20084/uploader19008
(rediscover). Auto-launch/collection requested; center8ded076ceeab471f9b84665f43708dda
preparing. Stage: workspace/market-cycle-v49-reopen-stage.
98geometry tests passed, including cancellation before move/request,
visibility loss vs matching positive closure, stale frame suppression, and
same-ID reopen after a capture. ModuleIsolation passed pool/packet tests,
broker-known same-ID reopen and distinct durable capture IDs.

V48 interrupted normally for V49 installation;22/22exact current accepted,
119rows,0positive closure events by shutdown. No complete V48 cycle claim.
## Real V49 evidence

Route9d614acdcade40869d0f64910a7f6bcf:47exact snapshots/221rows,
94requests/47target cancels and replies,0timeouts/invalid/unverified/error.
Server audit:45current210rows,2historical11rows (superseded rereads).
Follow-up426ef5de4a88420eb5dcbb496e43934e completed the remaining WineRack
and one new shop:2current15rows. Center e845d506301c4d09bd68476f54e83e02
completed15:24:00 local. Whole pass was exhausted; normal shutdown for V50
followed at center. No claim of multiple V49 cycles.

Kaldun (ObjectID1218473142): positive close15:17:01.851, no requests while
closed; same-ID new trading observation/admission15:17:41.863, read5rows
15:18:16.408, current server snapshot15:18:16.435. AncientLucifer
(ObjectID1283484059) read15:16:50 sequence4, closed15:19:58, reopened/admitted
15:20:49, reread15:21:18 sequence37. Server kept both distinct snapshot IDs,
sequence37 current and sequence4 historical. Positive closure/reopen and
same-ID reread after capture are LIVE VERIFIED. Source was character packet;
the guarded native kiosk=0 branch has not yet been observed live.

## V50 owned task acknowledgement

Live Kaldun price was current but its original NEW_LISTINGS task stayed LEASED:
V49 replaced the assigned target with a lease-free radar target even BEFORE
its first successful upload. Foreign leases are intentionally preserved by
server radar admission. Fixed in ReopenedCycleTarget: preserve the exact owned
ServerJob/token/revision for a reopened assigned shop's first read; after an
earlier successful upload, omit its completed old lease. New type/position come
from the validated native reply and radar admission remains enabled. No server
ownership rules weakened. ModuleIsolation asserts both cases.

V50 deployed15:24 local:804verified files, owner20616/game21188/uploader20808 (rediscover),
stage workspace/market-cycle-v50-reopen-lease-stage. Tests/build passed.
Next: observe several V50 full cycles and owned-lease completion on a real
reopening, audit current prices/center/immediate broker. No purchases/second reader.

V50 live first broker7ef929e830a2432d931b40f39b0b819c, started15:26:05,
finished15:27:51, pool3unknown. Route19ef7678f87344e6aeef5b51bcc232ff ongoing.
Joikin: native-admitted15:28:45, guarded native kiosk=0 close15:28:48.114;
no subsequent request/requeue by15:30:52. Native positive-closure fallback is
now LIVE VERIFIED as well. Full valid broker removed still-absent Burzik and
VanCleef: is_active=false, queue CANCELLED; no deletion at the closing packet.
