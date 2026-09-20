# Price verifier role

Select **Сборщик цен**, launch the client from the collector, enter the market, and press **Запустить сбор**. This role does not require a configured center zone.

The collector sends its live player coordinates to the server. The server leases the nearest trader within the highest priority class. The embedded `BrokerWorker.exe` prepares the LU4 target/shop hooks once per client session, sends the normal repeated target action that makes the client path to the trader, suppresses target/shop UI, captures the shop response, clears the target, and returns every row with price, quantity, base price, buy count, and enchant level.

The first task for a new client process performs signature/UObject discovery and can take around 100 seconds on the current client. The verified warm path reuses the hooks and took 6.9 seconds in the live test on 2026-09-20. The collector renews the server lease every 30 seconds during either path.

On a read or movement failure the collector reports the error and releases the task for delayed retry. If the worker hangs or the machine disappears, its lease expires and another collector can take the task. Stopping collection cleans the live hooks after the current bounded task exits; closing an owned client removes them with the process.
