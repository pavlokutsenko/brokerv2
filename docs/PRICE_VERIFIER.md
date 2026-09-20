# Price verifier role

Select **Сборщик цен**, launch the client from the collector, enter the market, and press **Запустить сбор**. This role does not require a configured center zone.

Set **СЕРВЕР API** to the reachable API address. `127.0.0.1` is valid only when the collector and server run on the same computer; remote collectors use the server's LAN/VPN/public address. Queue state, leases, retries, and price results live on that server, so broker and price collectors do not share local files.

The collector sends its live player coordinates to the server. The server leases the nearest trader within the highest priority class. The embedded `BrokerWorker.exe` prepares the LU4 target/shop hooks once per client session, sends the normal repeated target action that makes the client path to the trader, suppresses target/shop UI, captures the shop response, clears the target, and returns every row with price, quantity, base price, buy count, and enchant level.

Every live PID receives an isolated runtime directory. Hook state, resolved session pointers, actor snapshots, and capture state are never shared between broker and price-verifier clients running at the same time.

The first task for a new client process performs signature/UObject discovery and can take around 100 seconds on the current client. The verified warm path reuses the hooks and took 6.9 seconds in the live test on 2026-09-20. The collector renews the server lease every 30 seconds during either path.

On a read or movement failure the collector reports the error and releases the task for delayed retry. If the worker hangs or the machine disappears, its lease expires and another collector can take the task. Stopping collection cleans the live hooks after the current bounded task exits; closing an owned client removes them with the process.
# Session warm-up

The verifier prepares its PID-bound target/session/active64 and ProcessEvent
capture state before claiming a server job. This one-time warm-up can take
about a minute on the current client, but no queue lease is held during it.
After warm-up, each claimed trader reuses the validated PID/image-bound caches;
only the actor snapshot and the shop action are refreshed per trader.

The active64 fallback may find one equally long RC4-state group per live LU4
client. It selects the group whose guarded PID field equals the verifier PID;
global uniqueness across every client is neither expected nor required.

Each PID has its own runtime directory, target hook, session cache and capture
state. Driver handles are opened with shared read/write access so the broker and
price verifier can scan different clients concurrently. The driver serializes
only packet encryption and its active64 state update with the crypto mutex.
